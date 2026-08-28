# Circle to Search — план мультипровайдерной архитектуры

## Цель

Подготовить внутреннюю архитектуру плагина к нескольким сервисам визуального поиска, сохранив
Google Lens текущим провайдером по умолчанию и не добавляя на этом этапе переключатель в chip,
overlay или настройки.

После этого этапа Google-специфичные WebView2, DOM automation и loading overlay должны оставаться
внутри Google-провайдера, а Yandex должен работать по собственному HTTP-сценарию без создания
Google-окна. Добавление выбора провайдера в chip выполняется отдельным следующим этапом поверх
готового маршрутизатора.

## Проверенное текущее состояние

- `Main.cs` является тонким адаптером Flow Launcher, а runtime-граф собирается в
  `CTS/CompositionRoot.cs`; это нужно сохранить.
- `CTS/Search/IVisualSearchProvider.cs` уже задаёт пригодный общий контракт:
  `SearchAsync(byte[] png, CancellationToken cancel)` возвращает либо URL, либо `Handled()` для
  результата, полностью показанного самим провайдером.
- `CTS/Search/WebView2VisualSearchProvider.cs` и `CTS/Search/WebView2SearchWindow.cs` фактически
  являются Google Lens-компонентами, хотя их имена выглядят универсальными.
- Host-level loading overlay, навигация Lens, загрузка PNG через DOM/DataTransfer и обработка
  `Ctrl+W` находятся в `WebView2SearchWindow`; Yandex-код их не вызывает.
- `CTS/Search/YandexImagesProvider.cs` уже реализует `IVisualSearchProvider`: отправляет PNG через
  HTTP и возвращает URL, который координатор открывает в системном браузере.
- `CTS/CompositionRoot.cs` создаёт только `WebView2VisualSearchProvider` и напрямую передаёт его в
  `SearchCoordinator`.
- `CTS/Search/SearchCoordinator.cs` хранит один `_provider`; выбора, валидации ID и fallback сейчас
  нет. Сообщение `BrowserAutomationFailed` жёстко упоминает Google.
- `CTS/Settings/PluginSettings.cs` не хранит ID выбранного провайдера.
- `CTS/Capture/SelectionOutcome.cs` содержит только выделенную область и bitmap. На этом этапе его
  менять не требуется, потому что provider picker в overlay не реализуется.
- `CTS/Settings/SettingsPanel.cs` и `CTS/Trigger/QueryTrigger.cs` содержат Google-специфичный текст.
- `PluginRuntime` напрямую владеет одним `WebView2SearchWindow`; `YandexImagesProvider` создаёт
  `HttpClient`, но сейчас не освобождает его явно.

## Желаемая схема после этапа

```text
PluginSettings.SearchProviderId (default: google-lens)
                         |
SearchCoordinator -------+----> VisualSearchProviderRouter
                                      |-- google-lens
                                      |     `-- GoogleLensProvider
                                      |           `-- GoogleLensWindow
                                      |                 `-- Google-only loading/DOM/WebView2
                                      |
                                      `-- yandex-images
                                            `-- YandexImagesProvider
                                                  `-- HTTP -> external results URL
```

Координатор выбирает стратегию по стабильному ID, но не знает её реализации. Маршрутизатор
выполняет только маршрутизацию visual-search и не выдаёт произвольные сервисы вызывающему коду,
поэтому он не должен превращаться в общий service locator.

## Границы этапа

### Входит

- стабильные ID и read-only metadata провайдеров;
- маршрутизатор Google/Yandex с безопасным fallback;
- скрытая настройка активного провайдера с Google по умолчанию;
- чёткие Google-имена для WebView2-компонентов;
- provider-aware сообщения и логирование;
- корректное владение и освобождение provider-specific ресурсов;
- unit-тесты маршрутизации, fallback, coordinator flow и disposal;
- ручная проверка обеих веток без изменения overlay.

### Не входит

- кнопка, dropdown, popup, иконки или hit testing провайдеров в chip;
- изменение `OverlayWindow`, `OverlayVisualFactory`, `OverlayOptions` или `SelectionOutcome` ради
  выбора провайдера;
- публичный selector в `SettingsPanel`;
- изменение дизайна loading overlay;
- общий WebView, общая loading-анимация или capability-флаг вроде `NeedsLoadingOverlay`;
- новые поисковые сервисы, кроме регистрации уже существующих Google Lens и Yandex Images;
- silent install, упаковка WebView2 Runtime или изменение способа загрузки изображения.

## Архитектурные решения

### 1. Стабильная идентичность провайдера

Добавить `CTS/Search/SearchProviderId.cs` со строковыми константами:

```csharp
public static class SearchProviderIds
{
    public const string GoogleLens = "google-lens";
    public const string YandexImages = "yandex-images";
}
```

Для метаданных добавить небольшой immutable record, например
`SearchProviderDescriptor(string Id, string DisplayName)`. На этом этапе не добавлять flags для
WebView2, loading overlay, browser mode или других implementation details. При необходимости
future UI сможет получить упорядоченный read-only список descriptor-ов.

Сравнивать ID через `StringComparer.OrdinalIgnoreCase`, но в результатах и логах использовать
каноническое значение из descriptor-а.

### 2. Provider router вместо одного `_provider`

Добавить конкретный `VisualSearchProviderRouter`, который получает в конструкторе:

- набор регистраций `descriptor + factory`;
- ID провайдера по умолчанию;
- `PluginLog`.

Публичная операция должна принимать requested provider ID и PNG, выполнять поиск и возвращать
effective provider metadata вместе с `VisualSearchOutcome`. Например:

```csharp
Task<RoutedVisualSearchOutcome> SearchAsync(
    string? requestedProviderId,
    byte[] png,
    CancellationToken cancel);
```

`RoutedVisualSearchOutcome` должен содержать канонический effective provider ID, display name,
обычный `VisualSearchOutcome` и признак fallback. Это позволит координатору строить корректные
сообщения, не проверяя конкретные классы.

Правила маршрутизации:

1. Известный ID выбирает соответствующий provider.
2. `null`, пустой или неизвестный ID выбирает Google Lens как default.
3. Fallback логируется один раз на сессию как warning без изображения, URL, cookies или иных
   пользовательских данных.
4. Исключение factory или provider не запускает другой поисковый сервис автоматически: это
   обычная ошибка текущей сессии. Fallback предназначен только для неизвестного ID, а не для
   скрытой подмены выбранного сервиса после его сбоя.

Регистрации должны быть lazy. Не создавать `GoogleLensWindow` и его STA dispatcher, пока впервые
не выбран Google. Аналогично не создавать `HttpClient` Yandex до первого Yandex-поиска. Для
потокобезопасной однократной инициализации использовать `Lazy<T>` с
`LazyThreadSafetyMode.ExecutionAndPublication` либо эквивалентную локальную синхронизацию.

Маршрутизатор не должен возвращать наружу `IVisualSearchProvider`, иметь generic
`GetService<T>()` или разрешать зависимости других подсистем.

### 3. Явно отделить Google Lens от технологии WebView2

Переименовать без изменения поведения:

- `CTS/Search/WebView2SearchWindow.cs` -> `CTS/Search/GoogleLensWindow.cs`;
- `WebView2SearchWindow` -> `GoogleLensWindow`;
- `WebView2SearchStatus` -> `GoogleLensSearchStatus`;
- `CTS/Search/WebView2VisualSearchProvider.cs` -> `CTS/Search/GoogleLensProvider.cs`;
- `WebView2VisualSearchProvider` -> `GoogleLensProvider`;
- соответствующие test-файлы и test-классы.

`GoogleLensWindow` продолжает единолично владеть:

- проверкой/инициализацией WebView2;
- постоянным Google profile;
- Google URL и проверкой result URL;
- DOM/DataTransfer upload;
- host-level loading overlay;
- Google-specific navigation handling и `Ctrl+W`.

Ничего из этого не переносить в `IVisualSearchProvider`, router или `SearchCoordinator`.

### 4. Скрытая настройка выбранного провайдера

Добавить в `PluginSettings`:

```csharp
public string SearchProviderId { get; set; } = SearchProviderIds.GoogleLens;
```

На этом этапе не создавать элемент управления для неё. Значение существует как backend seam,
используется в тестах и позволит следующему этапу сохранять выбор из chip без изменения формата
настроек.

В начале каждой search session `SearchCoordinator` должен snapshot-нуть строку из settings и
использовать одно значение до завершения этой сессии. Не читать mutable setting повторно после
crop/upload.

Не перезаписывать и не сохранять неизвестное значение автоматически: router применяет runtime
fallback, а исходный JSON остаётся доступным для диагностики и будущего возвращения временно
отсутствующего provider-а.

### 5. Изменения `SearchCoordinator`

- Заменить поле и constructor parameter `IVisualSearchProvider` на
  `VisualSearchProviderRouter`.
- После выделения и crop вызвать router с provider ID, зафиксированным для текущей сессии.
- В логах начала upload и результата указывать только effective provider ID.
- Сохранить существующие состояния `Idle -> Selecting -> Uploading -> Idle`, single-session gate,
  cancel semantics, disposal bitmap и открытие `ResultsUrl`.
- Для `Handled()` не открывать внешний браузер, как сейчас.
- Формировать browser-related сообщения с display name effective provider-а:
  WebView2 Runtime требуется конкретному provider-у, а automation failure сообщает имя
  provider-а без hard-coded `Google` в coordinator.
- Не добавлять retry через другой provider и не отправлять один screenshot двум сервисам.

Сам `VisualSearchOutcome` и `IVisualSearchProvider` можно оставить неизменными: различие между
provider-handled WebView и URL для внешнего браузера уже выражено корректно.

### 6. Composition и жизненный цикл

Все factory и регистрации собрать только в `CompositionRoot.Create`:

- `google-lens` factory создаёт `GoogleLensWindow`, затем `GoogleLensProvider`;
- `yandex-images` factory создаёт `YandexImagesProvider(log)`;
- router получает обе регистрации и `google-lens` как default;
- coordinator получает router;
- `PluginRuntime` владеет router вместо конкретного Google window.

Определить однозначное владение: router освобождает только созданные им provider instances и не
форсирует lazy factory во время `Dispose`. `GoogleLensProvider` освобождает принадлежащий ему
`GoogleLensWindow`; `YandexImagesProvider` реализует `IDisposable` и освобождает `HttpClient`.
Повторный `Dispose` должен быть безопасным, каждый созданный ресурс освобождается ровно один раз.

`GoogleLensWindow.GetRuntimeVersion` после переименования остаётся статическим probe и может
использоваться settings/runtime UI без создания Google provider-а.

### 7. Текст, который сейчас связан с Google

- В `SearchCoordinator` убрать hard-coded Google из automation failure, используя effective
  descriptor.
- В `QueryTrigger` заменить subtitle на нейтральный текст visual search либо получать read-only
  display name активного provider-а через router. Не добавлять selector или action для смены.
- В `SettingsPanel` можно показывать текущий effective provider как read-only строку; не добавлять
  ComboBox, radio buttons или обработчик изменения.
- WebView2 runtime card/строка должна явно говорить, что runtime относится к Google Lens, а не ко
  всем провайдерам.
- Новые строки поместить в существующий `CTS/Ui/UiStrings.cs`, если соответствующий участок уже
  переведён на централизованные строки; не размножать новые literal-ы по UI.

## Порядок реализации

1. Добавить provider IDs, descriptor, registration/result records и unit-тесты нормализации ID.
2. Реализовать lazy `VisualSearchProviderRouter`, fallback и disposal; полностью покрыть его
   отдельными deterministic tests.
3. Переименовать Google Lens window/provider/status и обновить ссылки и тесты без функциональных
   изменений. Сразу запустить provider tests, чтобы rename не смешивался с изменением поведения.
4. Сделать `YandexImagesProvider` disposable и добавить тест освобождения через controllable
   handler либо provider factory seam.
5. Добавить скрытый `PluginSettings.SearchProviderId` с Google default.
6. Перевести `SearchCoordinator` на router, добавить provider-aware logging/errors и обновить
   coordinator tests.
7. Пересобрать composition root на две lazy регистрации и передать владение router-у.
8. Убрать misleading Google hard-code из query/settings read-only текста, не добавляя controls.
9. Прогнать deterministic suite, затем opt-in live tests Google и Yandex по отдельности.
10. Проверить release build и вручную выполнить обе ветки, временно меняя
    `SearchProviderId` в сохранённых настройках или через test-only composition — без временного
    production UI.

## Тесты

### `VisualSearchProviderRouterTests`

- известный `google-lens` вызывает только Google factory/provider;
- известный `yandex-images` вызывает только Yandex factory/provider;
- ID сравнивается без учёта регистра, outcome содержит канонический ID;
- `null`, whitespace и неизвестный ID используют Google default и помечают fallback;
- неизвестный ID не создаёт неизвестных ресурсов и пишет warning;
- повторные вызовы одного provider-а используют один lazy instance;
- выбор Yandex не создаёт Google window/provider;
- исключение выбранного provider-а не вызывает fallback provider;
- cancel token передаётся без замены;
- dispose освобождает только уже созданные providers, ровно один раз, и не активирует factories.

### Обновлённые `SearchCoordinatorTests`

- Google `Handled()` не открывает внешний URL;
- Yandex `Ok(url)` открывает URL один раз;
- requested и effective provider ID корректно попадают в router/log seam;
- неизвестная настройка приводит к Google fallback и нормальному завершению;
- provider-aware WebView2/automation error содержит display name effective provider-а;
- timeout/network/status failures сохраняют существующее поведение;
- cancel во время selection не создаёт provider;
- cancel/повторный hotkey во время upload сохраняют текущую state machine;
- bitmap освобождается и при provider failure;
- одна сессия использует snapshot provider ID даже при изменении settings во время selection.

### Provider tests

- переименованные `GoogleLensProviderTests` сохраняют все существующие mappings;
- `YandexImagesProviderTests` сохраняют endpoint, content type, parsing, policy и failure mappings;
- отдельный lifecycle test подтверждает освобождение HTTP resources;
- `GoogleLensLiveSearchTests`: два последовательных поиска доходят до `ResultsReady`, loading
  overlay и follow-up page не регрессируют;
- `YandexLiveSearchTests`: результат остаётся разрешённым `YandexResultUrlPolicy`.

## Команды проверки

Каждая зелёная test-команда считается успешной только при ненулевом числе реально выполненных
тестов.

```powershell
dotnet build .\CircleToSearch.csproj
dotnet test .\tests\CircleToSearch.Tests\CircleToSearch.Tests.csproj
dotnet test .\tests\CircleToSearch.Tests\CircleToSearch.Tests.csproj `
  --filter "FullyQualifiedName~GoogleLensLiveSearchTests" `
  -e CTS_WEBVIEW2_LIVE=1
dotnet test .\tests\CircleToSearch.Tests\CircleToSearch.Tests.csproj `
  --filter "FullyQualifiedName~YandexLiveSearchTests" `
  -e CTS_LIVE=1
powershell -File .\build_release.ps1 -NoPause
git diff --check
```

После rename сохранить проверенные env variable names: `CTS_WEBVIEW2_LIVE=1` для Google Lens и
`CTS_LIVE=1` для Yandex.

## Ручная проверка

1. Чистые/default settings: выделение открывает Google Lens, host loader скрывает промежуточную
   Google page, `Ctrl+W` закрывает окно.
2. `SearchProviderId = "yandex-images"`: выделение не создаёт Google window и не показывает его
   loading overlay; результат открывается в системном браузере.
3. `SearchProviderId = "missing-provider"`: поиск выполняется через Google, в log есть безопасный
   fallback warning, settings-файл самопроизвольно не переписывается.
4. Без WebView2 Runtime: Google возвращает runtime-specific failure; Yandex продолжает работать и
   не показывает Google/WebView2 prompt.
5. Reload plugin data после использования только Yandex, только Google и обоих providers не
   оставляет STA threads, WebView processes, hotkey hook или незакрытые HTTP resources.

## Acceptance criteria

- [ ] Google Lens остаётся поведением по умолчанию и визуально работает как до рефакторинга.
- [ ] Google loading overlay существует только внутри `GoogleLensWindow`.
- [ ] Yandex path не создаёт и не показывает Google/WebView2 window.
- [ ] `SearchCoordinator` не зависит от конкретного provider class и не содержит hard-coded
      `Google` в общих failure branches.
- [ ] Добавление третьего provider-а требует новой реализации `IVisualSearchProvider` и одной
      регистрации в `CompositionRoot`, но не изменений overlay/coordinator state machine.
- [ ] Неизвестный provider ID безопасно использует Google default без изменения settings-файла.
- [ ] В одном поиске screenshot отправляется ровно одному provider-у.
- [ ] Lazy resources создаются только для реально использованных providers и корректно
      освобождаются.
- [ ] `Main.cs` остаётся тонким, весь composition остаётся в `CTS/CompositionRoot.cs`.
- [ ] Не добавлены service locator, abstract base classes, implementation inheritance, XML docs
      или generic loading capability.
- [ ] Chip, lasso и `SelectionOutcome` функционально не изменены.
- [ ] Deterministic tests, обе opt-in live ветки, release build и `git diff --check` проходят.

## Следующий отдельный этап: provider picker в chip

Этот документ его не реализует. Когда backend будет готов, отдельный план должен:

- передать descriptors и текущий ID в overlay;
- сделать provider control hit-testable, чтобы клик по chip не начинал lasso;
- добавить выбранный provider ID в `SelectionOutcome`;
- использовать ID из outcome для текущей сессии и сохранять его как новый default;
- проверить keyboard navigation, pointer/touch, DPI, High Contrast и анимации chip.

Router API с явным requested ID уже готовит seam для этого: будущему этапу потребуется заменить
источник ID в coordinator, но не менять Google/Yandex implementations.

## Предположения и открытые вопросы

Зафиксированные defaults:

- default provider ID — `google-lens`;
- Yandex регистрируется, но production UI пока не предлагает его выбрать;
- неизвестный ID означает runtime fallback на Google, а не автоматическое исправление settings;
- provider failure не вызывает другой provider автоматически;
- provider-specific loading и browser automation принадлежат конкретному provider-у.

Открытый вопрос для следующего UI-этапа: должен ли chip сохранять выбор сразу при переключении или
только после успешного выделения. Для backend-этапа это не влияет на контракт router-а.
