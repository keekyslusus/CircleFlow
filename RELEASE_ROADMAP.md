# Circle to Search — roadmap релиза

## Цель

Подготовить текущий плагин с Google Lens через WebView2 к публичному релизу, не встраивая
Fixed Version Runtime размером 250+ МБ. Первый запуск должен быть понятным, выделение области —
приятным, окно Lens — аккуратным, а глобальная горячая клавиша — настраиваемой без ручного ввода
строки вида `Ctrl+Alt+Space`.

## Milestone 0 — Определиться с названием (release blocker)
- потом поменять везде где только можно в user visible strings
- сделать иконку

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


## Milestone 2 — новый selection overlay (release blocker) — готово

- ✅ после лассо показывается прямоугольник того, что выделится и отправится в провайдер
  (как circle to search в android): рамка системного акцента со свечением, затемнение
  плавно «втягивается» из лассо в прямоугольник, кадр держится ~450 мс до ухода в провайдер
- ✅ светлый полупрозрачный фон внутри lasso с размытыми границами/контурами (BlurEffect,
  отключается на софтверном рендере)
- ✅ для lasso используется цвет акцента системы
- ✅ вступительная анимация как в circle to search (OverlayEntrance): равномерный
  полупрозрачный акцентный тинт мягко проявляется и тает, вдоль волны от точки курсора
  мерцают частицы; ~1.3 с, слой самоудаляется; отключается при выключенных анимациях


## Milestone 3 — polish окна WebView2 (желательно; не blocker, если рискованно)

- почти готово, остались мелочи: поставить иконку в окне мб еще чтото
- сделать скролл тонким,если можно
- если есть экран загрузки(google lens) написать туда подсказки типа: Control+W чтобы закрыть окно,можно отвечать gemini без входа в аккаунт google


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


## Milestone 6 - допилить плагин в самом flow launcher при вызовые через keyword
- сейчас при вызове через keyword,ничего нет,нужно сделать блок который открывает окно выделения при напечатанном keyword