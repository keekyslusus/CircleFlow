# Repository Guidelines

- Keep `Program.cs` as a thin standalone application entry point.
- Assemble the runtime dependency graph only in `CTS/CompositionRoot.cs`.
- Prefer composition and constructor injection. Do not introduce implementation inheritance, abstract base classes, class hierarchies or service locators.
- Give each class one cohesive responsibility; keep feature-specific behavior and resource ownership inside that feature.
- Keep features loosely coupled: replacing or removing one should mainly affect its own code and composition, not unrelated features.
- Centralize shared behavior and policy in focused services (e.g. copying and its feedback in `ClipboardCopyService`); avoid duplication and abstractions that do not reduce coupling.
- Apply these principles(1. one cohesive responsibility, 2.Keep features loosely coupled, 3. Centralize shared behavior) proportionately: prefer the simplest design that makes current changes clear; do not add layers or interfaces solely for hypothetical future replacements.
- No XML docs (`/// <summary>`) and no comments that restate the code; comment only non-obvious why.
- Use `CTS/Ui/PluginPalette.cs` for all fixed UI colors; do not hardcode colors elsewhere.
- Put all user-visible strings in `Languages/*.xaml`; do not hardcode them in C#.
- Do not use em dashes (U+2014); use hyphens (-) instead.
