# DRS Unreal Tools

Small Windows tools for working with Unreal Engine projects.

| Tool | What it does |
|---|---|
| [UE Test Tool](Tools/UETestTool/README.md) | Launch any Unreal project on your machine as Dedicated Server, Listen Server, Client or Standalone, with per-project custom configurations. |

## Download

Get the latest `UETestTool.exe` from [Releases](../../releases). It's a single, self-contained exe: no installer, and no .NET runtime to install.

## Repository layout

```
Shared/Drs.Unreal.Common/     Engine, project and map discovery; .ini handling; launch command building.
                              Reusable by future tools.
Tools/UETestTool/             The UE Test Tool app (WPF, .NET 10).
Tests/Drs.Unreal.Common.Tests/
docs/images/                  Screenshots used by the READMEs.
scripts/release.ps1           Tags a release, waits for it to build and installs it.
```

## Building

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download) on Windows.

```powershell
dotnet test                                                          # build + run the unit tests
dotnet publish Tools/UETestTool -c Release -o publish/UETestTool     # single-file UETestTool.exe
```

The version comes from git tags ([MinVer](https://github.com/adamralph/minver)): a build of the commit tagged `v0.3.0` is version 0.3.0, and later commits build as pre-releases such as `0.3.1-alpha.0.2`. The app shows its version in the title bar.

## Releasing

1. Merge your changes into `main` through a PR, and let its CI build pass.
2. On an up-to-date `main`, run:

   ```powershell
   powershell -ExecutionPolicy Bypass -File scripts\release.ps1 0.3.0
   ```

   The script checks the repo and main's CI build, then tags and pushes `v0.3.0`. GitHub Actions tests, builds and publishes the release with `UETestTool.exe` attached. The script waits for that build, then installs the exe into `%LOCALAPPDATA%\Programs\UE Test Tool`, adding a Start menu shortcut if needed. Add `-DryRun` to run only the checks, or `-NoInstall` to skip installing.

With Claude Code, just say **"Publish 0.3.0"**. The project skill in `.claude/skills/publish-release` commits and merges any pending work through a PR, then runs the script.

## License

[MIT](LICENSE)
