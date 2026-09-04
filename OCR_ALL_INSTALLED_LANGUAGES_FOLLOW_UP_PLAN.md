# Follow-up план: OCR-выделение для всех установленных языков

## Статус и назначение

Это самостоятельный implementation handoff для следующего AI-агента. План описывает изменения, но не реализует их.

Цель: убрать ограничение автоматического OCR языками `ru` и `en` и позволить выделять, копировать и отправлять в Search текст на любом языке, для которого в Windows установлен OCR language pack. Ручного выбора исходного языка не будет. Все доступные движки разрешено запускать автоматически; глобальная параллельность остаётся равной двум.

Экранный перевод, определение source language для MyMemory и смена translation provider в этот follow-up не входят. Для Copy/Search важен точный текст; точность `OcrWord.LanguageTag` между несколькими языками с одним письмом, например English/German/French, не является acceptance criterion этой задачи.

## Проверенное текущее состояние

- Рабочая ветка основана на commit `4bd79d6`; рассматриваемая RU/EN-реализация пока находится в working tree.
- `dotnet test .\tests\CircleToSearch.Tests\CircleToSearch.Tests.csproj -c Release --no-restore` выполняет 489 тестов: 489 passed, 0 failed, 0 skipped.
- `OcrLanguageCatalog` уже читает все установленные языки из `OcrEngine.AvailableRecognizerLanguages`.
- `AutomaticOcrLanguageResolver.PreferredTags` содержит только `ru-RU` и `en-US`. Поэтому остальные установленные packs отбрасываются до запуска OCR.
- `AutomaticOcrRecognizer` и `SelectionTextRefiner` используют один resolver. Ограничение сейчас действует и на полноэкранную геометрию, и на повторное распознавание crop перед Copy/Search.
- `WindowsOcrRecognizer` принимает обязательный language tag, распознаёт большие кадры тайлами и использует общий `SemaphoreSlim` с concurrency 2.
- `OcrDocumentMerger` умеет объединять результаты разных движков, включая mixed-script строки и различную сегментацию слов.
- `OcrTextQualityScorer` специализирован под русский и английский: для любого tag кроме `ru*` выбирается английский профиль и Latin script.
- `SelectionTextRefiner.MatchesPreliminaryScript` считает Cyrillic совместимым только с `ru*`, а Latin — только с `en*`.
- Сообщение `plugin_circletosearch_ocr_language_unavailable` и README утверждают, что обязательны именно Russian/English packs.
- Ручной source-language picker уже удалён. Возвращать его нельзя.

## Зафиксированные продуктовые решения

1. Использовать все exact language tags из `OcrEngine.AvailableRecognizerLanguages`.
2. Не вводить whitelist, hard limit количества языков или ручной source-language picker.
3. Не сворачивать regional variants по neutral tag: `en-US`, `en-GB`, `zh-Hans` и `zh-Hant` считаются отдельными установленными recognizers. Удаляются только точные case-insensitive дубликаты tags.
4. Порядок tags должен быть детерминированным: ordinal-ignore-case по normalized tag.
5. Не больше двух OCR operations могут одновременно подготавливать крупный tile и вызывать WinRT OCR. Общее число языков этим не ограничивается.
6. Рост времени распознавания при 3–4 установленных packs приемлем.
7. Overlay по-прежнему показывается немедленно, а OCR выполняется в фоне.
8. Copy и Search продолжают использовать refinement и общий cache для текущего selection.
9. Существующие специальные защиты от русского mojibake сохраняются, но generic pipeline не должен считать любой не-русский язык английским.
10. Translation workflow и MyMemory provider не менять. Автоопределение source language будет отдельной будущей задачей вместе со сменой переводчика.

## Желаемое поведение

### Full-screen OCR

1. Catalog возвращает все установленные Windows OCR packs.
2. Resolver нормализует, дедуплицирует и детерминированно сортирует все их tags без языкового whitelist.
3. Automatic recognizer запускает каждый разрешённый language engine.
4. Общий scheduler допускает не более двух одновременно выполняющихся тяжёлых tile operations.
5. Успешные документы объединяются существующим geometry-aware merger.
6. Ошибка одного full-screen language pass не уничтожает успешные результаты остальных языков.
7. Если catalog пуст, возвращается `LanguageUnavailable`.

### Выделение и Copy/Search

1. Текст любого успешно распознанного языка участвует в hit testing и может быть выделен.
2. Первый Copy/Search повторно распознаёт crop всеми установленными engines с тем же ограничением concurrency 2.
3. Refinement не отклоняет German/French как «не English», Ukrainian/Bulgarian как «не Russian» и не предполагает только Latin/Cyrillic.
4. Если из нескольких refinement passes успешен один, его фактическое письмо сравнивается с письмом preliminary text. Сравнение не зависит от конкретного BCP-47 tag.
5. Несовместимый lone candidate не заменяет preliminary text. Например, Latin lookalike не заменяет Cyrillic, а Latin не заменяет Greek/Arabic/Han.
6. При mixed-script preliminary lone candidate принимается только если покрывает все значимые scripts preliminary text; иначе используется preliminary text.
7. Copy и Search получают одинаковый resolved text и сохраняют текущие cancellation/cache guarantees.

## Архитектурные изменения

### 1. Сделать resolver языково-нейтральным

Изменить `CTS/TextRecognition/AutomaticOcrLanguageResolver.cs`:

- удалить `PreferredTags` и neutral-language fallback;
- взять все непустые `OcrLanguageOption.Tag`;
- нормализовать tags через `Trim()`;
- удалить только exact case-insensitive дубликаты;
- отсортировать `StringComparer.OrdinalIgnoreCase`;
- вернуть immutable snapshot;
- пустой catalog по-прежнему даёт пустой результат.

Не переименовывать resolver только ради изменения политики: `AutomaticOcrLanguageResolver` остаётся корректным названием.

### 2. Ввести generic Unicode-script анализ

Добавить чистый компонент `CTS/TextRecognition/OcrUnicodeScriptClassifier.cs`, не зависящий от WPF, WinRT controls или translation provider.

Он должен анализировать `Rune`, а не UTF-16 `char`, и классифицировать как минимум:

- Latin;
- Cyrillic;
- Greek;
- Arabic;
- Hebrew;
- Devanagari;
- Han;
- Hiragana/Katakana;
- Hangul;
- Thai;
- OtherLetter.

Пунктуация, цифры, whitespace, emoji и symbols не считаются письмом. Результат анализа должен содержать количество букв по scripts, dominant script и набор значимых scripts. Script считается значимым, если содержит хотя бы две буквы либо не менее 20% всех букв; это не даёт случайной единичной букве блокировать mixed-script refinement.

Classifier используется и scorer, и refiner через constructor injection. Создавать его нужно только в `CTS/CompositionRoot.cs`.

### 3. Обобщить `OcrTextQualityScorer`

Изменить `CTS/TextRecognition/OcrTextQualityScorer.cs`:

- удалить правило `ru ? RussianBigrams : EnglishBigrams`;
- хранить optional language profiles только для явно поддержанных profiles (`ru` и `en` на первом этапе);
- для неизвестного языка profile contribution равен нулю, а не English score;
- заменить `IsNativeRussian` на нейтральные признаки качества: script distribution, полезные символы, invalid characters, mixed alphanumeric penalties и optional profile ratio;
- сохранить проверенные RU/EN fixtures как специальные регрессионные случаи;
- `ChooseEquivalent` при одинаковом normalized text не должен искать только Russian/English candidate: выбирать candidate по generic quality, затем по `languageOrder`;
- для конфликтующих кандидатов без language profile использовать generic score и deterministic language order;
- не отбрасывать строки только из цифр/пунктуации.

Нельзя добавлять словари всех языков или сетевой language detection в эту задачу. Generic scoring является best-effort выбором OCR-текста; translation source identification отложен.

### 4. Сделать refinement script-aware, а не RU/EN-aware

Изменить `CTS/TextRecognition/SelectionTextRefiner.cs`:

- заменить `MatchesPreliminaryScript(string languageTag, string preliminaryText)` сравнением scripts фактического preliminary text и текста единственного успешного `OcrDocument`;
- не выводить script из language tag;
- pure Latin preliminary совместим с German/French/English Latin candidate;
- pure Cyrillic preliminary совместим с Ukrainian/Russian/Bulgarian Cyrillic candidate;
- Greek, Arabic, Hebrew, Indic, CJK и Hangul сравниваются своими script groups;
- для mixed-script preliminary candidate должен содержать все значимые preliminary scripts;
- если preliminary не содержит букв, разрешить candidate как сейчас;
- сохранить fallback при `Failed`, `PlatformUnavailable`, `LanguageUnavailable` и cancellation semantics;
- не логировать preliminary/refined text или candidates.

Чтобы сравнивать фактический candidate text, использовать `documents[0].Lines.Select(line => line.Text)`, а не `documents[0].LanguageTag`.

### 5. Реально ограничить тяжёлую работу двумя operations

Проверить `CTS/TextRecognition/WindowsOcrRecognizer.cs` и scheduler boundary.

Сейчас pixel preparation происходит до `_scheduler.WaitAsync`. При запуске всех языков это позволяет каждому pass одновременно выделить крупный BGRA tile buffer. Перенести получение semaphore до `PreparePixels`, а освобождение — после завершения `RecognizeAsync` и чтения tile result. Требования:

- одновременно существует не более двух активно подготавливаемых/распознаваемых tiles через этот recognizer;
- ожидание semaphore принимает cancellation token;
- semaphore освобождается во всех exception/cancellation paths;
- последовательность tiles внутри одного language pass сохраняется;
- `SoftwareBitmap` всегда освобождается;
- не сериализовать работу полностью: две операции всё ещё могут идти параллельно.

Не добавлять второй независимый scheduler в controller или composition root.

### 6. Обновить пользовательский текст

Изменить:

- `Languages/en.xaml` — generic сообщение наподобие `Install at least one Windows OCR language pack to recognize screen text.`;
- `README.md` — описать автоматическое использование всех установленных Windows OCR packs, отсутствие ручного выбора и concurrency 2;
- `CTS/Ui/UiStrings.cs` менять только если меняется набор keys, а не только значение существующего key.

Все пользовательские строки остаются в `Languages/*.xaml`; C#-литералы для UI не добавлять.

## Изменения по файлам

### Изменить

- `CTS/TextRecognition/AutomaticOcrLanguageResolver.cs`
  - вернуть все установленные exact tags;
  - deterministic order и exact deduplication.
- `CTS/TextRecognition/OcrTextQualityScorer.cs`
  - generic scoring;
  - optional RU/EN profiles вместо English-by-default;
  - generic equivalent-candidate selection.
- `CTS/TextRecognition/SelectionTextRefiner.cs`
  - сравнивать фактические scripts текстов;
  - убрать `StartsWith("ru")`/`StartsWith("en")` из safety gate.
- `CTS/TextRecognition/WindowsOcrRecognizer.cs`
  - включить pixel preparation в существующий concurrency boundary.
- `CTS/CompositionRoot.cs`
  - создать один classifier и передать его scorer/refiner.
- `Languages/en.xaml`
  - generic OCR-pack message.
- `README.md`
  - all-installed-languages behavior и performance note.
- существующие OCR/refinement tests
  - заменить предположения о двух вызовах на ожидание всех catalog entries.

### Добавить

- `CTS/TextRecognition/OcrUnicodeScriptClassifier.cs`.
- `tests/CircleToSearch.Tests/OcrUnicodeScriptClassifierTests.cs`.

Не изменять:

- `CTS/Translation/*`;
- translation settings/provider contracts;
- layout и rendering translated cards;
- жесты выделения и action-card placement;
- `Main.cs`.

## Автоматические тесты

### `AutomaticOcrLanguageResolverTests`

- возвращает `de-DE`, `en-US`, `fr-FR`, `ru-RU`, `uk-UA` одновременно;
- работает, когда установлен только `de-DE` или только `ja-JP`;
- пустой catalog возвращает пустой список;
- удаляет case-insensitive exact duplicates;
- сохраняет regional variants как отдельные tags;
- результат детерминирован независимо от входного порядка;
- нет специального требования присутствия `ru` или `en`.

### `AutomaticOcrRecognizerTests`

- вызывает fake recognizer для каждого catalog tag;
- partial failure одного языка сохраняет документы других языков;
- только один установленный non-RU/EN pack даёт Success;
- отсутствие packs даёт `LanguageUnavailable`;
- cancellation при нескольких языках не доставляет поздний документ.

### `OcrUnicodeScriptClassifierTests`

- Latin: English, German с umlaut, French с accents;
- Cyrillic: Russian, Ukrainian и Bulgarian;
- Greek;
- Arabic;
- Hebrew;
- Devanagari;
- Han и Japanese mixed Han/Kana;
- Hangul;
- punctuation/digits/emoji не становятся scripts;
- mixed-script significance threshold не реагирует на одиночную случайную букву.

### `OcrTextQualityScorerTests`

- сохранить все существующие RU/EN regressions;
- unknown Latin tag не использует English bigrams;
- чистый German/French candidate остаётся useful;
- Ukrainian Cyrillic не штрафуется как неправильный English;
- Greek/Arabic/Japanese/Hangul candidates остаются useful;
- одинаковый normalized text выбирается детерминированно без RU/EN special case;
- control/replacement characters и смешение цифр внутри слов по-прежнему штрафуются.

### `SelectionTextRefinerTests`

- German preliminary принимает German/Latin refined result при `NoText` от других packs;
- Ukrainian preliminary принимает Cyrillic result с `uk-UA`;
- Greek/Arabic/Japanese cases принимают совпадающее письмо;
- Cyrillic preliminary не заменяется Latin lookalike;
- Greek preliminary не заменяется Latin candidate;
- mixed Latin+Cyrillic preliminary не заменяется lone Latin-only result;
- text without letters сохраняет текущую fallback semantics;
- один failed pass откатывает только соответствующую строку;
- Copy/Search cache и cancellation tests остаются зелёными.

### Concurrency tests

Добавить test seam вокруг тяжёлой tile operation либо выделить существующий scheduler в узкий внутренний компонент. Fake operation должна подтвердить:

- при 4–6 языках наблюдаемый peak concurrency равен 2;
- cancellation ожидающих operations завершается без deadlock;
- exception одной operation не теряет semaphore permit;
- выполняется ожидаемое ненулевое число operations.

Не делать algorithmic tests зависимыми от конкретного набора установленных языков на CI-машине.

## Ручная проверка

Проверять только packs, реально установленные в Windows; отсутствие конкретного дополнительного pack не считать test skip полного suite.

1. На системе с `ru` и `en` повторить `добавить уведомление`, `Add notification` и mixed-script fixture; регрессий быть не должно.
2. Установить German pack, открыть немецкий текст с `ä/ö/ü/ß`, выделить и сравнить Copy посимвольно.
3. Установить Ukrainian pack, проверить `додати сповіщення`, включая `і/ї/є`.
4. Проверить один язык с другим письмом, например Greek, Arabic или Japanese.
5. Показать на одном экране строки минимум трёх установленных языков; все должны получить selectable word bounds.
6. Выполнить Copy и Search для каждого языка; payload должен совпадать.
7. Повторный Copy/Search неизменённого selection не должен запускать новый refinement.
8. Закрыть overlay во время многоязычного OCR/refinement; поздний clipboard write/search publish не допускается.
9. На 4K проверить отсутствие global downscale и корректность bounds.
10. На машине без `ru`/`en`, но с другим OCR pack, overlay не должен возвращать `LanguageUnavailable`.
11. На машине без единого OCR pack должен отображаться generic localized error.
12. Измерить cold full-screen latency и peak working set при 2 и 4 packs; результаты записать в handoff, но не вводить hard limit без отдельного продуктового решения.

## Команды проверки

```powershell
dotnet build .\CircleFlow.csproj -c Release --no-restore
dotnet test .\tests\CircleToSearch.Tests\CircleToSearch.Tests.csproj -c Release --no-restore
git diff --check
```

Зелёным считается только test run с ожидаемым ненулевым количеством тестов. После targeted tests обязательно выполнить полный suite и сообщить passed/failed/skipped counts.

## Acceptance criteria

- В production-коде нет списка разрешённых OCR-языков `ru/en`.
- Каждый exact tag из `OcrLanguageCatalog.AvailableLanguages` передаётся automatic recognizer.
- Приложение работает, если установлен только один non-RU/EN OCR pack.
- Все распознанные языки участвуют в word hit testing, selection, Copy и Search.
- German/French/Ukrainian и хотя бы один non-Latin/non-Cyrillic fixture проходят unit tests.
- Исходные RU/EN и mixed-script regressions остаются зелёными.
- Lone refinement candidate не заменяет preliminary text текстом несовместимого Unicode script.
- Copy и Search используют один cached resolved text.
- Общая тяжёлая OCR concurrency не превышает 2 независимо от числа packs.
- Full-screen OCR одного языка может упасть без потери успешных документов остальных языков.
- Нет ручного source-language picker и нет hard cap числа языков.
- OCR/clipboard/search text не появляется в логах.
- Translation provider, translation source detection и translated-card UI не изменены.
- README и localized unavailable message больше не требуют Russian/English packs.
- Release build проходит без warnings/errors; полный suite проходит с ненулевым числом тестов.

## Явные нецели

- Автоопределение языка для переводчика.
- Смена MyMemory или добавление API keys.
- Гарантия правильного source-language tag между языками с одинаковым письмом.
- Ручной выбор OCR language.
- Новый UI списка активных languages.
- Словари и language models для каждого языка.
- Изменение translation cards, их layout, фона или inpainting.
- Изменение жестов выделения.

## Допущения и открытые вопросы

Допущения:

- Обычно установлено 2–4 Windows OCR packs, поэтому линейный рост общей latency приемлем.
- Пользователь под «поддержкой языка» имеет в виду корректные selectable bounds и Copy/Search text, а не достоверное определение языка для перевода.
- Все установленные exact recognizers должны участвовать автоматически.
- Concurrency 2 является фиксированным техническим ограничением, а не настройкой пользователя.

Открытых продуктовых вопросов для реализации этого follow-up нет. Translation autodetection намеренно отложен до выбора другого бесплатного provider без пользовательских API keys.
