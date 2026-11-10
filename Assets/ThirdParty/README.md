# Third-party dependencies

Pinned upstream libraries are isolated from game code. Each retains its upstream notices.

- DOTween 1.3.045: unmodified free runtime DLL + original readme; no Pro dependency. https://dotween.demigiant.com/download.php
- xLua 2.1.16_android_16kb: Lua 5.3, Windows x64 / Android ARM64 native libraries; upstream Src/Editor/Resources. Unity assembly definitions are local integration files. No C# hotfix enabled.
- Google.Protobuf 3.33.5: official NuGet netstandard2.0 runtime.
- Noto Sans SC: SIL Open Font License, source and original license retained.
- YooAsset 2.3.19 and MCP for Unity 10.3.0 are embedded Unity packages under Packages. MCP runtime helpers are Editor-only in this project.

Dependency provenance and archive checksums are in Tools/dependencies.lock.json. Do not update automatically. Only import resources for implemented modules.
