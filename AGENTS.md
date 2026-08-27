# Repository Guidelines

- Keep `Main.cs` as a thin Flow Launcher adapter.
- Assemble the runtime dependency graph only in `CTS/CompositionRoot.cs`.
- Prefer composition and constructor injection. Do not introduce implementation inheritance, abstract base classes, class hierarchies or service locators.
- No XML docs (`/// <summary>`) and no comments that restate the code; comment only non-obvious why.