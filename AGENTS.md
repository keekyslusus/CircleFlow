# Repository Guidelines

- Keep `Program.cs` as a thin application entry point.
- Assemble the runtime dependency graph only in `CTS/CompositionRoot.cs`.
- Prefer composition and constructor injection. Do not introduce implementation inheritance, abstract base classes, class hierarchies or service locators.
- Give each class one cohesive responsibility; keep feature-specific behavior and resource ownership inside that feature.
- Keep features loosely coupled: replacing or removing one should mainly affect its own code and composition, not unrelated features.
- Centralize shared behavior and policy in focused services (e.g. copying and its feedback in `ClipboardCopyService`); avoid duplication and abstractions that do not reduce coupling.
- Apply these principles(1. one cohesive responsibility, 2.Keep features loosely coupled, 3. Centralize shared behavior) proportionately: prefer the simplest design that makes current changes clear; do not add layers or interfaces solely for hypothetical future replacements.
- No XML docs (`/// <summary>`) and no comments that restate the code; comment only non-obvious why.
- Before adding an icon, check `CTS/Ui/PluginIcons.cs` and reuse existing geometry when available.
- Use `CTS/Ui/PluginPalette.cs` for all fixed UI colors; do not hardcode colors elsewhere.
  Theme colors come from `ColorRoles` (`Surface`, `OnSurface`, `Primary`, `Outline`, `Dock`, ...): build component palettes from roles instead of repeating values, and read a role directly (e.g. `Roles.Primary`) rather than borrowing another feature's component palette. Add a literal only for brand colors or values that are genuinely specific to one component.
- Take the font family and font sizes from `CTS/Ui/PluginTypography.cs` (`Caption` 12, `Body` 14, `Subtitle` 16, `Title` 20, `Display` 28); do not hardcode font names or sizes elsewhere.
- Icons use the Material outlined style: no solid filled areas. Draw new icons as SVG strokes: `viewBox="0 0 24 24"`, `fill="none"`, `stroke="currentColor"`, `stroke-width="1.65"`, round line caps and joins; size 20px (21px in settings rows). Material Symbols weight/grade axes do not apply to these custom SVGs.
  - Line glyphs without enclosed areas (arrows, close, check, translate) look the same filled or outlined, so the existing fill-rendered paths in `PluginIcons` may be reused. There, the `Filled` suffix means the path is rendered with a fill, not the filled icon style.
  - Allowed solid icons: glyphs that are solid even in Material Symbols Outlined (e.g. the `arrow_drop_down` caret), icons matching Android Circle to Search for recognizability (e.g. the music note), and brand marks (search providers, AniList, Pinterest), which keep their original artwork.
- Put all user-visible strings in `Languages/*.xaml`; do not hardcode them in C#.
- Do not use em dashes (U+2014); use hyphens (-) instead.

## Tests

- While iterating, run the tests of the feature you changed, e.g. `--filter "FullyQualifiedName~SettingsPreviewTests"`, and the fast set: `dotnet test tests/CircleToSearch.Tests --filter "Category!=Slow"` (about 15 seconds).
- Run the full suite once, before finishing the task, not after every edit. Do not run the fast set right before it: the full suite already includes it.
- If a running `CircleFlow.exe` locks the build output, stop it (`Stop-Process -Name CircleFlow`) and rebuild; do not switch to another configuration to work around the lock.
- Add `--no-build` when the code has not changed since the last build, e.g. when rerunning or narrowing a filter.
- If the full suite fails in tests unrelated to your change, rerun only those tests (`--no-build --filter`), not the full suite. If they pass alone, report them as flaky, with the failure message.
- Mark a test class `[Trait("Category", "Slow")]` when its tests together take a second or more (real-time waits, WPF windows, child processes).
- In UI tests, wait for a condition, not a fixed duration. Drive WPF animations with `ManualAnimationClock` (`time.Advance`) instead of waiting on real time.
