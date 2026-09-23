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
- Use Material-style outlined SVG icons: `viewBox="0 0 24 24"`, `fill="none"`, `stroke="currentColor"`, `stroke-width="1.65"`, round line caps and joins; size 20px (21px in settings rows). No filled icons; Material Symbols weight/grade axes do not apply to these custom SVGs.
- Put all user-visible strings in `Languages/*.xaml`; do not hardcode them in C#.
- Do not use em dashes (U+2014); use hyphens (-) instead.

## Tests

- While iterating, run the fast set: `dotnet test tests/CircleToSearch.Tests --filter "Category!=Slow"` (about 15 seconds).
- Also run the slow tests of the feature you changed, e.g. `--filter "FullyQualifiedName~SettingsPreviewTests"`. Run the full suite once, before finishing the task, not after every edit.
- Mark a test class `[Trait("Category", "Slow")]` when its tests together take a second or more (real-time waits, WPF windows, child processes).
- In UI tests, wait for a condition, not a fixed duration; to check an animation mid-way, seek its clock instead of sleeping.
