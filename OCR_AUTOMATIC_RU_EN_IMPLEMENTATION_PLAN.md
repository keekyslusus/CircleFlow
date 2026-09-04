# План автоматического русско-английского OCR и уточнения выделенного текста

## Статус и назначение

Это самостоятельный implementation handoff для другого AI-агента. Он описывает изменения, но не содержит их реализации.

Цель: убрать ручной выбор исходного языка OCR и автоматически распознавать русский и английский текст на одном экране. Выделение должно использовать объединённую геометрию двух OCR-проходов, а перед `Copy` и `Search` выбранный фрагмент должен повторно распознаваться из исходного кадра с увеличением. Пример `добавить уведомление` должен копироваться именно как `добавить уведомление`, а не как похожая латиница `A06aB... YBeAomneHHe`.

Внешний вид экранного перевода и проблема россыпи translated cards в этот план не входят.

## Проверенное текущее состояние

- Проект собирается под `net9.0-windows10.0.19041.0`; production-проект — `CircleFlow.csproj`, тестовый — `tests/CircleToSearch.Tests/CircleToSearch.Tests.csproj`.
- Проверенный baseline на commit `4bd79d6`: `dotnet test .\tests\CircleToSearch.Tests\CircleToSearch.Tests.csproj -c Release --no-restore` выполняет 444 теста, 0 failed, 0 skipped.
- `CTS/TextRecognition/WindowsOcrRecognizer.cs` создаёт ровно один `Windows.Media.Ocr.OcrEngine`. Пустой tag вызывает `TryCreateFromUserProfileLanguages()`, поэтому на машине с английским приоритетным языком кириллица распознаётся как визуально похожая латиница.
- Текущий recognizer уменьшает целый кадр до `OcrEngine.MaxImageDimension`. На 4K-мониторе это дополнительно ухудшает мелкий текст.
- `IOcrRecognizer.RecognizeAsync` принимает один optional language tag и возвращает один `OcrDocument` с одним `LanguageTag` на весь экран.
- `OcrDocument` содержит line/word bounds в физических пикселях frozen capture и стабильный `ReadingOrder`.
- `OcrOverlayController` запускает OCR асинхронно и передаёт документ в `TextSelectionOverlayController` и `ScreenTranslationOverlayController` через `OverlayControllerFactory`.
- `TextSelectionRange` хранит выбранные слова, их прямоугольники, общий bounds и уже собранный текст.
- `TextSelectionOverlayController.OnCopy` синхронно пишет `_selection.Text` в clipboard. `OnSearch` синхронно публикует `SearchSelectedText` с тем же текстом. Исходный `BitmapSource` и сервис повторного OCR этому controller сейчас не доступны.
- Настройки содержат `PluginSettings.OcrLanguageTag`; `SettingsPanel` показывает ручной source-language picker. По принятому решению этот режим больше не нужен.
- `OcrLanguageCatalog.AvailableLanguages` уже получает только установленные Windows OCR packs.
- `ScreenTranslationWorkflow` передаёт один `document.LanguageTag` всем строкам. После объединения русского и английского это перестанет быть достаточным.
- OCR-текст сейчас не логируется; это privacy-инвариант, который необходимо сохранить.

## Зафиксированные продуктовые решения

1. Source OCR работает автоматически, без ручного выбора в settings.
2. Первая поддерживаемая пара — русский и английский. Одновременно запускается не больше двух language engines.
3. Предпочитаемые tags: `ru-RU` и `en-US`. Если точного regional tag нет, разрешается любой установленный pack с тем же neutral language (`ru-*` или `en-*`).
4. Если установлен только один из двух packs, OCR продолжает работать с ним. Если не установлен ни один — возвращается `LanguageUnavailable`.
5. Полноэкранные проходы дают интерактивную геометрию и предварительный текст.
6. Перед каждым первым `Copy` или `Search` для текущего selection выполняется уточнение по исходному frozen frame. Результат кэшируется до изменения/снятия selection, поэтому повторная команда не запускает OCR снова.
7. Если уточнение не дало пригодного текста или один проход упал, используется предварительный merged text. Ошибка refinement не должна превращать работавший Copy/Search в ошибку.
8. `Copy` и `Search` обязаны использовать один и тот же resolved text.
9. Полноэкранный OCR остаётся фоновым и не задерживает показ overlay.
10. Вид translated cards, их layout и фон не изменяются.

## Желаемое поведение

### Запуск overlay

1. Frozen screenshot показывается немедленно.
2. Автоматический recognizer получает установленные `ru`/`en` tags из catalog.
3. Для каждого доступного языка кадр распознаётся в исходном разрешении по тайлам.
4. Языковые документы объединяются по пространственно совпадающим строкам.
5. В каждой группе выбирается наиболее правдоподобный вариант текста; его bounds используются для hit testing и выделения.
6. Итоговый документ доставляется существующим consumers на UI dispatcher.

### Copy/Search

1. Пользователь выделяет слова как сейчас; action card должна появляться сразу.
2. После нажатия `Copy` или `Search` controller фиксирует snapshot текущего `TextSelectionRange` и временно блокирует обе action buttons от повторных кликов.
3. Из исходного frozen frame вырезаются отдельные прямоугольники выбранной части каждой строки.
4. Каждый crop увеличивается до OCR-friendly размера и распознаётся доступными `ru`/`en` engines.
5. Результаты объединяются тем же scorer/merger, собираются в исходном порядке строк и кэшируются для selection.
6. `Copy` пишет resolved text в clipboard и оставляет overlay открытым. `Search` публикует ровно один `SearchSelectedText` и продолжает существующий closing workflow.
7. При отмене/закрытии overlay незавершённое refinement отменяется и не выполняет поздний clipboard write или publish.

## Архитектура

```text
frozen BitmapSource
  -> AutomaticOcrRecognizer
       -> OcrFrameTiler
       -> ILanguageOcrRecognizer (Windows OCR, ru)
       -> ILanguageOcrRecognizer (Windows OCR, en)
       -> OcrDocumentMerger + OcrTextQualityScorer
       -> merged OcrDocument
            -> hit testing / text selection
            -> screen translation (line language aware)

TextSelectionRange + frozen BitmapSource
  -> SelectionTextRefiner
       -> per-line crop + adaptive upscale
       -> same ru/en language recognizer
       -> same merger/scorer
       -> resolved text
       -> Copy or Search
```

Новые компоненты должны соединяться только в `CTS/CompositionRoot.cs`. Не вводить service locator, abstract base classes или implementation inheritance.

## Модели и интерфейсы

### Разделить низкоуровневый и автоматический OCR

В `CTS/TextRecognition` ввести два узких контракта:

- `ILanguageOcrRecognizer`
  - `Task<OcrRecognitionOutcome> RecognizeAsync(BitmapSource source, string languageTag, CancellationToken)`;
  - реализуется `WindowsOcrRecognizer`;
  - language tag обязателен: выбор profile language из этого класса удалить.
- `IOcrRecognizer`
  - `Task<OcrRecognitionOutcome> RecognizeAsync(BitmapSource source, CancellationToken)`;
  - реализуется новым `AutomaticOcrRecognizer`;
  - именно этот контракт использует `OcrOverlayController`.

`DisabledOcrRecognizer` обновить под новый автоматический контракт. Не заставлять controller передавать source language — это ответственность composition и automatic recognizer.

### Язык строки

Расширить `OcrLine` обязательным `LanguageTag`. Для raw single-language результата он равен фактическому `engine.RecognizerLanguage.LanguageTag`; для merged line — tag выбранного кандидата.

Сохранить `OcrDocument.LanguageTag` как dominant language для обратной совместимости и диагностических решений. `AutomaticOcrRecognizer` определяет его по суммарному числу Unicode letters в выбранных строках; при равенстве используется deterministic candidate order. Нельзя определять dominant language простым числом строк: короткие UI labels не должны перевешивать длинный абзац.

`OcrWord` пока не требует отдельного language tag: selection refinement выполняется по строкам, а перевод может группировать chunks по `OcrLine.LanguageTag`. Обновить все production/test constructors `OcrLine` явно, без неоднозначного optional overload.

### Refinement contract

Добавить `ISelectionTextRefiner`:

```csharp
Task<SelectionTextRefinement> RefineAsync(
    BitmapSource frozenFrame,
    TextSelectionRange selection,
    CancellationToken cancellationToken);
```

`SelectionTextRefinement` содержит status и nullable text. Статусы должны различать как минимум `Success`, `NoText`, `Canceled`, `Failed`; внутрь результата не помещать exceptions или OCR candidates. Controller использует исходный `selection.Text` для всех исходов, кроме `Success` с непустым text.

`AutomaticOcrRecognizer` и `SelectionTextRefiner` должны композиционно использовать один экземпляр `ILanguageOcrRecognizer`, один resolver языков и один merger/scorer, чтобы full-screen и refined результаты не расходились по правилам выбора.

## Выбор автоматических языков

В `OcrLanguageCatalog` либо отдельном `AutomaticOcrLanguageResolver` реализовать чистую, unit-testable функцию:

1. Для русского выбрать exact `ru-RU`, иначе первый `ru-*` в ordinal-ignore-case сортировке.
2. Для английского выбрать exact `en-US`, иначе первый `en-*` в той же сортировке.
3. Не возвращать один tag дважды.
4. Порядок результата всегда детерминирован.
5. Не включать все установленные языки: это неограниченно увеличит startup cost и число ложных кандидатов.

Tags являются protocol/configuration values, а не пользовательскими строками; их можно хранить в C#. Любые новые подписи или предупреждения интерфейса должны находиться в `Languages/*.xaml` и читаться через `UiStrings`.

## Распознавание без уменьшения 4K-кадра

Заменить full-frame downscale в `WindowsOcrRecognizer.PreparePixels` на тайлы:

- tile width/height не превышают `OcrEngine.MaxImageDimension`;
- использовать overlap 64 физических пикселя по внутренним границам;
- крайние тайлы clamp-ятся к исходному кадру;
- каждый word/line bounds переводится из tile-local обратно в capture-relative physical pixels добавлением tile offset;
- для удаления дублей каждому тайлу назначить non-overlapping ownership/core region и принимать слово, если центр его bounds принадлежит core этого тайла;
- на внешней границе кадра ownership включает край полностью;
- после сборки заново назначить последовательные line IDs, word IDs и reading order;
- геометрически независимые колонки с одинаковым Y не объединять в одну строку.

Не выполнять одновременно неограниченное количество `RecognizeAsync`: общий scheduler automatic recognizer должен ограничивать OCR concurrency значением 2. Cancellation проверять перед подготовкой каждого тайла, перед WinRT call и между tile results.

Для кадров, которые уже помещаются в `MaxImageDimension`, должен получаться ровно один tile без лишнего resample.

## Объединение ru/en результатов

### Пространственная группировка

`OcrDocumentMerger` принимает документы одинакового pixel size и не зависит от WPF controls/WinRT types.

1. Сначала сортировать line candidates по `Top`, затем `Left`, затем language order.
2. Считать строки кандидатами одной физической строки, если:
   - vertical overlap составляет не менее 60% меньшей высоты;
   - horizontal ranges пересекаются либо расстояние между ними не превышает одну максимальную высоту строки;
   - центры не принадлежат явно разным колонкам. Для этого не соединять широким transitive merge строки, у которых horizontal overlap отсутствует с общим anchor.
3. Внутри spatial group выбирать одну целую line candidate. В первой версии не смешивать отдельные слова из разных engines: различная сегментация слов делает word-level splice нестабильным.
4. Bounds итоговой строки и слов брать у выбранного кандидата, а не union-ить разные OCR-проходы.
5. Непарные строки сохранять после проверки качества и NMS от дублей.

### Детерминированный выбор текста

Добавить чистый `OcrTextQualityScorer`. Для каждой line candidate вычислять:

- число Unicode letters;
- долю Cyrillic letters и Latin letters;
- долю replacement/control/private-use characters;
- число токенов со смесью цифр и букв внутри слова;
- число неестественных внутренних uppercase transitions;
- долю буквенных bigrams, присутствующих в небольшом встроенном профиле соответствующего языка;
- длину полезного текста без whitespace/punctuation.

Правила выбора должны сначала использовать сильный сигнал:

- если русский проход содержит минимум две кириллические буквы и не менее 50% его букв — кириллица, считать его native-script candidate;
- если русский native-script candidate имеет приемлемое качество и английский вариант состоит из похожей латиницы/цифр, выбирать русский;
- для строки без кириллицы предпочитать качественный английский candidate;
- строки только из цифр/пунктуации выбирать по agreement/геометрии и deterministic language order;
- при полном равенстве normalized text результат не должен считаться конфликтным.

Один script ratio недостаточен: ошибочный английский OCR кириллицы тоже может состоять на 100% из латиницы. Поэтому обязательны noise penalties и компактные character-bigram profiles. Профили хранить как внутренние данные без сетевых зависимостей и без runtime-словаря. Не добавлять большой словарь или ML package в рамках этой задачи.

Минимальные scorer/merger fixtures:

- `ru: "добавить уведомление"` против `en: "A06aB\"TS YBeAomneHHe"` — выбирается русский;
- корректный `Add notification` против худшего русского кандидата — выбирается английский;
- одинаковое `Telegram 123` из обоих engines не дублируется;
- русская и английская строки в разных областях обе сохраняются;
- две колонки на одной высоте не схлопываются;
- punctuation-only и numeric lines не исчезают и не размножаются.

Если в ходе реализации реальные Windows OCR fixtures показывают, что line-level selection теряет корректный mixed-script текст внутри одной строки, это отдельный измеримый повод перейти к word alignment. Не вводить word-level merge заранее без failing fixture.

## Повторный OCR выбранного фрагмента

Реализовать `SelectionTextRefiner` следующим образом:

1. Сгруппировать `selection.Words` по исходному `LineId` и восстановить порядок через document/selection reading order. При необходимости добавить в `TextSelectionRange` immutable line-order metadata, чтобы refiner не зависел от mutable controller state.
2. Для каждой выбранной строки построить tight union bounds выбранных слов.
3. Добавить вертикальный padding `max(2 px, round(lineHeight * 0.25))`; горизонтальный padding ограничить тем же значением, но он нужен только для сохранения крайних glyphs.
4. Clamp crop к `frozenFrame.PixelWidth/PixelHeight`.
5. Рассчитать scale так, чтобы медианная/исходная высота строки стала примерно 40 px: `scale = clamp(40 / sourceHeight, 1, 3)`.
6. Дополнительно clamp scale так, чтобы crop после увеличения не превышал `OcrEngine.MaxImageDimension`. Использовать качественный bitmap resample; не увеличивать уже крупный текст.
7. Распознать crop обоими доступными engines и объединить тем же merger/scorer.
8. После обратного mapping оставить только слова, центр которых попадает в исходный unpadded selected line bounds, расширенный максимум на 10% высоты. Это не даёт захватить соседние невыделенные слова из padding.
9. Собрать слова одной строки через пробел, строки — через `Environment.NewLine`.
10. Если строка refinement пустая, подставить предварительный текст именно этой строки, а не откатывать весь multi-line selection.

Refinement не должен менять видимые highlight bounds: пользователь выделял геометрию предварительного документа, а уточняется только payload команды.

Кэшировать resolved text вместе с generation текущего selection. Новый `Begin`, `Dismiss`, `SetDocument` или завершение нового range инвалидируют кэш и отменяют текущий refinement.

## Изменения TextSelectionOverlayController

- Инъектировать frozen `BitmapSource` и `ISelectionTextRefiner` через constructor.
- Сделать `OnCopy` и `OnSearch` `async void` только как WPF event boundaries; всю тестируемую логику вынести в private/internal `Task<string?> ResolveSelectionTextAsync(...)`.
- Перед await зафиксировать selection reference/generation и provider ID для Search.
- На время refinement отключить обе кнопки. Повторные clicks не должны запускать второй request.
- После await проверить `_disposed`, cancellation и generation. Устаревший результат игнорировать.
- Copy: использовать refined text, вызвать clipboard, показать существующий localized toast, затем снова включить кнопки и оставить action card открытой.
- Search: использовать refined text, поставить существующий `_searchPublished` guard и опубликовать ровно одну команду.
- При failed/no-text refinement использовать исходный `selection.Text` без error toast. Toast `TextCopyFailed` оставлять только для фактической ошибки clipboard.
- При dispose отменить controller-owned `CancellationTokenSource` и не обращаться к WPF controls после отмены.
- Не логировать preliminary/refined text.

Если refinement обычно укладывается в короткую задержку, не добавлять новый user-visible label. Если ручной QA показывает заметную задержку более примерно 300 мс, показать существующий loading visual или добавить локализованное `Recognizing…` с отложенным показом после 150 мс, чтобы избежать мерцания. Это решение принять по измерению, не блокируя базовую реализацию.

## Совместимость с экранным переводом

Внешний вид перевода не менять, но `ScreenTranslationWorkflow` должен перестать предполагать один source language:

1. Группировать `OcrLine` по normalized `Line.LanguageTag`.
2. Строки, язык которых эквивалентен target, не отправлять provider.
3. Для каждой оставшейся группы вызвать provider с её source tag и тем же target tag.
4. Объединить успешные chunks обратно по line ID/order.
5. Если все строки уже target-language — сохранить существующий `SameLanguage` outcome.
6. Если часть групп уже target-language, а часть переведена успешно, результат не считать partial failure только из-за пропущенных same-language строк.
7. Реальные service failures по одной языковой группе сохраняют существующую partial semantics.

Это изменение нужно только для корректности multilingual document; `ScreenTranslationOverlayController.Render`, `TranslationCardLayout`, palette и визуальные карточки не изменять.

## Настройки и миграция

- Удалить source OCR picker из `SettingsPanel`; target translation picker оставить.
- Удалить использование `PluginSettings.OcrLanguageTag`. Старое JSON-поле безопасно игнорируется десериализатором; не писать отдельную destructive migration.
- Удалить неиспользуемые `UiStrings.SettingsOcrLanguageLabel` и `UiStrings.SystemDefaultLanguage`/соответствующие XAML keys только если после изменения нет других consumers.
- `OcrLanguageCatalog.AvailableLanguages` продолжить передавать settings panel для существующего target-language picker, пока его источник данных не будет переработан отдельной задачей.
- Рядом с target picker допустимо показать read-only localized status автоматического OCR: оба packs активны / отсутствует русский / отсутствует английский. Если status добавляется, все варианты разместить в `Languages/*.xaml`.
- `CompositionRoot` создаёт language resolver, low-level Windows recognizer, merger/scorer, automatic recognizer и selection refiner; других мест assembly графа быть не должно.

## Изменения по файлам

### Изменить

- `CTS/TextRecognition/IOcrRecognizer.cs`
  - сделать основной контракт автоматическим;
  - добавить/вынести explicit-language контракт;
  - обновить disabled implementation и outcome aggregation semantics.
- `CTS/TextRecognition/WindowsOcrRecognizer.cs`
  - принимать обязательный tag;
  - убрать `TryCreateFromUserProfileLanguages`;
  - заменить full-frame downscale на tile recognition;
  - корректно map-ить bounds и освобождать WinRT bitmap каждого tile.
- `CTS/TextRecognition/OcrLanguageCatalog.cs`
  - добавить deterministic resolution `ru-RU`/`en-US` по установленным packs.
- `CTS/TextRecognition/OcrDocument.cs`
  - добавить `OcrLine.LanguageTag` и документировать dominant `OcrDocument.LanguageTag` через invariants/tests.
- `CTS/TextRecognition/TextSelectionRange.cs`
  - предоставить refiner достаточный immutable line order/preliminary line text.
- `CTS/Capture/OverlayInteractions/OcrOverlayController.cs`
  - удалить requested-language parameter и вызвать automatic contract.
- `CTS/Capture/OverlayInteractions/TextSelectionOverlayController.cs`
  - внедрить frame/refiner, добавить async resolution, cache, cancellation и stale-result guards.
- `CTS/Capture/OverlayInteractions/OverlayControllerFactory.cs`
  - передать один frozen frame OCR controller и text refinement controller;
  - удалить `_ocrLanguageTag` dependency.
- `CTS/Translation/ScreenTranslationWorkflow.cs`
  - batch-ить строки по source language без изменения визуального результата.
- `CTS/Settings/PluginSettings.cs`
  - удалить obsolete manual source-language setting.
- `CTS/Settings/SettingsPanel.cs`
  - убрать source picker, сохранить target picker.
- `CTS/CompositionRoot.cs`
  - собрать новый dependency graph только здесь.
- `CTS/Ui/UiStrings.cs`, `Languages/en.xaml`
  - удалить ставшие неиспользуемыми source-picker strings и добавить только реально используемый automatic-pack status, если он вводится.
- `README.md`
  - описать автоматический Russian/English OCR, зависимость от установленных Windows language packs и refinement before Copy/Search.

### Добавить

Имена можно скорректировать в рамках тех же ответственностей:

- `CTS/TextRecognition/AutomaticOcrRecognizer.cs`
- `CTS/TextRecognition/AutomaticOcrLanguageResolver.cs`
- `CTS/TextRecognition/OcrFrameTiler.cs`
- `CTS/TextRecognition/OcrDocumentMerger.cs`
- `CTS/TextRecognition/OcrTextQualityScorer.cs`
- `CTS/TextRecognition/ISelectionTextRefiner.cs`
- `CTS/TextRecognition/SelectionTextRefiner.cs`

Не объединять merger, scorer, crop logic и WPF event handling в один большой controller.

## Автоматические тесты

### Чистые unit tests

Добавить отдельные test classes:

- `AutomaticOcrLanguageResolverTests`
  - exact regional tags;
  - neutral fallback;
  - case-insensitive matching;
  - один missing pack;
  - оба missing;
  - deterministic order/no duplicates.
- `OcrFrameTilerTests`
  - изображение меньше max даёт один tile;
  - 3840x2160 покрывается полностью без downscale;
  - overlap/core regions не имеют дыр;
  - крайние word bounds mapping не выходят за source;
  - boundary duplicate принадлежит ровно одному tile.
- `OcrTextQualityScorerTests`
  - корректная кириллица превосходит приведённую латинскую ошибку;
  - корректный English не проигрывает ложной кириллице;
  - digits/punctuation и mixed alphanumeric identifiers не получают необоснованный hard reject;
  - control/replacement characters штрафуются.
- `OcrDocumentMergerTests`
  - все fixtures из раздела merge;
  - stable IDs/reading order;
  - different columns stay separate;
  - dominant document language взвешивается по буквам.
- `SelectionTextRefinerTests`
  - single word и multi-line crops;
  - adaptive 1x/2x/3x scaling;
  - clamp у всех краёв кадра;
  - padding не добавляет соседнее слово;
  - одна failed refined line откатывается только к своей preliminary line;
  - cancellation не возвращает success;
  - результат сохраняет `Environment.NewLine`.

Все algorithmic tests должны использовать fake `ILanguageOcrRecognizer`; они не должны зависеть от установленных language packs.

### Обновить существующие tests

- `WindowsOcrRecognizerTests`
  - обязательный explicit tag;
  - bounds mapping с tile offset;
  - cancellation;
  - существующий synthetic English smoke test;
  - Cyrillic smoke test запускать детерминированно только в окружении, где resolver видит русский pack; отсутствие pack проверять как outcome, а не превращать suite в flaky failure/skip.
- `OcrOverlayControllerTests`
  - automatic interface без tag;
  - partial language failures aggregated вне controller;
  - dispose/cancellation не доставляет поздний result.
- `PointerGestureRouterTests` либо новый `TextSelectionOverlayControllerTests`
  - Copy ждёт refinement и пишет refined Cyrillic;
  - refinement failure копирует preliminary text;
  - Search публикует refined text ровно один раз;
  - double click запускает один refinement;
  - Copy затем Search использует cache;
  - новая selection инвалидирует cache;
  - dispose во время await не пишет clipboard и не публикует command.
- `TextSelectionRangeTests`
  - preliminary text по строкам доступен refiner без потери reading order.
- `ScreenTranslationWorkflowTests`
  - mixed ru/en groups вызывают provider с правильными source tags;
  - target-equivalent lines не отправляются;
  - all-target возвращает `SameLanguage`;
  - failure одной language group даёт корректный partial result.
- `OcrTranslationSettingsTests`, `UiStringsTests`
  - manual source setting/picker удалены;
  - старое JSON с `OcrLanguageTag` десериализуется без ошибки;
  - новые status strings, если добавлены, существуют.
- `PrivacyLoggingTests`
  - ни preliminary text, ни candidates, ни refined text не попадают в log.

## Ручная проверка

Проводить на frozen frame, а не на отдельно сохранённом OCR demo:

1. Русская строка `добавить уведомление`: выделить оба слова, Copy, сравнить clipboard посимвольно.
2. Та же строка: Search, убедиться, что URL/query получает точную кириллицу.
3. Английская строка `Add notification`: Copy и Search не должны транслитерировать её в кириллицу.
4. Один экран с отдельными русскими и английскими абзацами: оба должны выделяться и копироваться правильно без переключения setting.
5. Смешанная строка `Telegram — добавить notification`: зафиксировать фактический результат. При провале добавить failing fixture до усложнения merger.
6. Мелкий текст на 1920x1080, 2560x1440 и 3840x2160; проверить, что 4K больше не проходит через full-frame downscale.
7. DPI 100%, 125%, 150%: highlight geometry остаётся на исходных словах, refinement меняет только payload.
8. Single-word selection рядом с другим словом: padding не должен копировать соседа.
9. Быстро нажать Copy дважды: один refinement, одна clipboard operation/toast.
10. Нажать Search и сразу закрыть overlay: поздний result не должен открыть браузер.
11. Временно убрать русский pack: английский продолжает работать, отсутствие русского отражается согласованным status/outcome.
12. Нажать Translate на mixed-language screen: provider requests используют line source languages, но внешний вид cards остаётся прежним.

## Команды проверки

```powershell
dotnet build .\CircleFlow.csproj -c Release --no-restore
dotnet test .\tests\CircleToSearch.Tests\CircleToSearch.Tests.csproj -c Release --no-restore
```

Зелёным считается только запуск с ожидаемым ненулевым числом тестов. После targeted tests обязательно выполнить полный suite и указать итоговые passed/failed/skipped counts.

## Acceptance criteria

- В settings нет ручного выбора source OCR language.
- При установленных русском и английском Windows OCR packs оба движка участвуют автоматически.
- `добавить уведомление` копируется и отправляется в Search точной кириллицей.
- Корректный английский текст не деградирует при включённом русском проходе.
- Русские и английские строки на одном кадре доступны одновременно.
- Copy/Search используют refinement исходного crop; повторное действие на неизменённом selection использует cache.
- Ошибка refinement откатывается к предварительному merged text и не ломает Copy/Search.
- Full-screen 4K frame распознаётся тайлами без глобального уменьшения.
- Bounds после tiles/merge остаются capture-relative physical pixels; hit testing и highlights не смещаются при DPI scaling.
- OCR concurrency ограничена двумя вызовами; cancellation и stale async completions безопасны.
- Mixed-language document не отправляется переводчику с одним неверным source tag.
- Визуальные translated cards не изменены.
- OCR/clipboard/search text отсутствует в логах.
- Полный тестовый suite проходит с ненулевым числом тестов; отдельно проверены исходный русский repro и обратный английский случай.

## Явные нецели и дальнейшая работа

- Не исправлять `ScreenTranslationOverlayController.Render` и `TranslationCardLayout`.
- Не делать inpainting, подбор фона, font matching или новый translated overlay.
- Не добавлять ручной source-language picker.
- Не запускать все установленные языки автоматически.
- Не добавлять cloud OCR, Tesseract, словарный пакет или тяжёлую ML-модель.
- Не менять жесты выделения, hit tolerance или action-card placement без отдельного failing test.
- Поддержка третьего языка должна добавляться расширением candidate policy и language profile после измерения latency/accuracy, а не неограниченным перебором packs.

## Допущения и открытые вопросы

- Допущение: текущий продуктовый scope — русский и английский. Если требуется другой обязательный язык, candidate policy и acceptance matrix нужно согласовать до реализации.
- Допущение: задержка refinement перед Copy/Search до нескольких сотен миллисекунд приемлема ради качества; UI-индикатор вводится только после измерения реальной задержки.
- Открытый технический вопрос: достаточно ли line-level merge для реальных mixed-script строк. Решение принимается по fixture `Telegram — добавить notification`; при успешном результате word-level merge не нужен.
- Открытый UX-вопрос: показывать ли read-only предупреждение об отсутствующем `ru`/`en` pack в settings. Это не влияет на core pipeline, но выбранное поведение должно быть единообразно покрыто localized strings и tests.
