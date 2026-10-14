# Tents

> An open-source alternative for Desktop Organization.

Tents is a lightweight Windows app that keeps your desktop tidy. Group your desktop icons into **tents**: labeled, resizable containers that sit on the desktop behind your windows. Name them, move them around, and roll them up when you need the space. Tents runs quietly in the background and uses almost no CPU or memory.

**Status:** Planning. There's nothing to install yet. Watch the repo or check the roadmap below to follow progress.

## Why Tents?

- **A tidy desktop.** Put related icons together in tents you can name, resize and roll up.
- **Feather-light.** Low resource use is a core requirement, not an afterthought. Tents uses effectively no CPU while idle (no timers, no polling) and as little memory as possible.
- **Your files stay put.** Tents never moves, renames or deletes files unless you tell it to. Everything on your desktop stays where it is on disk.
- **Feels native.** It follows Windows light and dark themes, scales properly on every monitor, and supports the usual right-click menus and drag and drop.
- **Open source.** MIT licensed and open to contributions.

## Planned features

| Feature | Milestone |
|---|---|
| Create, move, resize and title tents; drag icons in and out; double-click to open; native right-click menus | M1 |
| Rollup: click a tent's title to collapse it to the title bar | M1 |
| Layout is saved and restored after restarts | M1 |
| Start with Windows, tray icon | M1 |
| Quick-hide: double-click empty desktop to hide or show all tents | M2 |
| Tent appearance: color, opacity, blur, light and dark mode | M2 |
| Multi-monitor and mixed-DPI layouts | M2 |
| Named layout snapshots, settings UI | M2 |
| Folder portals: a tent that shows the contents of any folder | M3 |
| Rules that automatically sort new desktop items into tents | M4 |
| Desktop pages, search and filtering inside a tent, icon tinting | M5 |
| Installer, winget package, auto-update | M6 |

## Roadmap

- **M0, Foundations:** solution skeleton, CI, contributing guidelines, and three technical experiments: pinning tents to the desktop, hiding native desktop icons that are already inside tents, and measuring the performance baseline.
- **M1, MVP:** core tents, rollup, saved layout, start with Windows and a tray icon. Released as a portable zip.
- **M2, Polish:** quick-hide, tent appearance, multi-monitor and DPI support, snapshots, settings.
- **M3, Folder portals.**
- **M4, Rules and auto-sort.**
- **M5, Desktop pages and extras.**
- **M6, Distribution:** installer, winget and auto-update.

## Built with

- C# on .NET 10 (LTS) with WPF
- [CsWin32](https://github.com/microsoft/CsWin32) for Win32 and shell interop
- xUnit and BenchmarkDotNet

Targets Windows 10 22H2 and Windows 11.

## Contributing

Contributions are welcome once M0 lands. A `CONTRIBUTING.md` with build instructions, code style and the project's clean-room rules will be added then. Tents is an independent, clean-room project, so contributions must be original work.

## License

MIT. The `LICENSE` file will be added in M0.
