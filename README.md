# Northstar Explorer

> **Note:** This project is a work in progress and largely an experiment to see how far I can push vibe coding with Codex.

Northstar is a macOS-focused file explorer built with Avalonia UI, inspired by Windows Explorer interactions.

![Northstar Screenshot](https://raw.githubusercontent.com/brooklynDev/Northstar/1bce5bc/docs/screenshot.png)

## Releases

Download prebuilt macOS app bundles from GitHub Releases:

- https://github.com/brooklynDev/Northstar/releases

On macOS, before first run, remove quarantine attributes:

```bash
xattr -dr com.apple.quarantine "Northstar Explorer.app"
```

## About This App

Northstar is a macOS-first file explorer prototype focused on Windows Explorer-style workflows, built as a fast-moving vibe-coding experiment with Codex. The goal is to explore how far this approach can go while still producing a polished, useful desktop tool: tabbed browsing, keyboard-first navigation, quick previews, built-in terminal workflows, and a themed UI that feels native on Mac.

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
