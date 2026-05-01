# Guida Scripting

Guida Scripting is an embeddable .NET scripting runtime and host API framework.

It provides the reusable core for applications that want to run scripts, track script tasks, expose host capabilities, and describe scripting APIs for editor tooling or documentation.

The core package is intentionally host-neutral. Applications provide concrete integrations such as browser automation, desktop UI, storage, network access, and workflow services through host-specific adapters.

## Included Scope

- Script execution runtime contracts.
- Task lifecycle and cancellation behavior.
- Language engine integration points.
- Host capability abstractions.
- Scripting API metadata and generated guidance.
- Test and sample host infrastructure as the public surface matures.

## Not Included

- WebView2 host implementation.
- Browser tab management.
- DOM/page automation implementation.
- Web scraping or extraction implementation.
- Network capture or request interception implementation.
- Screenshots, OCR, VLM, Whisper, TTS, or native model runtime implementation.
- WPF, AvalonDock, AvalonEdit, command palette, panes, or desktop UI behavior.
- Product-specific assets, docs, manifests, bundled model files, or closed API surfaces.

## Status

This repository is in an early alpha phase. APIs are unstable until a future `1.0` release.

## Build

```powershell
dotnet restore Guida.Scripting.sln -m:1
dotnet build Guida.Scripting.sln -c Debug --no-restore -m:1
dotnet test Guida.Scripting.sln -c Debug --no-build -m:1
dotnet test Guida.Scripting.sln -c Debug --no-build -m:1 --collect:"XPlat Code Coverage"
```

## License

This project is licensed under Apache-2.0. See [LICENSE](LICENSE).
