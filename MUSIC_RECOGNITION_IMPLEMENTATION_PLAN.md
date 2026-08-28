# План интеграции прогрессивного распознавания музыки

## Цель

Добавить в основной Circle to Search plugin zero-config распознавание музыки из текущего
Windows output через WASAPI loopback и неофициальный Shazam endpoint. Запуск выполняется отдельной
кнопкой рядом с нижним selection chip. Кнопка не должна быть дочерним элементом chip и не должна
запускать lasso. Полноценный визуальный дизайн listening/result UI откладывается; эта итерация должна
дать надёжный рабочий сценарий и минимальную системную обратную связь.

Прогрессивный сценарий делает не более трёх последовательных запросов на накопленном звуке:

| Попытка | Момент от начала capture | Что отправляется |
| --- | ---: | --- |
| 1 | 4 с | fingerprint примерно первых/последних доступных 4 с |
| 2 | 8 с | новый fingerprint накопленных 8 с |
| 3 | 12 с | новый fingerprint накопленных 12 с |

Первое совпадение немедленно завершает capture и сессию. `12 с` — максимальная длительность записи,
а не обязательная задержка. Одновременно разрешён только один HTTP-запрос; старты запросов разделяет
не менее 4 секунд. Отсутствие совпадения — единственная причина перейти к следующей точке.

## Проверенное текущее состояние

- `Main.cs` остаётся тонким Flow Launcher adapter; dependency graph собирается в
  `CTS/CompositionRoot.cs`.
- `CTS/Search/SearchCoordinator.cs` сериализует одну overlay/search-сессию, владеет cancellation и
  различает `Idle`, `Selecting`, `Uploading`.
- `CTS/Capture/OverlayWindow.cs` создаёт отдельный STA overlay, возвращает
  `SelectionOutcome?` и передаёт владение frozen bitmap координатору только после lasso.
- `CTS/Capture/OverlayVisualFactory.cs` строит chip как нижний дочерний элемент root grid. Сам chip —
  `Border`, а не кнопка. Window-level mouse handlers начинают lasso для обычного клика.
- Рабочий PoC расположен в `poc/MusicRecognition.Poc`: `LoopbackRecorder`, audio preprocessing,
  Shazam-compatible fingerprint/codec и `ShazamClient` уже прошли локальную проверку. На тестовом
  аудио совпадение получено даже по 4-секундному фрагменту.
- PoC сейчас делает одну запись фиксированной длины и только после неё один запрос. Для progressive
  режима capture должен продолжаться независимо от fingerprint/HTTP attempt и предоставлять
  безопасные snapshots.
- Основной проект пока не ссылается на `NAudio` и `MathNet.Numerics`; `poc/**` исключён из его
  компиляции.
- Все фиксированные цвета должны оставаться в `CTS/Ui/PluginPalette.cs`, а все видимые строки — в
  `Languages/*.xaml` через `CTS/Ui/UiStrings.cs`.

## Границы этой итерации

Входит:

- отдельная рабочая music-кнопка рядом с chip;
- progressive capture/fingerprinting/lookup с ранней остановкой;
- cancellation, защита от параллельных сессий и умеренный request rate;
- минимальный показ результата средствами Flow Launcher;
- перенос production-кода PoC в основной plugin, тесты, packaging и licensing metadata.

Не входит:

- пользовательские API keys или backend;
- microphone capture: распознаётся только звук default Windows output device;
- постоянное фоновое распознавание в стиле Pixel Now Playing;
- выбор audio device и настройка checkpoint-ов пользователем;
- polished waveform/listening/result overlay, обложка альбома и история результатов;
- автоматические бесконечные повторы при ошибках или `429`.

## Желаемое поведение

1. Пользователь вызывает существующий overlay горячей клавишей или Flow query.
2. В нижней части видны два соседних элемента: существующий selection chip и отдельная music-кнопка.
   Они могут находиться в общем horizontal container, но music-кнопка не вложена в `Border` chip.
3. Клик, Enter или Space по music-кнопке:
   - не добавляет lasso points и не создаёт visual selection;
   - завершает overlay с действием `MusicRecognition`;
   - освобождает frozen screenshot, потому что музыкальному workflow он не нужен;
   - запускает loopback capture default output без настроек или countdown.
4. Первая lookup-попытка начинается после 4 секунд пригодного capture. При совпадении запись
   останавливается сразу; типичное время результата — около 4–5 секунд плюс фактическая сеть.
5. После `no match` capture продолжается, следующие попытки используют больше накопленного звука на
   8 и 12 секундах. После третьего `no match` показывается локализованное «трек не найден».
6. При совпадении Flow показывает artist и title. Если ответ содержит допустимый Shazam HTTPS URL,
   сообщение имеет кнопку «Open in Shazam»; иначе результат всё равно показывается без кнопки.
7. Silence/no usable peaks не расходуют HTTP-запрос. Если пригодный fingerprint не появился ни в
   одной точке, показывается отдельная подсказка, что на default output не обнаружен звук.
8. Повторное нажатие hotkey во время `RecognizingMusic` отменяет capture/request и молча возвращает
   состояние в `Idle`. Flow query во время любой активной сессии по-прежнему не создаёт вторую.
9. `Escape`, right click и deactivation до выбора действия сохраняют текущую отмену overlay.
10. Visual search через lasso продолжает работать без изменения пользовательского поведения.

## Архитектура и контракты

### 1. Результат overlay

В `CTS/Capture/SelectionOutcome.cs` добавить простой discriminated-by-enum контракт без иерархий:

- `OverlayAction`: `VisualSelection`, `MusicRecognition`;
- `OverlayOutcome`: action плюс nullable `SelectionOutcome`;
- factory methods/validated constructor, чтобы `VisualSelection` всегда имел selection, а
  `MusicRecognition` — нет.

Изменить `OverlayWindow.SelectAsync` и внутренний `RunOnce` на `Task<OverlayOutcome?>`. Frozen frame
остаётся у `SelectionOutcome` только для visual action. На music/cancel/error frame должен быть
освобождён внутри overlay path; нельзя передавать ненужный bitmap в music service.

`SearchCoordinator` получает `Func<CancellationToken, Task<OverlayOutcome?>>`, разбирает action после
закрытия overlay и либо выполняет существующий visual workflow, либо вызывает injected music
recognizer. Не переносить orchestration в `Main.cs`.

### 2. UI кнопки

В `CTS/Capture/OverlayVisualFactory.cs`:

- заменить единственный bottom-aligned chip node на bottom-aligned horizontal action tray;
- оставить существующий `Chip` отдельным первым ребёнком tray;
- создать `Button MusicButton` вторым sibling с небольшим gap;
- вернуть tray и button через `OverlayVisual`;
- применять entrance/exit к tray целиком, чтобы оба элемента появлялись и исчезали согласованно;
- использовать только значения `PluginPalette` и локализованные tooltip/accessibility strings;
- для первой версии достаточно простой note icon и стандартных focus/click states — внешний вид
  специально не доводить до финального дизайна.

В `OverlayWindow` подписать `MusicButton.Click` на отдельный handler. Явно гарантировать, что routed
mouse event от кнопки не попадает в lasso handlers: пометить его handled на границе кнопки и добавить
guard по visual ancestry/`OriginalSource` в window mouse-down handler. Полагаться только на неявное
поведение WPF `Button` нельзя — это должен защищать interaction test.

После click выставить `_finished`, прекратить render callbacks/mouse capture, установить
`OverlayOutcome.MusicRecognition`, закрыть STA dispatcher без selection hold. Не применять cancel
click-through, чтобы исходный клик не прошёл в приложение под overlay.

### 3. Production music-модуль

Создать `CTS/MusicRecognition/` и перенести туда проверенные части PoC с namespace
`CircleToSearch.MusicRecognition`:

- `Audio/CapturedAudio.cs`
- `Audio/MonoSampleProvider.cs`
- `Audio/AudioPreprocessor.cs`
- новый snapshot-capable `Audio/LoopbackCaptureSession.cs`
- `Fingerprinting/Crc32.cs`
- `Fingerprinting/ShazamSignature.cs`
- `Fingerprinting/ShazamSignatureCodec.cs`
- `Fingerprinting/ShazamSignatureGenerator.cs`
- `Shazam/ShazamClient.cs`
- `Shazam/ShazamRecognition.cs`
- новый `ProgressiveMusicRecognizer.cs`
- новый `MusicRecognitionOutcome.cs`
- новый `ShazamRequestThrottle.cs`

Не копировать текущий `LoopbackRecorder.RecordAsync(duration)` как production abstraction: он отдаёт
данные только после окончания duration и поэтому не позволяет ранний результат.

`LoopbackCaptureSession` должен:

- запустить один `WasapiRecorderBuilder().WithLoopbackCapture()`;
- читать buffers в отдельной долгоживущей async-задаче;
- сохранять raw PCM в ограниченный 12-секундный buffer с известным `WaveFormat`;
- отдавать consistent copy через `Snapshot()` без остановки capture;
- корректно завершаться по match, cancel, device error, plugin dispose и max capture duration;
- не держать lock во время preprocessing/fingerprinting/HTTP;
- быть `IAsyncDisposable` и гарантировать завершение capture task перед освобождением NAudio object.

Для unit tests отделить capture и lookup небольшими интерфейсами или constructor-injected delegates.
Использовать composition/constructor injection; не вводить base classes, service locator или static
mutable singleton.

### 4. Progressive scheduler

`ProgressiveMusicRecognizer.RecognizeAsync` реализует policy `[4s, 8s, 12s]` относительно единого
capture-start timestamp:

1. Проверить process-lifetime Shazam cooldown до запуска capture. Если cooldown активен, вернуть
   `RateLimited` без аудиозаписи и HTTP.
2. Запустить capture и monotonic stopwatch.
3. Для каждого checkpoint ждать только оставшееся время до абсолютной точки. Пока HTTP/fingerprint
   обрабатываются, capture продолжает наполнять buffer.
4. Взять snapshot, преобразовать его в mono 16 kHz float samples, посчитать RMS и fingerprint вне
   WPF dispatcher thread.
5. При silence/zero peaks пропустить сеть и перейти к следующей абсолютной точке.
6. Перед POST пройти общий `ShazamRequestThrottle`; одновременно допускается один запрос, а разница
   между временем старта POST должна быть не меньше 4 секунд.
7. `match` немедленно возвращает `Matched`; `no match` разрешает следующую точку; cancellation,
   rate-limit, HTTP/service error немедленно прекращают цикл. Не повторять transport failures.
8. В `finally` остановить и dispose capture независимо от результата.

Если первый HTTP-ответ приходит уже после следующего checkpoint, не запускать запрос параллельно.
После ответа взять свежий snapshot и всё равно пройти 4-секундный request gate. Для каждого POST
fingerprint должен быть построен из самого свежего доступного накопленного audio, но не более 12 с.

Добавить per-request timeout (ориентир 8 секунд) и общий session cancellation. Timeout считается
service/network failure, а не `no match`; автоматический retry в той же сессии не делается.

### 5. `429` и умеренный rate

Изменить `ShazamClient`, чтобы `429` был typed outcome/exception и сохранял `Retry-After`, если header
присутствует. Не определять rate limit по тексту exception.

При первом `429`:

- немедленно остановить progressive session;
- не выполнять оставшиеся checkpoint requests;
- поставить общий для экземпляра plugin cooldown до `Retry-After`, либо на 60 секунд, если header
  отсутствует/некорректен;
- все новые music sessions во время cooldown отклонять до capture/HTTP с локализованным сообщением;
- логировать только status, длительность cooldown и номер attempt, не signature URI и не raw JSON.

Базовые ограничения `[4, 8, 12]`, максимум 3 POST за ручной запуск, один active session и общий
4-секундный gate должны оставаться константами policy, а не пользовательскими settings. Это сохраняет
plug-and-play поведение и не поощряет агрессивную настройку endpoint polling.

### 6. Coordinator и показ результата

В `CTS/Search/SearchCoordinator.cs`:

- добавить `RecognizingMusic` в `SearchState`;
- внедрить music recognizer и callbacks показа обычного сообщения/сообщения с кнопкой;
- вынести существующую visual-ветку в private method, чтобы общий session lifecycle оставался один;
- переименовать `CancelActiveSelection` в `CancelActiveSession` и обновить callers/tests;
- второй hotkey при `Selecting` или `RecognizingMusic` отменяет; при visual `Uploading` остаётся
  проигнорированным;
- всегда возвращать `Idle` и очищать CTS в `finally`.

Минимальная presentation без нового кастомного result window:

- `Matched`: Flow `ShowMsgWithButton` с `Artist — Title`, album/genre при наличии и кнопкой открытия
  Shazam; при отсутствии безопасного URL использовать `ShowMsg`;
- `NoMatch`: обычное локализованное сообщение, не error;
- `NoAudio`: обычное локализованное сообщение с упоминанием default output;
- `RateLimited`, endpoint/network/device failure: локализованная ошибка;
- `Canceled`: ничего не показывать.

Проверять result URL перед `Process.Start`: только absolute `https` и ожидаемый `shazam.com` host или
его subdomain. Невалидный URL не превращает найденный track в ошибку — просто не показывается кнопка.

В `CTS/CompositionRoot.cs` собрать и передать ровно по одному `HttpClient`/`ShazamClient`, request
throttle и recognizer на lifetime plugin. Здесь же адаптировать `api.ShowMsg`,
`api.ShowMsgWithButton`, `api.ShowMsgError` и существующий URL opener. `PluginRuntime.Dispose` сначала
отменяет active session, затем освобождает music/network resources и существующие ресурсы.

### 7. Dependencies, PoC и packaging

В `CircleToSearch.csproj` добавить production references на проверенные версии:

- `NAudio` `3.0.1`;
- `MathNet.Numerics` `5.0.0`.

Сохранить `poc/**` исключённым из основного compile. После переноса не оставлять две расходящиеся
реализации алгоритма: превратить `poc/MusicRecognition.Poc` в тонкий diagnostic CLI, который
ссылается на production music module (через `ProjectReference`/`InternalsVisibleTo`), либо удалить из
PoC дубликаты после миграции их тестов. Сам CLI должен сохранить `--file` и loopback smoke-test для
ручной диагностики.

Проверить `build_release.ps1` output: `NAudio`, `MathNet.Numerics` и требуемые transitive assemblies
должны реально находиться в `bin/Release`, а плагин должен загружаться Flow Launcher на чистом reload.
Плагин не должен требовать установленный SongRec, Rust, Python, ffmpeg, API key или отдельный backend.

## Licensing и privacy — release blocker

Fingerprint/codec/client PoC адаптированы из SongRec и помечены `GPL-3.0-or-later`. Перед переносом в
распространяемый plugin владелец репозитория должен принять один из вариантов:

1. **Предпочтительное допущение плана:** распространять весь совместный plugin source/binary на
   условиях GPL-3.0-or-later, добавить полный `LICENSE`, сохранить copyright/SPDX headers и перенести
   `poc/MusicRecognition.Poc/NOTICE.md` в корневой third-party notice.
2. Если GPL для всего plugin неприемлема, остановить перенос GPL-derived файлов и сначала заменить
   fingerprint implementation на юридически совместимую clean-room/permissive альтернативу.

Бесплатность и открытый исходный код сами по себе не заменяют выполнение условий GPL. Это технический
release gate, а не юридическая консультация; перед публикацией проверить совместимость лицензий всех
распространяемых dependencies.

В README/первом релизе явно указать:

- endpoint неофициальный и может измениться/ограничить IP;
- на Shazam отправляется acoustic fingerprint, не raw WAV;
- raw capture хранится только в памяти и не записывается на диск production plugin;
- распознаётся звук default Windows output;
- функция требует интернет и может вернуть no match.

Не логировать raw PCM, signature data URI, полный response JSON или устойчивые request IDs. Для
диагностики достаточно attempt number, captured duration, RMS, peak count, HTTP status, elapsed time и
итоговый status.

## Изменения по файлам

- `CTS/Capture/SelectionOutcome.cs`: `OverlayAction`/`OverlayOutcome`, правила владения bitmap.
- `CTS/Capture/OverlayVisualFactory.cs`: action tray, отдельный `MusicButton`, обновлённый
  `OverlayVisual`, общие entrance/exit animations.
- `CTS/Capture/OverlayWindow.cs`: music click routing, `OverlayOutcome`, защита от lasso bubbling,
  frame disposal.
- `CTS/MusicRecognition/**`: production audio, fingerprint, Shazam client, throttle и progressive
  recognizer.
- `CTS/Search/SearchCoordinator.cs`: music branch/state/cancellation/presentation.
- `CTS/CompositionRoot.cs`: только здесь собрать music graph и Flow callbacks.
- `CTS/Ui/PluginPalette.cs`: только необходимые временные button colors/states.
- `CTS/Ui/UiStrings.cs`, `Languages/en.xaml` и остальные имеющиеся locale files: button tooltip,
  listening/result/no-match/no-audio/rate-limit/network/device strings, Open in Shazam.
- `CircleToSearch.csproj`: NAudio/MathNet references, license/notice content при необходимости.
- `poc/MusicRecognition.Poc/**`: оставить thin diagnostic harness без дублирования production logic.
- `tests/CircleToSearch.Tests/**`: progressive, throttle, Shazam parsing, coordinator, ownership и
  overlay interaction tests; обновить существующие coordinator/preview tests под новый outcome.
- `README.md`, `LICENSE`, `THIRD_PARTY_NOTICES.md` (или эквивалент): zero-config usage, privacy,
  unofficial endpoint и GPL attribution.

## Тесты

### Unit: progressive recognizer

- match на 4 с: ровно один snapshot/fingerprint/POST, capture остановлен раньше 12 с;
- no match на 4 с, match на 8 с: ровно два последовательных POST;
- три no match: точки 4/8/12, ровно три POST, итог `NoMatch`;
- request starts разделены минимум на 4 с даже при late/instant fake responses;
- пока request выполняется, fake capture продолжает накапливать данные; следующая подпись длиннее;
- silence/zero peaks пропускает POST и в конце возвращает `NoAudio`;
- cancellation во время delay, fingerprint и HTTP останавливает capture и запрещает поздний result;
- timeout/network/device failure не запускает следующую attempt;
- `429` прекращает текущую сессию, устанавливает cooldown и новая сессия во время cooldown делает
  ноль capture/POST;
- `Retry-After` используется, invalid/missing header даёт 60 секунд;
- capture/session dispose выполняется один раз на всех исходах.

Использовать fake monotonic clock/delay, fake capture snapshots и fake lookup; unit suite не должна
ждать реальные 4/8/12 секунд и не должна обращаться в сеть.

### Unit/STA: overlay и coordinator

- visual factory возвращает chip и music button как siblings общего tray; button не является
  descendant chip;
- button имеет localized tooltip/accessibility name и keyboard activation;
- click по music button возвращает `MusicRecognition`, не вызывает lasso и освобождает screenshot;
- обычный drag по screenshot по-прежнему возвращает visual selection;
- Escape/right-click/cancel возвращают null и освобождают screenshot;
- negative-monitor/high-DPI overlay test остаётся зелёным;
- music match/no-match/no-audio/rate-limit/cancel правильно мапятся на Flow callbacks;
- второй hotkey отменяет recognition; query/extra click не создаёт concurrent session;
- existing visual provider selection, bitmap disposal и error mappings не регрессируют.

Обновить `ChipPreviewTests`, чтобы PNG показывал отдельную кнопку рядом с chip для light/dark theme;
это только smoke/visual QA, не утверждение финального дизайна.

### Client/codec

- request содержит fingerprint URI, не raw audio;
- track/album/genre/cover/share URL parsing не регрессирует;
- response without `track` => typed no match;
- `429` и `Retry-After` => typed rate-limit;
- non-success/malformed JSON/cancellation имеют отдельные ожидаемые исходы;
- CRC/header и 4-секундный music-like fixture продолжают давать peaks.

### Команды проверки

Из корня репозитория:

```powershell
dotnet test .\tests\CircleToSearch.Tests\CircleToSearch.Tests.csproj -c Release
dotnet test .\poc\MusicRecognition.Poc.Tests\MusicRecognition.Poc.Tests.csproj -c Release
dotnet build .\CircleToSearch.csproj -c Release
powershell -ExecutionPolicy Bypass -File .\build_release.ps1 -NoPause
```

Зелёный результат засчитывается только если запустилось ожидаемое ненулевое число tests. После unit
suite выполнить ручные проверки в Flow Launcher:

1. Запустить известный трек и нажать music button: убедиться, что распространённый трек находится на
   первой точке и overlay click не рисует lasso.
2. Запустить менее очевидный/тихий фрагмент: проверить переход ко второй/третьей точке без
   параллельных POST.
3. Остановить весь system output: получить `NoAudio`, а не network request/error.
4. Нажать hotkey повторно во время listening: capture отменяется, позднее сообщение не появляется.
5. Сразу после этого выполнить обычный visual lasso через Google/Yandex и проверить прежний flow.
6. Reload plugin во время listening: не остаётся WASAPI capture, STA thread или undisposed client.
7. Проверить output `bin/Release` и запуск на системе без SongRec/Python/Rust/ffmpeg.

Live Shazam smoke-test делать вручную и редко; не включать его в default CI.

## Acceptance criteria

- Music button визуально расположен рядом с chip и является отдельным sibling control.
- Button работает мышью и клавиатурой и никогда не начинает lasso.
- Никаких ключей/настроек/дополнительных программ от пользователя не требуется.
- Known song может завершиться после первой 4-секундной attempt; capture не ждёт 12 секунд после
  match.
- На одну ручную сессию приходится максимум 3 непараллельных POST на точках 4/8/12 с минимум
  4 секундами между стартами.
- `429` прекращает повторы и включает локальный cooldown; transport failure не маскируется как no
  match.
- Production не сохраняет raw audio на диск и отправляет только fingerprint.
- Cancel/dispose не оставляет capture, HTTP task, overlay STA dispatcher или session semaphore в
  активном состоянии.
- Все новые visible strings локализованы, все fixed colors находятся в `PluginPalette`.
- `Main.cs` остаётся thin adapter, production graph собирается только в `CompositionRoot`.
- Existing visual search behavior и полный ожидаемый набор тестов остаются зелёными.
- До публичного распространения выполнен выбранный licensing path и добавлены attribution/privacy
  notices.

## Допущения и открытые вопросы

- План предполагает согласие владельца проекта распространять объединённый plugin под
  GPL-3.0-or-later. Без этого integration блокируется на замене fingerprint implementation.
- Для первой версии результат показывается встроенными сообщениями Flow Launcher; отдельный красивый
  listening/result UI сознательно отложен.
- Policy `[4, 8, 12]`, min interval 4 с, fallback cooldown 60 с и request timeout около 8 с считаются
  безопасными начальными значениями. После ручного теста их можно ужесточить, но нельзя делать более
  агрессивными без наблюдений по `429`.
- Default output может измениться/исчезнуть во время capture; первая версия завершает текущую сессию
  понятной device error и не пытается автоматически переключаться.
- Shazam endpoint не гарантирован публичным контрактом. Его изменение должно приводить к contained
  user-facing service error, а не к падению Flow Launcher.
