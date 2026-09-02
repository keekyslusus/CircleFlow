# План реализации OCR-выделения текста и перевода экрана

## Статус и назначение

Это implementation handoff для другого агента. Он описывает реализацию, но не содержит самой реализации.

Цель: при открытии CircleFlow автоматически и локально распознавать текст на замороженном снимке текущего монитора. Пользователь должен выделять найденный текст обычным для десктопа жестом мыши и получать локальное меню «Copy» / «Search» без отдельного режима или чипа копирования. Единственный новый постоянный чип — «Translate», который переводит распознанный текст всего экрана и накладывает перевод поверх исходных строк.

## Проверенное текущее состояние

- Проект — WPF-плагин на `net9.0-windows`; основной проект — `CircleFlow.csproj`, тестовый — `tests/CircleToSearch.Tests/CircleToSearch.Tests.csproj`.
- `CTS/Capture/PointerMonitorCapture.cs` делает снимок только монитора под указателем и возвращает `System.Drawing.Bitmap`, физические границы монитора, work area, DPI scale и положение курсора.
- `CTS/Capture/OverlayWindow.cs` создаёт замороженный `BitmapSource` и показывает полноэкранный overlay на отдельном STA-потоке.
- Единственным владельцем событий `MouseLeftButtonDown`, `MouseMove` и `MouseLeftButtonUp` сейчас является `CTS/Capture/OverlayInteractions/SelectionOverlayController.cs`.
- `CTS/Capture/SelectionGestureClassifier.cs` считает жест диагональю до 3 физических пикселей выбором цвета, жест меньше `LassoMinDiagonalPx` ошибкой, а более крупный жест визуальным выделением.
- При визуальном выделении `OverlayWindow.OnSelectionCompleted` немедленно переводит overlay в `Closing`, передаёт владение исходным bitmap через `VisualSelection` и запускает существующий visual-search workflow.
- Короткий клик копирует цвет через `ColorPickController`; после успешного копирования overlay закрывается.
- `OverlayInteractionState` содержит режимы `Selecting`, `Listening`, `MusicResult`, `ColorConfirmation`, `Closing`. Текстовых и переводческих состояний пока нет.
- Нижний tray собирается в `ActionTrayVisualFactory`: информационный selection chip, выбор Google/Yandex и кнопка распознавания музыки. Все фиксированные цвета находятся в `CTS/Ui/PluginPalette.cs`, все пользовательские строки — в `Languages/en.xaml` через `CTS/Ui/UiStrings.cs`.
- Весь production dependency graph действительно собирается в `CTS/CompositionRoot.cs`; `Main.cs` остаётся тонким адаптером.
- Текущий полный тестовый baseline после restore: 393 теста, 0 failed, 0 skipped (`dotnet test .\tests\CircleToSearch.Tests\CircleToSearch.Tests.csproj -c Release`).

## Требуемый UX-контракт

### Открытие overlay и OCR

1. Overlay должен появляться сразу на существующем замороженном кадре; OCR не должен задерживать первый показ окна.
2. После `Loaded` запустить OCR замороженного `BitmapSource` в фоне. Не показывать глобальный spinner и не блокировать старые жесты.
3. До готовности OCR поведение полностью прежнее: click копирует цвет, drag рисует visual lasso.
4. После готовности OCR наведение на распознанное слово меняет курсор с crosshair на I-beam и показывает очень лёгкую подсветку строки/слова. Постоянно рисовать рамки вокруг всего найденного текста нельзя.
5. Ошибка OCR не должна закрывать overlay или ломать visual search. Она становится видимой только если пользователь нажал «Translate»; тогда показать локализованный toast.

### Маршрутизация мыши

Источник жеста выбирается ровно один раз на `MouseDown` и не меняется до `MouseUp`:

- Если OCR уже готов, указатель попал в слово и `Alt` не зажат — начать text selection.
- Иначе передать весь жест существующему lasso/color pipeline.
- `Alt+click` и `Alt+drag` на тексте принудительно используют старый pipeline. Это сохраняет возможность взять цвет пикселя буквы или обвести область, начинающуюся на тексте.
- Если OCR завершился после `MouseDown`, текущий жест не повышается из lasso в text selection.
- Если OCR был заменён/отменён, уже начатый text gesture работает со snapshot того `OcrDocument`, который был выбран на `MouseDown`.

Router получает WPF DIPs, а OCR bounds хранятся в capture-relative физических пикселях. Вынести существующую формулу `(dip - overscanInset) * scale` в один `OverlayCoordinateMapper` и использовать её и в lasso, и в text hit testing; не дублировать округление в двух controllers. Абсолютный `monitor.Left/Top` к OCR bounds не добавлять.

Для мыши не вводить long press. Обычный click по слову выбирает одно слово; click-drag выбирает непрерывный диапазон слов. Диапазон определяется включительно между anchor word и текущим word в стабильном OCR reading order и одинаково работает при движении вперёд и назад.

При выделении нескольких строк текст для clipboard/search собирается с пробелами между словами одной строки и `Environment.NewLine` между строками. Не добавлять OCR-текст в логи.

Text selection не вызывает существующий `OnSelectionStarted` и не скрывает нижний tray: это действие не является visual lasso и не должно выглядеть как начало закрытия overlay.

### Контекстное меню текста

После `MouseUp` рядом с union bounds выделенных слов показать компактную карточку с двумя кнопками:

- `Copy`: записать текст в clipboard, оставить overlay открытым, показать короткое подтверждение, сохранить выделение до следующего клика/Escape.
- `Search`: закрыть overlay, затем открыть текстовый поиск в браузере. Для текущего visual provider использовать парный текстовый endpoint: `google-lens` → Google Search, `yandex-images` → Yandex Search. URL строить только через отдельный валидируемый builder и `Uri.EscapeDataString`.

Карточка должна быть полностью внутри overlay: сначала размещать над выделением, при нехватке места — под ним, затем clamp по краям. Её hit-test нельзя пропускать в lasso. Click вне карточки снимает предыдущее текстовое выделение и затем обрабатывает новый жест по обычным правилам.

### Translate chip

Добавить отдельную текстовую кнопку/чип `Translate` рядом с существующими элементами tray. Не добавлять глобальный чип `Copy` и не добавлять режим входа в копирование.

Состояния чипа:

1. `Translate` — обычное состояние.
2. `Translating…` + существующий стиль loading indicator — ожидание OCR или сети; повторный click отменяет только перевод и возвращает обычное выделение.
3. `Show original` — перевод показан; click убирает перевод, не закрывая overlay.

Первое нажатие до сетевой отправки показывает модальную карточку внутри overlay: распознанный текст экрана будет отправлен MyMemory через HTTPS, screenshot не отправляется. Кнопки `Continue` и `Cancel`; согласие сохраняется в settings. Ни исходный, ни переведённый текст, ни полный request URL не логируются.

Если OCR ещё выполняется, нажатие `Translate` переводит UI в ожидание и запускает сетевой этап сразу после получения документа. Если текста нет, OCR language недоступен или OCR упал — вернуться к `Selecting` и показать локализованный toast.

Если фактический source language OCR эквивалентен target language после нормализации тегов, сетевой запрос не делать: вернуть UI в `Selecting` и показать локализованное сообщение, что экран уже на целевом языке.

При успешном результате:

- слегка приглушить оригинальные OCR line bounds;
- поверх каждой исходной строки нарисовать translated card с theme-aware фоном и текстом;
- переведённый слой в первой версии не участвует в word-level selection, потому что у перевода нет достоверной геометрии слов; пользователь возвращается через `Show original`, чтобы выделять исходный текст;
- текст не должен обрезаться или выходить за границы монитора. Layout рассчитывать отдельно и тестировать: измерить wrapped text, расширить card вниз, clamp по экрану и последовательно раздвигать пересекающиеся cards. При невозможности сохранить исходное положение допустимо сместить card, но нельзя молча обрезать строку.

Во время `Translating` и `TranslationShown` lasso/text/color input отключён. Provider/music controls также отключены, кроме translate chip. После `Show original` они восстанавливаются.

### Escape и закрытие

Сохранить текущий приоритет и добавить текст/перевод:

1. закрыть debug panel;
2. закрыть provider menu;
3. закрыть consent card;
4. если перевод выполняется — отменить его и вернуться к `Selecting`;
5. если перевод показан — показать оригинал;
6. если открыто text action menu — убрать menu и выделение;
7. иначе закрыть overlay.

Right click пока сохраняет текущее значение «закрыть overlay»; нативное context menu не вводить.

## Границы первой версии

В scope входят локальный Windows OCR, mouse text selection, copy/search menu, Translate chip, keyless MyMemory provider, overlay перевода, настройки OCR/target language, cancellation, accessibility, автоматические и ручные тесты.

Не входят: touch/stylus-specific gestures, long press, редактирование OCR-текста, сохранение OCR/переводов на диск, история clipboard, перевод screenshot как изображения, offline translation, смешанные OCR-языки в одном кадре и word-level selection переведённого текста.

Один OCR-run использует один выбранный language pack. Это осознанное ограничение `Windows.Media.Ocr.OcrEngine`; multi-language merge нужно проектировать отдельно.

## Архитектура и поток данных

```text
PointerMonitorCapture
  -> frozen BitmapSource shown immediately
  -> WindowsOcrRecognizer (background, local)
  -> immutable OcrDocument in physical monitor pixels
       -> PointerGestureRouter -> TextSelectionController -> Copy/Search menu
       -> ScreenTranslationRequested command
            -> OverlaySessionWorkflow
            -> MyMemoryTranslationProvider (HTTPS, text only)
            -> ScreenTranslationResult
            -> overlay translation visual
```

Сетевой перевод и открытие browser должны оставаться вне WPF controllers. OCR и hit testing являются частью локальной overlay-session. Весь граф собирается только в `CompositionRoot`.

## Модели и новые компоненты

### `CTS/TextRecognition`

Создать:

- `OcrDocument.cs`
  - immutable `OcrDocument(LanguageTag, PixelSize, Lines)`;
  - `OcrLine(Id, Order, BoundsPx, Words)`;
  - `OcrWord(Id, LineId, ReadingOrder, Text, BoundsPx)`;
  - bounds всегда относительно левого верхнего угла capture в физических пикселях, не screen coordinates и не DIPs;
  - модели не содержат WPF controls или WinRT types.
- `IOcrRecognizer.cs`
  - `Task<OcrRecognitionOutcome> RecognizeAsync(BitmapSource source, string? requestedLanguageTag, CancellationToken)`;
  - outcome различает success, no text, language unavailable, platform unavailable, canceled и failed без exception-driven UI flow.
- `WindowsOcrRecognizer.cs`
  - использует `Windows.Media.Ocr.OcrEngine` и `Windows.Graphics.Imaging.SoftwareBitmap`;
  - `requestedLanguageTag == null/empty` означает `TryCreateFromUserProfileLanguages()`, иначе `TryCreateFromLanguage`;
  - проверяет `OcrEngine.MaxImageDimension`; большой снимок уменьшает с сохранением aspect ratio и возвращает word rectangles обратно в исходные физические пиксели через scale factors;
  - удаляет пустые слова, union-ит word bounds в line bounds, выдаёт стабильный reading order;
  - освобождает `SoftwareBitmap`/buffers во всех исходах.
- `OcrLanguageCatalog.cs`
  - читает `AvailableRecognizerLanguages`, отдаёт безопасный список тегов/display names для settings и проверяет сохранённый tag.
- `OcrTextHitTester.cs`
  - hit test слова с небольшим DPI-aware tolerance;
  - выбирает минимальный содержащий/ближайший word rectangle детерминированно;
  - не делает «магнитный» выбор через большие пустые промежутки.
- `OverlayCoordinateMapper.cs` в `CTS/Capture`
  - преобразует overlay DIPs в capture-relative physical pixels и обратно;
  - централизует DPI/overscan/clamp rules для lasso, text hit testing, highlights и action-card placement.
- `TextSelectionRange.cs`
  - нормализует anchor/current order;
  - возвращает selected words, highlight rectangles, union bounds и текст с корректными line breaks.

### Platform targeting

`Windows.Media.Ocr` недоступен текущему компилятору при голом `net9.0-windows`. Сначала выполнить отдельный compile spike:

1. Перевести оба `TargetFramework` на `net9.0-windows10.0.19041.0` и зафиксировать поддерживаемый minimum через `SupportedOSPlatformVersion` после проверки текущей политики проекта.
2. Не добавлять `Microsoft.Windows.SDK.Contracts`: для .NET 5+ официальный путь — versioned Windows TFM.
3. В spike скомпилировать создание `OcrEngine`, создание BGRA8 `SoftwareBitmap` из byte buffer и один `RecognizeAsync`.
4. После доказательства удалить spike и только затем продолжать feature code.
5. Если restore/SDK projection не работает на CI/package host, остановиться и описать blocker; не подменять Windows OCR Tesseract или cloud OCR без отдельного решения владельца.

Конвертацию выполнять с frozen `BitmapSource` на background thread: привести pixels к BGRA32, при необходимости resize до `MaxImageDimension`, скопировать в отдельный buffer и создать `SoftwareBitmap`. Не читать рабочий `System.Drawing.Bitmap` параллельно с visual-search ownership transfer.

### `CTS/Capture/OverlayInteractions`

Создать `PointerGestureRouter.cs`, и сделать его единственным подписчиком на три mouse events `Selection.InputSurface`. Он хранит `ActiveGesture = None | Lasso | Text` и напрямую композирует два конкретных controller; не вводить abstract base classes или implementation inheritance.

Рефакторинг `SelectionOverlayController`:

- убрать самостоятельную подписку на mouse events;
- открыть узкие методы `Begin(Point)`, `Update(Point)`, `Complete(Point)` и `Cancel()`;
- сохранить существующий sampling, reveal, classifier, pixel pick, hold animation и bounds calculation без изменения результатов;
- существующие callbacks selection/color остаются прежними.

Создать `TextSelectionOverlayController.cs`:

- принимает актуальный `OcrDocument` только через setter/snapshot provider;
- обслуживает hover, cursor, begin/update/complete, reverse drag, highlight и action card;
- `Copy` использует внедрённый `Action<string>` clipboard delegate и показывает localized toast, но не публикует команд и не закрывает overlay;
- `Search` публикует `SearchSelectedText(text, selectedProviderId)`;
- dispose отменяет pending OCR continuation, снимает mouse capture, handlers и animations.

Создать `OcrOverlayController.cs`:

- запускается из `OverlayWindow.OnLoaded`;
- владеет `CancellationTokenSource` и generation id;
- выполняет recognizer не на WPF dispatcher;
- публикует готовый immutable document в text/translation controllers через dispatcher;
- stale completion после dispose/close игнорируется.

Создать `ScreenTranslationOverlayController.cs` для consent card, состояния translate chip, request id, render/dismiss/cancel и stale-result rejection. Этот controller не получает `HttpClient` или конкретный MyMemory provider.

Расширить `OverlayControllers`, `OverlayControllerContext` и `OverlayControllerFactory`. При ошибке частичной сборки dispose выполнять в обратном порядке. В production factory передавать зависимости явно из `CompositionRoot`; тесты используют fake/disabled recognizer.

### Visual tree

Расширить `OverlayVisual` моделями:

- `TextSelectionVisual`: non-hit-test hover/highlight canvas и hit-testable action-card layer с `Background = null`;
- `TranslationActionVisual`: translate button, label/icon/loading state;
- `TranslationOverlayVisual`: non-hit-test canvas для translated cards и отдельный consent-card host.

Создать соответствующие factories/presenters в `CTS/Capture/OverlayVisuals`. Рекомендуемый z-order:

1. screenshot и существующие lasso/dim layers;
2. text hover/highlight (non-hit-test);
3. существующий input surface;
4. translated cards (non-hit-test);
5. effects/listening layers;
6. text action card и consent host;
7. bottom overlay;
8. debug panel.

Action card и consent host должны блокировать lasso только на своих видимых controls. Обобщить `OverlayWindow.IsActionTrayInteraction` в проверку всего overlay chrome (`Bottom.Root`, `Debug.Panel`, text action card, consent card), сохранив тесты hit boundaries.

Все новые фиксированные цвета добавить в `PluginPalette` как `TextInteractionPalette` и `TranslationPalette`. Ни одного `Color.From...`, hex brush или named fixed color вне palette.

### Команды, session и workflow

В `CTS/Capture/OverlayCommand.cs` добавить:

- `SearchSelectedText(string Text, string ProviderId)`;
- `ScreenTranslationRequested(Guid RequestId, OcrDocument Document, string TargetLanguageTag)`;
- `CancelScreenTranslation(Guid RequestId)`.

Строки валидировать на null/whitespace, но не логировать их в `OverlaySession.Publish`; изменить logging команды так, чтобы записывался только тип и request id, если он нужен для диагностики.

В `IOverlaySession`/`OverlaySession` добавить dispatcher-safe методы:

- `ShowTranslationAsync(ScreenTranslationResult result, CancellationToken)`;
- `ShowTranslationFailureAsync(Guid requestId, TranslationFailure failure, CancellationToken)`;
- при необходимости `RestoreSelectionAsync(Guid requestId, CancellationToken)`.

Расширить `OverlayInteractionMode` значениями `TranslationConsent`, `Translating`, `TranslationShown`. Text selection остаётся вложенным состоянием `Selecting`, а не отдельным global mode. Явно протестировать переходы:

- `Selecting <-> TranslationConsent`;
- `TranslationConsent -> Translating`;
- `Selecting -> Translating` для уже принятого consent;
- `Translating -> TranslationShown | Selecting | Closing`;
- `TranslationShown -> Selecting | Closing`;
- music/color transitions из translation modes запрещены.

В `OverlaySessionWorkflow` вести translation task и linked CTS параллельно с command task/music task через `Task.WhenAny`. У каждого запуска свой request id. Cancellation не закрывает сессию, completion с неактуальным request id не меняет UI. Внешняя отмена/hotkey/closing отменяет OCR и перевод и дожидается безопасного завершения в `finally`.

`SearchSelectedText` должен сначала закрыть overlay, затем вызвать новый `TextSearchWorkflow`; это предотвращает попадание overlay в новый screenshot и повторный input. Ошибка открытия browser показывает существующий notifier с новой localized строкой.

### `CTS/Translation`

Создать:

- `ITranslationProvider.cs` с batch-oriented контрактом, который принимает line/chunk ids, source tag и target tag;
- `MyMemoryTranslationProvider.cs`;
- `TranslationSegmenter.cs`;
- `ScreenTranslationWorkflow.cs`;
- outcome/result/failure models без UI-типов.

Первый provider — официальный keyless endpoint `https://api.mymemory.translated.net/get`. Требования:

- только HTTPS и exact host policy;
- `q` и `langpair` формировать URI builder-ом; никаких raw string concatenation пользовательского текста;
- не передавать `key`, `user`, `de` или screenshot;
- учитывать официальный предел `q` в 500 UTF-8 bytes;
- длинную OCR line делить по sentence/whitespace, а при необходимости по Unicode rune, сохраняя способ обратной склейки; не разрезать surrogate pair;
- переводить по одному paragraph/line chunk, не отправлять весь экран одним запросом;
- максимум 2 одновременных request, общий timeout 10 секунд и полная cancellation propagation;
- одинаковые chunks внутри одного экрана дедуплицировать, но не сохранять чувствительный cache на диск и не держать его после завершения request;
- ограничить размер JSON response, валидировать HTTP status, `responseStatus` и наличие `responseData.translatedText`;
- partial success разрешён: показать успешные строки, для неуспешных оставить оригинал и один warning toast. Если успешных строк нет — failure и возврат к `Selecting`.

Нормализовать BCP-47 теги отдельно. Сначала пробовать полный lowercase tag; для обычных regional tags иметь протестированный fallback к neutral language, а `zh-Hans`/`zh-Hant` маппить явно. Source language берётся из реально созданного `OcrEngine.RecognizerLanguage`, а не угадывается по тексту.

Перед release ещё раз проверить актуальные MyMemory Terms/limits и необходимость attribution. Provider abstraction обязателен, потому что keyless сервис может изменить quota/contract. Не использовать undocumented Google Translate endpoints как скрытый fallback.

### Text search

Создать `CTS/Search/TextSearchWorkflow.cs` и чистый `TextSearchUrlBuilder.cs`:

- Google URL: HTTPS Google Search с escaped `q`;
- Yandex URL: HTTPS Yandex Search с escaped `text`;
- ограничить query разумным размером (например, 2,000 Unicode scalar values) с явным localized сообщением вместо построения чрезмерного URL;
- policy tests проверяют scheme/host, encoding `&`, `?`, `#`, Unicode и отсутствие double encoding;
- открытие URL использовать через уже существующий `OpenResultsUrl` delegate, внедрённый из `CompositionRoot`.

### Settings и локализация

В `PluginSettings` добавить:

- `string OcrLanguageTag` — empty означает Windows user-profile language;
- `string TranslationTargetLanguageTag` — при пустом значении один раз нормализовать текущую UI culture и сохранить при Apply;
- `bool TranslationPrivacyConsentAccepted`.

В `SettingsPanel` добавить ComboBox выбора OCR language (`System default` + `AvailableRecognizerLanguages`) и target language. Каталог language tags передавать через constructor из `CompositionRoot`; не использовать service locator. Недоступный сохранённый OCR tag отображать как fallback и не падать.

Добавить в `Languages/en.xaml` и typed accessors в `UiStrings` все новые labels, tooltips, automation names, consent, success/error/partial/empty/language/network/rate-limit/timeout strings. Не хардкодить пользовательский текст в C#. Обновить `TestUiStrings`-совместимые проверки; не добавлять русский XAML в рамках этой задачи, если отдельно не запрошена локализация самого плагина.

## Пошаговая реализация

1. **Platform/OCR spike.** Обновить TFM обоих проектов, доказать WinRT compile/runtime и bitmap conversion на небольшом synthetic image, удалить временный spike. Зафиксировать minimum Windows policy.
2. **Domain OCR.** Добавить immutable models, outcome, language catalog, scaling/conversion и `WindowsOcrRecognizer`; покрыть mapper/hit-test/selection unit tests без WPF window.
3. **Разделить pointer routing.** Превратить текущий selection controller в passive lasso target, ввести единственного router и сначала доказать, что все старые click/lasso/color тесты проходят без изменения результатов.
4. **Text selection UI.** Добавить hover, I-beam, word/range highlight, reverse drag, action card, clipboard copy, Escape/click-away и accessible names.
5. **Text search.** Добавить command, URL builder/workflow, provider mapping и правильный close-before-open порядок.
6. **Translate chip и settings.** Добавить visual/controller, OCR/target language settings, consent card, palette и localization keys; пока использовать fake workflow.
7. **MyMemory provider.** Реализовать UTF-8 segmentation, language mapping, bounded concurrency, parsing, partial results, privacy-safe logging и cancellation.
8. **Translation workflow/session.** Добавить request-id lifecycle, parallel task handling, overlay callbacks, state transitions и stale-completion guards.
9. **Translation rendering.** Добавить line-card layout, source dimming, `Show original`, disabled conflicting controls и visual QA artifacts.
10. **Hardening/package.** Пройти DPI/multi-monitor/accessibility/manual matrix, проверить Terms/attribution, full tests, release build и package contents.

## Автоматические тесты

Добавить как минимум следующие test suites:

- `WindowsOcrRecognizerTests`
  - пустой результат, unavailable language, scaling в обе стороны, word/line bounds mapping, cancellation, disposal;
  - WinRT runtime test отделить от deterministic model tests; не считать условно не запущенный тест доказательством OCR.
- `OcrTextHitTesterTests`
  - inside word, tolerance edge, gap, overlapping boxes, DPI coordinates, deterministic tie.
- `TextSelectionRangeTests`
  - single word, same line, multiple lines, reverse drag, punctuation, Unicode/CJK, line breaks.
- `PointerGestureRouterTests`
  - text hit → text target;
  - outside hit → legacy target;
  - `Alt` on text → legacy target;
  - OCR completion after down не меняет target;
  - OCR snapshot replacement во время drag;
  - chrome hit не начинает жест;
  - capture/dispose cleanup.
- Дополнить `SelectionGestureClassifierTests` и `OverlayWindowTests`, чтобы существующие exact-click, three-pixel color pick, too-small и normal lasso сохранились вне текста и под `Alt`.
- `TextActionOverlayTests`
  - copy остаётся в overlay, clipboard failure показывает localized toast, search публикует ровно одну command, card placement/clamp, Escape/click-away.
- `TextSearchUrlBuilderTests` и `TextSearchWorkflowTests`
  - provider mapping, Unicode/reserved chars, length limit, close-before-open, opener failure.
- `TranslationSegmenterTests`
  - 500 UTF-8 bytes, emoji/surrogate, CJK без пробелов, exact boundary, reconstruct order, deduplication.
- `MyMemoryTranslationProviderTests` с fake `HttpMessageHandler`
  - URI/host/langpair, success, malformed JSON, missing text, non-2xx, service `responseStatus`, timeout, caller cancellation, partial success, max concurrency <= 2, response size limit;
  - ни один captured log message не содержит source/translation/query URL.
- `OverlayInteractionStateTests`
  - все новые разрешённые/запрещённые переходы и `CanAcceptSelectionInput` только в `Selecting`.
- Дополнить `OverlaySessionWorkflowTests`
  - translation success/failure/partial/cancel/retry, stale request ignored, hotkey cancellation, coexistence с music task, search close order.
- Дополнить `OverlayControllerLifecycleTests`
  - pending OCR/translation/animations отменены и window collectable.
- Дополнить visual tests
  - точный z-order и hit-test boundaries;
  - action tray с Translate chip;
  - dark/light/high-contrast colors только из palette;
  - translation cards не выходят за viewport и не clip-ят measured text;
  - accessibility names для всех новых buttons.
- Дополнить `UiStringsTests` и settings tests новыми typed keys и round-trip значений.

Обязательные команды проверки:

```powershell
dotnet test .\tests\CircleToSearch.Tests\CircleToSearch.Tests.csproj -c Release --filter "FullyQualifiedName~PointerGestureRouterTests|FullyQualifiedName~TextSelectionRangeTests|FullyQualifiedName~TranslationSegmenterTests|FullyQualifiedName~MyMemoryTranslationProviderTests"
dotnet test .\tests\CircleToSearch.Tests\CircleToSearch.Tests.csproj -c Release
dotnet build .\CircleFlow.csproj -c Release
powershell -ExecutionPolicy Bypass -File .\build_release.ps1 -NoPause
```

Полный запуск считается зелёным только если выполнено больше baseline 393 тестов, failed = 0 и новые suites действительно присутствуют в выводе. После изменения TFM не использовать `--no-restore` до успешного restore.

## Ручная QA-матрица

Проверить на реальном OCR, не только mocks:

- 100%, 125%, 150% и 200% DPI;
- основной и монитор с отрицательными координатами;
- 1080p и 4K screenshot, включая путь resize по `MaxImageDimension`;
- английский, русский, CJK/emoji и намеренно отсутствующий language pack;
- одиночное слово, reverse drag, несколько строк, очень мелкий текст, пробел между колонками;
- click/drag вне текста и `Alt+click`/`Alt+drag` поверх текста;
- очень медленный OCR: начатый до completion lasso остаётся lasso;
- clipboard занят/недоступен;
- offline, timeout, rate limit, malformed/partial translation response;
- отмена translation chip, Escape на каждом уровне, повторный hotkey и закрытие окна во время OCR/network;
- provider menu, music recognition, color confirmation до и после возврата из translation;
- light, dark, Windows high contrast и reduced animations;
- keyboard focus, Tab navigation и screen-reader names на Translate/Copy/Search/Continue/Cancel/Show original.

Создать opt-in visual preview test по образцу `SelectionPreviewTests`, который рендерит PNG с hover, multi-line selection, action card, consent и translated overlay; визуально проверить артефакты перед сдачей.

## Acceptance criteria

- Overlay открывается без ожидания OCR.
- После OCR курсор над словом становится I-beam; обычный drag выделяет текст, а не lasso.
- Одиночный click по слову выбирает слово и показывает только `Copy`/`Search`.
- Отдельного copy chip/режима нет.
- Вне OCR bounds старые color/lasso результаты и координаты не изменились; `Alt` принудительно сохраняет их и над текстом.
- Target жеста не меняется после `MouseDown`, независимо от OCR race.
- Copy не закрывает overlay; Search закрывает overlay до открытия браузера.
- Translate — единственный новый постоянный chip; есть waiting/cancel/show-original lifecycle.
- OCR полностью локален; MyMemory получает только текст после первого явного consent; чувствительный текст/URL не логируется и не сохраняется.
- Большие изображения корректно масштабируются для OCR и word bounds возвращаются в физические координаты capture.
- Перевод не clip-ится, не выходит за viewport, поддерживает partial failure и всегда может быть снят без закрытия overlay.
- Closing/dispose отменяет OCR/network, stale callbacks не меняют UI и не удерживают window/bitmap.
- Все строки и цвета соблюдают `Languages/*.xaml` и `PluginPalette`; `Main.cs` остаётся тонким, composition — только в `CompositionRoot`.
- Новые targeted tests, полный suite, Release build и package build зелёные с ненулевым ожидаемым числом тестов.

## Предположения и решения, требующие подтверждения владельца

1. **Минимальная Windows.** План предполагает допустимость versioned TFM и Windows 10-era WinRT OCR. Перед реализацией нужно зафиксировать официальный minimum OS плагина.
2. **Один OCR-язык за запуск.** По умолчанию используется Windows profile language, но в settings его можно выбрать явно. Mixed-language OCR отложен.
3. **MyMemory как первый provider.** Он не требует API key, но имеет лимиты, требует source language и является внешним сервисом. Terms/limits/attribution — release gate. При несогласии с этим сервисом заменить только реализацию `ITranslationProvider`, не UX/domain.
4. **Переведённый текст не selectable в MVP.** Выделяется только исходный OCR-текст; это исключает ложную word geometry перевода.
5. **Text search следует текущему provider.** Google Lens соответствует Google text search, Yandex Images — Yandex text search. Если product decision должен быть «всегда системный поисковик», заменить только mapping/workflow.
6. **Русская локализация UI не входит.** Новые строки добавляются в существующий `en.xaml`; отдельный `ru.xaml` — самостоятельная задача.

## Официальные технические ссылки

- Windows OCR API и word rectangles: https://learn.microsoft.com/en-us/uwp/api/windows.media.ocr.ocrengine
- Вызов WinRT API из .NET desktop через versioned Windows TFM: https://learn.microsoft.com/en-us/windows/apps/desktop/modernize/desktop-to-uwp-enhance
- Создание `SoftwareBitmap` из buffer: https://learn.microsoft.com/en-us/uwp/api/windows.graphics.imaging.softwarebitmap.createcopyfrombuffer
- MyMemory GET API и предел 500 bytes: https://mymemory.translated.net/doc/spec.php
- MyMemory Terms: https://mymemory.translated.net/terms-and-conditions
