# CircleFlow: Screen OCR, Text Selection, and In-Place Translation Plan

## 1. Executive Summary & Objective

The objective of this feature is to replicate the seamless text capabilities of **Android's Circle to Search** on Windows:
1. **Zero-Mode Text Interaction (No separate "copy text" chip):** When the overlay opens, users can directly hover over text on the screen, see the cursor change to a text cursor (`I-Beam`), and drag to select/copy words just like in a browser or document.
2. **Contextual Action Toolbar & Instant Copy:** Selecting text displays a floating pill with `[Copy]`, `[Search]`, and `[Translate]`. Pressing `Ctrl+C` immediately copies the selected text and displays a toast notification.
3. **Full-Screen In-Place Translation:** A dedicated `[Translate]` chip in the bottom dock translates all detected text blocks on screen and replaces them in-place with styled overlays matching the original font and background color.

---

## 2. Architecture & Repository Guidelines Compliance

Any executing agent **must strictly observe** the repository rules from `AGENTS.md`:
* **Thin Adapter:** Keep `Main.cs` strictly as a thin Flow Launcher adapter. Do not touch it for this feature.
* **Composition Root:** Assemble all new dependencies, services, and controllers **only** in `CTS/CompositionRoot.cs`.
* **No Class Hierarchies:** Prefer composition and constructor injection. Do not introduce abstract base classes, inheritance trees, or service locators.
* **Comments:** No XML docs (`/// <summary>`) and no comments that restate the code. Comment only non-obvious *why*.
* **Colors:** All fixed/theme colors must reside in `CTS/Ui/PluginPalette.cs`. Do not hardcode hex/RGB values.
* **Localization:** Put all user-visible strings in `Languages/*.xaml` (ensure both `en.xaml` and `ru.xaml` are populated).
* **Dispatcher Crash Invariants:** Do not leave unhandled exceptions in UI/dispatcher callbacks. Any abort must cleanly complete the command channel and close sessions without hanging Flow Launcher (see `RELEASE_ROADMAP.md`).

---

## 3. Architecture & New Components

```
CTS/
├── Ocr/
│   ├── IOcrService.cs               // Interface for OCR processing
│   ├── WindowsMediaOcrService.cs    // WinRT Windows.Media.Ocr.OcrEngine implementation
│   ├── OcrWordSnapshot.cs           // Immutable word record: text, DipRect, PixelRect
│   ├── OcrLineSnapshot.cs           // Immutable line record grouping words
│   └── OcrScreenSnapshot.cs         // Complete snapshot of words, spatial index, and raw text
├── Translation/
│   ├── ITranslationService.cs       // Translation interface
│   ├── GoogleFreeTranslateClient.cs // HTTP client for Google Translate free endpoint
│   └── TranslationBlock.cs          // Original text block -> Translated text with bounds & style
├── Capture/
│   ├── OverlayInteractions/
│   │   ├── TextOverlayController.cs // Manages hover detection, selection range, and toolbar
│   │   └── TranslationOverlayController.cs // Manages full-screen translation state & toggle
│   ├── OverlayVisuals/
│   │   ├── TextSelectionVisual.cs   // Highlight shapes over selected words
│   │   ├── FloatingTextToolbarVisual.cs // Mini floating pill for Copy / Search / Translate
│   │   └── TranslationOverlayVisual.cs  // In-place translated text cards over the screen
│   └── SelectionGestureKind.cs      // Add TextSelection to enum
```

---

## 4. Phase-by-Phase Implementation Steps

### Phase 1: Windows Built-In OCR Service (`CTS/Ocr/`)

1. **Framework & Dependencies:**
   * In `CircleFlow.csproj`, verify Windows 10/11 WinRT API access:
     Target is already `net9.0-windows`. If WinRT projection is needed, add reference or use `<TargetPlatformVersion>10.0.19041.0</TargetPlatformVersion>`.
2. **`WindowsMediaOcrService.cs`:**
   * Use `Windows.Media.Ocr.OcrEngine`.
   * Initialize using `OcrEngine.TryCreateFromUserProfileLanguages()` with fallback to `OcrEngine.AvailableRecognizerLanguages.FirstOrDefault()`.
   * Method `Task<OcrScreenSnapshot> RecognizeAsync(Bitmap frame, double scale, CancellationToken ct)`.
   * Convert `System.Drawing.Bitmap` to `Windows.Graphics.Imaging.SoftwareBitmap` (or memory stream into `BitmapDecoder`).
   * Map `OcrWord.BoundingRect` (physical pixels) to DIPs:
     $$\text{DipX} = \frac{\text{PixelX}}{\text{scale}}, \quad \text{DipY} = \frac{\text{PixelY}}{\text{scale}}$$
   * Build an immutable `OcrScreenSnapshot` containing a list of `OcrLineSnapshot` and `OcrWordSnapshot`, along with a spatial bounding box lookup.

### Phase 2: Background Zero-Latency OCR Pipeline

1. **Asynchronous Triggering:**
   * In `CTS/Capture/OverlaySession.cs` or `OverlaySessionWorkflow.cs`, kick off `RecognizeAsync` immediately when the screen frame is captured by `PointerMonitorCapture`:
     ```csharp
     var ocrTask = Task.Run(() => ocrService.RecognizeAsync(frameCopy, scale, ct), ct);
     ```
   * While the entrance ripple animation plays (~180–220 ms), OCR completes in the background thread without blocking the WPF dispatcher.
2. **Transferring Snapshot to Overlay:**
   * When `ocrTask` completes, deliver `OcrScreenSnapshot` into `OverlayWindow` via dispatcher message or callback.

### Phase 3: Cursor-Chameleon & In-Place Text Selection

1. **Hit-Testing & Cursor Switching:**
   * In `OverlayWindow.cs` / `TextOverlayController.cs`:
     * On `PreviewMouseMove` (when not actively drawing a lasso or selection):
       * Check if pointer position intersects any `OcrWordSnapshot.DipRect`.
       * If over text: change window cursor to `Cursors.IBeam`.
       * If off text: restore `Cursors.Arrow` (or crosshair).
2. **Differentiating Text Drag vs. Image Lasso:**
   * If `PreviewMouseLeftButtonDown` occurs over a text word:
     * Mode transitions to `OverlayInteractionMode.TextSelection`.
     * Do **not** draw the visual search lasso or dim cutouts.
     * On mouse drag, expand the text selection range: highlight all words between start index and current index.
   * If `PreviewMouseLeftButtonDown` occurs on empty screen or an image without text:
     * Retain current behavior: lasso/rectangle visual search or pixel color pick.
3. **Lasso Around Text Fallback:**
   * If the user circles a region with the lasso, and that region contains text lines:
     * In addition to visual search, the action tray can display a secondary option: `[📋 Copy N words]`.

### Phase 4: Floating Action Toolbar & Shortcuts

1. **`FloatingTextToolbarVisual.cs`:**
   * When mouse drag completes with one or more words selected:
     * Calculate bounding box enclosing the selected text.
     * Position a compact floating pill (CornerRadius 16, drop shadow) 8px above (or below) the selection.
     * Buttons:
       * 📋 **Копировать / Copy** (`Ctrl+C`)
       * 🔍 **Искать / Search** (executes visual/text query)
       * 🌐 **Перевести / Translate** (translates only this selection)
2. **Keyboard Accelerators:**
   * In `OverlayWindow.PreviewKeyDown`:
     * If text is selected and user presses `Ctrl+C`:
       * Copy selected text to clipboard.
       * Emit `ToastNotification("Copied to clipboard", ToastTone.Success)`.
       * Dismiss selection.

### Phase 5: Translation Engine & In-Place Full-Screen Overlay

1. **`GoogleFreeTranslateClient.cs`:**
   * Implements `ITranslationService`.
   * Endpoint: `https://translate.googleapis.com/translate_a/single?client=gtx&sl=auto&tl={targetLang}&dt=t&q={encodedText}`.
   * Batch requests or group consecutive lines into paragraphs to minimize HTTP calls.
   * Auto-detect target language from system UI locale (e.g. `ru` or `en`).
2. **Bottom Dock Translation Chip:**
   * In `ActionTrayVisualFactory.cs`, add a translate button next to the music button:
     * Icon: `[A ⇄ 文]` / Translate symbol.
     * Tooltip: `Translate screen`.
3. **In-Place Card Replacement:**
   * When user clicks Translate:
     * If OCR is still completing, show a pulsing loading state on the button.
     * When ready, for each text paragraph/line:
       * Sample the dominant background color under the bounding box from `GdiBitmap _frame`.
       * Create a `Border` with that background color, CornerRadius 4, slightly larger than the original text bounds (to completely mask the original text).
       * Render the translated text with matching font size, weight, and contrasting color.
       * Animate fade-in (180 ms).
     * Add a floating toggle pill at the top/dock: `[Show Original / Show Translation]`.

### Phase 6: Design, Palette & Localization

1. **Colors in `PluginPalette.cs`:**
   * Add text selection highlight color: `Color.FromArgb(0x66, accent.R, accent.G, accent.B)` (semi-transparent accent).
   * Add translation backdrop colors and toolbar palette entries.
2. **Strings in `Languages/en.xaml` and `Languages/ru.xaml`:**
   * `plugin_circletosearch_translate_action`: "Translate" / "Перевести"
   * `plugin_circletosearch_copy_text_action`: "Copy text" / "Копировать текст"
   * `plugin_circletosearch_text_copied`: "Text copied to clipboard" / "Текст скопирован в буфер"
   * `plugin_circletosearch_translating`: "Translating screen…" / "Перевод экрана…"
   * `plugin_circletosearch_translation_failed`: "Translation failed" / "Не удалось перевести"
   * `plugin_circletosearch_show_original`: "Show original" / "Показать оригинал"

---

## 5. Potential Pitfalls & Mitigation Strategies

| Risk / Pitfall | Impact | Solution |
| :--- | :--- | :--- |
| **DPI Mismatch** | Text selection offset from visual letters | Always divide WinRT OCR physical bounds by `PointerMonitorCaptureResult.Scale`. |
| **OCR Delay on 4K Screens** | UI lag if run on dispatcher | Never call `OcrEngine` on WPF STA dispatcher. Run exclusively on `Task.Run()` during entrance animation. |
| **Missing Windows OCR Language** | `OcrEngine` throws or returns null | Check `OcrEngine.IsLanguageSupported()`. If language pack missing, gracefully disable I-Beam and display error toast on translate click. |
| **Background Color Sampling** | In-place translated box looks like an ugly white rectangle on dark image | Sample 4 corner pixels and center pixel of text bounding box to compute median/average color for mask. |
| **WPF Adorner / Visual Tree Leak** | Memory leak or dispatcher crash on close | Ensure all selection borders and floating toolbars are cleaned up in `OverlayWindow.Closed` and controller `Dispose()`. |

---

## 6. Verification & Testing Plan

1. **Unit Tests (`CircleToSearch.Tests`):**
   * `OcrSpatialIndexTests`: Test hit-testing bounding boxes for words at arbitrary coordinates.
   * `GoogleFreeTranslateClientTests`: Test parsing of JSON response array from translation endpoint.
   * `TextSelectionRangeTests`: Test range selection across multiple lines and words.
2. **UI / Integration Tests:**
   * Test STA dispatcher lifecycle when opening and closing overlay with active OCR task.
   * Verify `Ctrl+C` does not crash if clipboard is locked by another process (use retry/catch).
3. **Manual Smoke Testing:**
   * Open overlay over a browser with code and text: verify cursor turns to `I-Beam`.
   * Drag to select a sentence: verify highlight and floating toolbar appearance.
   * Press `Ctrl+C`: verify toast and paste into Notepad.
   * Click `[Translate]` in bottom dock: verify text is replaced in-place by Russian translation.
