- README WIP

# CircleFlow

<!-- What CircleFlow does, with screenshots. -->

## Install

Download `CircleFlow-win-Setup.exe` from the [latest release](https://github.com/keekyslusus/CircleFlow/releases/latest) and run it. It needs no administrator rights and installs everything into one folder, `%LocalAppData%\CircleFlow`. The installer is not code-signed yet, so Windows SmartScreen may ask you to confirm with **More info → Run anyway**.

Prefer no installer? `CircleFlow-win-Portable.zip` is the same application for a folder of your choice. Keep all its files together; the folder must be writable.

### Requirements

- Windows 10 version 2004 or later, x64. The .NET runtime is included.
- [Microsoft Edge WebView2 Runtime](https://developer.microsoft.com/en-us/microsoft-edge/webview2/#download) for the built-in browser. Windows 11 already has it.
- Text recognition uses the OCR languages installed in Windows.
- Search, translation and music recognition need an internet connection.

## Updates

CircleFlow checks for a new version shortly after it starts and then once a day. When one is out, a notification offers **Update**: CircleFlow downloads only what changed, restarts, and you are on the new version. Your settings are kept.

## Your data

Settings, logs and the built-in browser's profiles are stored in the `Data` folder of your installation (`%LocalAppData%\CircleFlow\Data`, or inside the portable folder). Logs in `Data\Logs` help when reporting a problem.

**Run at Windows startup** in Settings is off until you turn it on. Disabling CircleFlow in Task Manager's Startup apps shows as off in Settings too. If you use the portable version, turn it off before moving or deleting the folder.

To uninstall, use **Settings → Apps → Installed apps → CircleFlow → Uninstall**. This removes the whole folder, including `Data`.

## Contributing

Building, testing and releasing are described in [CONTRIBUTING.md](./CONTRIBUTING.md).

## License

GPL-3.0-or-later. See [LICENSE](./LICENSE), [THIRD_PARTY_NOTICES.txt](./THIRD_PARTY_NOTICES.txt) and [THIRD_PARTY_LICENSES](./THIRD_PARTY_LICENSES).
