# DRS Unreal Tools

Small Windows tools for working with Unreal Engine projects.

| Tool | What it does |
|---|---|
| [UE Test Tool](Tools/UETestTool/README.md) | Launch any Unreal project on your machine as Standalone, Listen Server, Dedicated Server or Client, with per-project custom configurations. |

## Download

Get the latest `UETestTool.exe` from [Releases](../../releases). It's a single, self-contained exe: no installer, and no .NET runtime to install.

## Repository layout

```
Shared/Drs.Unreal.Common/     Engine, project and map discovery; .ini handling; launch command building.
                              Reusable by future tools.
Tools/UETestTool/             The UE Test Tool app (WPF, .NET 10).
Tests/Drs.Unreal.Common.Tests/
docs/images/                  Screenshots used by the READMEs.
```

## Building

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download) on Windows.

```powershell
dotnet test                                                          # build + run the unit tests
dotnet publish Tools/UETestTool -c Release -o publish/UETestTool     # single-file UETestTool.exe
```

Pushing a tag such as `v0.1.0` makes the GitHub Actions workflow build the exe and attach it to a GitHub Release.

## License

[MIT](LICENSE)
