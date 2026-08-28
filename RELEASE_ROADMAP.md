# Circle to Search — roadmap релиза

## Цель

Подготовить текущий плагин с Google Lens через WebView2 к публичному релизу, не встраивая
Fixed Version Runtime размером 250+ МБ. Первый запуск должен быть понятным, выделение области —
приятным, окно Lens — аккуратным, а глобальная горячая клавиша — настраиваемой без ручного ввода
строки вида `Ctrl+Alt+Space`.

Целевая версия: `0.6.0` после прохождения всех release gates из этого документа.

## Проверенное текущее состояние

- `Main.cs` остаётся тонким адаптером Flow Launcher; граф зависимостей собирается в
  `CTS/CompositionRoot.cs`.
- `CTS/Search/WebView2SearchWindow.cs` открывает `lens.google.com`, передаёт PNG из памяти через
  `DataTransfer`/`DragEvent` и ждёт сгенерированный результат `udm=26`. Status bar и DevTools уже
  выключены, профиль WebView2 постоянный и изолированный.
- Отсутствующий Runtime превращается в `UploadFailure.BrowserRuntimeUnavailable`, после чего
  `SearchCoordinator` показывает обычное сообщение об ошибке Flow Launcher.
- `CTS/Capture/OverlayWindow.cs` замораживает монитор под курсором и рисует жёлтый `Polyline`
  толщиной 2 px. Затемнения, подсказки, reveal mask и анимации нет.
- `CTS/Settings/SettingsPanel.cs` — обычный `StackPanel`; hotkey вводится вручную в `TextBox`.
- `HotkeyRegistrar.TryApply` снимает текущую комбинацию до проверки новой. Если новая занята,
  пользователь остаётся без работающей горячей клавиши.
- Текущая release-папка занимает около 2,2 МиБ. Roadmap не должен добавлять браузерный Runtime
  или тяжёлый UI-фреймворк.

## Продуктовые и архитектурные ограничения

- Сохранить `Main.cs` тонким; собирать зависимости только в `CTS/CompositionRoot.cs`.
- Оставить WPF, уже используемый Flow Launcher и плагином. Визуальный язык WinUI + Material You
  реализовать композицией WPF, цветами, отступами, скруглениями и motion. Не добавлять WinUI 3,
  Windows App SDK или библиотеку Material-компонентов.
- Предпочитать композицию и constructor injection. Не добавлять service locator, abstract base
  classes, иерархии implementation inheritance или XML docs.
- Не использовать clipboard, временные скриншоты, скрытые upload endpoints, browser impersonation
  или silent-установку программ.
- Косметику внутри Google считать best-effort: её сбой никогда не должен ломать поиск Lens.
- Сохранить системный window chrome, resize/snap, keyboard navigation, per-monitor DPI, High
  Contrast и системную настройку отключения анимаций.
- Централизовать UI-строки. Для `0.6.0` сделать русский и английский по текущей UI-culture;
  английский — fallback для остальных языков.


## Milestone 1 — диалог отсутствующего WebView2 (release blocker)

### Желаемое поведение

Не показывать диалог при загрузке плагина. При первой попытке поиска без Runtime вместо обычного
Flow error показать отдельный центрированный диалог:

- Заголовок: `Для Google Lens нужен WebView2` / `WebView2 is required for Google Lens`.
- Коротко объяснить, что это компонент Microsoft, обычно уже имеющийся в Windows 11 и большинстве
  систем Windows 10.
- Строка состояния Runtime с понятной missing/error icon.
- Primary: `Открыть страницу загрузки` / `Open download page`.
- Secondary: `Проверить снова` / `Check again`.
- Tertiary: `Закрыть` / `Close`.
- Download action открывает официальную пользовательскую страницу Microsoft в браузере. Плагин не
  скачивает `.exe`, не запускает installer, не запрашивает elevation и не выполняет silent command.
- `Проверить снова` динамически проверяет Runtime. Если он появился, диалог закрывается, а плагин
  повторяет поиск с тем же PNG в памяти — выделять область заново не нужно.
- Закрытие возвращает `SearchCoordinator` в `Idle`. Повторные triggers не создают несколько
  диалогов.

### План реализации

- [ ] Добавить `CTS/Ui/UiStrings.cs` с русскими/английскими строками и выбором culture.
- [ ] Добавить `CTS/Ui/RuntimeMissingDialog.cs`: собрать WPF UI кодом, запускать на выделенном
  `StaDispatcher`, асинхронно возвращать `Retry` или `Dismissed`.
- [ ] При необходимости добавить конкретный `RuntimeDialogController`: single-instance gate,
  runtime probe, официальный download URI, external URL opener, dispatcher и корректный `Dispose`.
- [ ] Передать в `SearchCoordinator` отдельный callback диалога Runtime. При
  `BrowserRuntimeUnavailable` дождаться решения; при `Retry` вызвать
  `_provider.SearchAsync(png, cancel)` ещё один раз и обработать результат обычно. Не зацикливать.
- [ ] В `CompositionRoot` передавать live probe
  `WebView2SearchWindow.GetRuntimeVersion(pluginDirectory)`. Controller собрать там и освободить
  в `PluginRuntime.Dispose`.
- [ ] Переименовать `OpenResultsUrl` в `OpenExternalUrl`: тот же безопасный shell-open нужен для
  Microsoft download page.
- [ ] Оставить `WebView2SearchWindow` ответственным только за WebView2 и
  `RuntimeUnavailable`; он не показывает product prompt и не открывает внешние страницы.
- [ ] Runtime-card в настройках вызывает live probe при создании и по `Проверить снова`.

### Тесты

- [ ] `SearchCoordinatorTests`: Runtime missing + dismissed → `Idle`, один provider call,
  generic error не показан.
- [ ] Runtime missing + успешный retry → тот же PNG, ровно два provider calls, успех.
- [ ] Повторный `RuntimeUnavailable` после retry не открывает новый dialog и не зацикливается.
- [ ] Одновременные запросы controller показывают один dialog.
- [ ] Download action вызывает только настроенный HTTPS URL Microsoft; installer path/silent
  switches в коде отсутствуют.
- [ ] Ручной тест в VM без WebView2: keyboard navigation, close, download page, ручная установка,
  `Проверить снова`, retry того же выделения.

## Milestone 2 — новый selection overlay (release blocker)

- почти выполнено,осталось докрутить визуал выделения и в конце после лассо показать квадратиком что выделиться и отправится в google lens


## Milestone 3 — polish окна WebView2 (желательно; не blocker, если рискованно)

Сохранить стандартный Windows title bar, resize и snap. Страница остаётся настоящим Google Lens:
sign-in, follow-up chat и accessibility Google работают.

- когда открывается окно webview там на секунду появляется окно google lens,чтобы это решить можно заинджектить анимацию загрузку на полный экран на секунду(?)
- QOL: возможность закрыть webview окно через control+W(?)

### План реализации

- [ ] Иконка плагина, title `Circle to Search — Google Lens`, разумный minimum size и системные
  rounded corners там, где их предоставляет Windows.
- [ ] Заменить bare `WebView2` content на `Grid` с loading overlay:
  `Ищем с помощью Google Lens…` / `Searching with Google Lens…` и indeterminate progress.
  Убирать overlay после валидации result URL.
- [ ] При upload failure показать короткое error state перед обычным error path.
- [ ] Оставить выключенными status bar/DevTools; сохранить context menu, keyboard input, auth и
  ссылки.
- [ ] Загружать `Images/app.png` как icon без file lock.
- [ ] DWM attributes применять после появления HWND, unsupported значения игнорировать. Не делать
  borderless custom chrome.
- [ ] Опциональный thin scrollbar: `8 px`, transparent track, rounded neutral thumb, hover. CSS
  держать в одном method, не использовать obfuscated Google classes; failure игнорировать.
- [ ] Не добавлять address bar, browser toolbar, arbitrary navigation или custom user-agent.

### Тесты

- [ ] `WebView2LiveSearchTests`: два поиска с тем же window/profile доходят до `ResultsReady`.
- [ ] Loading transitions проверить без сети через delegates/pure state reducer.
- [ ] Signed out/in, follow-up chat, resize/maximize/snap, context menu, wheel/touchpad/keyboard,
  200% DPI.
- [ ] Если scrollbar нестабилен или ухудшает contrast, исключить его из `0.6.0`.

## Milestone 4 — настройки и hotkey capture (release blocker)

### Желаемый layout

Три вертикальные карточки:

1. **Горячая клавиша / Hotkey** — первая и визуально главная.
2. **Google Lens / WebView2** — runtime status и help/download link.
3. **Качество изображения / Image quality** — max long side и пояснение quality/upload time.

Hotkey-card содержит focusable rounded capture block с комбинацией в виде keycaps. Click/Enter
переводит его в `Нажмите сочетание клавиш…` / `Press a key combination…`. Следующая
поддерживаемая non-modifier key с удерживаемыми modifiers становится кандидатом. Escape отменяет
capture; Tab вне записи продолжает keyboard navigation.

Captured hotkey применяется сразу:

- Успех: обновить `PluginSettings.HotkeyGesture`, сохранить, показать success state, обновить query
  status.
- Unsupported/occupied: объяснить ошибку, сохранить старое значение и восстановить старую OS
  registration.
- Требовать хотя бы один modifier и одну non-modifier key.
- Поддержать `A–Z`, `0–9`, `F1–F12`, Space, Insert, Delete, Home, End, PageUp, PageDown.

### План реализации

- [ ] Добавить pure state machine `CTS/Settings/HotkeyCaptureController.cs`: idle/recording,
  cancel, modifier-only, completed chord, unsupported input.
- [ ] Использовать `KeyInterop.VirtualKeyFromKey`; корректно обрабатывать `Key.System` для Alt.
  Канонический текст получать через `HotkeyGestureParser`.
- [ ] Добавить parser helpers `TryFormat`/supported-key; не показывать fallback вроде `0xBA`.
- [ ] `HotkeyRegistrar.TryApply` возвращает success/reason. Замена транзакционная: сохранить старый
  chord, unregister, попробовать candidate; при неудаче re-register старый. Settings сохранять
  только после успеха.
- [ ] Добавить delegate-based internal constructor или маленький composition seam в registrar для
  unit tests без настоящего global hotkey.
- [ ] Пересобрать `SettingsPanel` из WPF cards и focusable `Border`; business state держать в
  controller/registrar, не в UI handlers.
- [ ] Удалить editable hotkey `TextBox`.
- [ ] Сохранить image-size range `256–8000`; предпочтителен slider + точное numeric value.
- [ ] Startup `webView2Version` заменить live probe, чтобы runtime-card обновлялась после установки.

### Тесты

- [ ] `HotkeyCaptureControllerTests`: start, Escape, modifier-only, supported/unsupported,
  Alt/SystemKey.
- [ ] `HotkeyGestureParserTests`: все capturable keys round-trip/canonical.
- [ ] `HotkeyRegistrarTests`: success, conflict rollback, invalid без unregister, rollback failure.
- [ ] `SettingsPanel` STA smoke: create, focus, cancel, dispose без handler leak.
- [ ] Ручной conflict test: старый hotkey продолжает работать.
- [ ] Keyboard-only/screen-reader pass: card order, focus, prompt, success/error announcements.

## Интеграция и документация релиза

- [ ] Обновить `plugin.json` до `0.6.0` только после blocker milestones.
- [ ] Обновить `README.md`: Runtime flow, hotkey capture, overlay, light/dark screenshots.
- [ ] Явно указать: WebView2 не встроен, silent installer не запускается, открывается только
  официальная Microsoft page.
- [ ] Сохранить direct Lens route и передачу captured bytes из памяти в видимую Lens page.
- [ ] Удалить release-facing упоминания Yandex; удаление inactive code/tests — отдельное решение.
- [ ] Release directory меньше `5 MiB` и содержит `WebView2Loader.dll` для `win-x64`.
- [ ] Собрать archive из `bin/Release`, установить в новый Flow profile, выполнить
  `Reload Plugin Data` и пройти acceptance без development files.

## Команды и release gates

Каждая зелёная test-команда должна показать ненулевое число выполненных тестов.

```powershell
dotnet build .\CircleToSearch.csproj
dotnet test .\tests\CircleToSearch.Tests\CircleToSearch.Tests.csproj
dotnet test .\tests\CircleToSearch.Tests\CircleToSearch.Tests.csproj `
  --filter "FullyQualifiedName~WebView2LiveSearchTests" `
  -e CTS_WEBVIEW2_LIVE=1
powershell -File .\build_release.ps1 -NoPause
git diff --check
```

Итоговые acceptance criteria:

- [ ] Missing Runtime показывает custom dialog, не запускает silent install и retry-ит тот же PNG.
- [ ] Chip, dim/reveal, lasso, cancel и crop работают во всём DPI matrix и при negative coordinates.
- [ ] WebView напрямую доходит до `udm=26`; sign-in и follow-up conversation работают.
- [ ] Hotkey не требует печати, conflict не уничтожает старую комбинацию, UI доступен с клавиатуры.
- [ ] Plugin reload освобождает hotkey window, overlay/dialog dispatchers и WebView processes.
- [ ] Clipboard не меняется; screenshot bytes, Lens URLs, cookies/credentials не попадают в logs.
- [ ] Offline, Google failure, close и повторный trigger всегда возвращают coordinator в `Idle`.
- [ ] Deterministic suite, live test, release build, installed smoke и `git diff --check` проходят.

## Предположения и открытые вопросы

Зафиксировано для `0.6.0`:

- Только Windows x64.
- WPF — UI technology; WinUI + Material You — визуальный язык, не зависимости.
- Runtime устанавливается только явным действием пользователя вне плагина.
- Русский и английский; остальные locales используют English fallback.
- Стандартный Windows title bar предпочтительнее custom chrome.

Вопросы для visual QA:

1. Chip исчезает при pointer-down или остаётся до mouse-up? Default: исчезает.
2. Thin scrollbar стабилен и читаем в обеих Google themes? Если нет, не включать в `0.6.0`.
3. Image quality оставить numeric или сделать slider с presets? Default: slider + numeric value,
   сохранив `1600 px`.

