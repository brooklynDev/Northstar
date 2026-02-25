# Northstar Explorer

Northstar is a macOS-focused file explorer built with Avalonia UI, inspired by Windows Explorer interactions.

![Northstar Screenshot](docs/screenshot.png)

## What It Does

- Dark, desktop-style file browser UI
- Quick access sidebar + folder listing
- Breadcrumb/path bar (clickable, editable)
- Sortable columns: Name, Type, Size, Modified
- Explorer-style keyboard behavior:
  - Type-to-select
  - `Enter` to open
  - `Backspace` to go back
- Context menu + shortcuts (`Cmd/Ctrl + C/X/V`, `Delete`, `Cmd/Ctrl + Shift + N`)
- Auto-refresh when files change in the current folder
- Open current folder in external terminal app

## Requirements

- macOS
- .NET SDK 10

## Quick Start

```bash
make run
```

## Build

```bash
make build
```

## Publish (macOS app bundles)

```bash
make publish
```

Artifacts are written to:

- `artifacts/osx-arm64/Northstar Explorer.zip`
- `artifacts/osx-x64/Northstar Explorer.zip`

Each zip unpacks to a single `.app` bundle.
