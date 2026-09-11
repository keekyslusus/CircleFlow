# Repository Guidelines

- Keep `Main.cs` as a thin Flow Launcher adapter.
- Assemble the runtime dependency graph only in `CTS/CompositionRoot.cs`.
- Prefer composition and constructor injection. Do not introduce implementation inheritance, abstract base classes, class hierarchies or service locators.
- Give each class one cohesive responsibility; keep feature-specific behavior and resource ownership inside that feature.
- Keep features loosely coupled: replacing or removing one should mainly affect its own code and composition, not unrelated features.
- Centralize shared behavior and policy in focused services (e.g. copying and its feedback in `ClipboardCopyService`); avoid duplication and abstractions that do not reduce coupling.
- No XML docs (`/// <summary>`) and no comments that restate the code; comment only non-obvious why.
- Use `CTS/Ui/PluginPalette.cs` for all fixed UI colors; do not hardcode colors elsewhere.
- Put all user-visible strings in `Languages/*.xaml`; do not hardcode them in C#.
