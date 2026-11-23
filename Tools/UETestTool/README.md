# UE Test Tool

Launch Unreal Engine projects for testing (Dedicated Server, Listen Server, Client, Standalone, or your own configurations) without typing command lines.

![UE Test Tool](../../docs/images/UETestTool.png)

It follows the Windows light/dark mode and accent colour. It doesn't need to live in a project folder: run `UETestTool.exe` from anywhere.

## Using it

1. **Project**: pick one of your Unreal projects. Use **Folders…** to choose where the tool looks, e.g. your repos folder and your Unreal Projects folder. Projects in those folders, and up to three levels below them, are found on every search. Until you add a folder, the tool shows an error and finds nothing.
   - Use **Browse…** to add a project from anywhere else.
   - Removing a folder removes its projects from the list. Projects added with Browse… stay.
   - Right-click a project to remove it from the list.
   - Projects on a disconnected drive are greyed out until it's back.
2. **Engine**: every installed engine, both launcher installs and registered source builds. Engines are compared by release (e.g. 5.8), so a project made with a 5.8 source build is fine on the 5.8 launcher install, and vice versa. If the selected engine is a different release, a warning appears, with a **Switch** button when an engine of the project's release is installed.
   - A project associated with a source build that isn't registered on this machine (e.g. after re-registering your build) is checked against the engine version recorded in its most recent log, in `Saved/Logs`. If there's no log, the warning says the project's engine version is unknown.
3. **Map**: the maps in the project's `Content` folder and in its content plugins. With the list focused, type the start of a map's name to jump to it.
4. **Configuration**: choose how to launch. Add your own extra arguments to any configuration in the right-hand box; they're remembered per configuration, just for you.
5. **Launch**, or double-click a configuration.

**Show command** reveals the full command line, including the project and map that are added automatically.

**Refresh** searches again for engines, projects and maps, and re-reads the project's config files (e.g. after a `git pull`).

### Built-in configurations

| Configuration | Launches as |
|---|---|
| Dedicated Server | `<map> -server -log` |
| Listen Server | `<map>?listen -game -log` |
| Client | `127.0.0.1 -game -log` (no map: connects to a server on this machine) |
| Standalone | `<map> -game -log` |

The editor executable and the `.uproject` path are always added in front.

## Adding configurations for a project

Use the **New** row at the bottom of the window:

- **Name**: what to call it, e.g. `Client (noHMD)`.
- **Template**: an existing configuration to base it on. The new configuration launches the same way (map, `?listen` or connect address) and starts with the template's arguments.
- **Arguments**: edit or extend them, e.g. add `-noHMD -WINDOWED -ResX=1200 -ResY=800`.
- Click **Add**.

Use ✎ on a configuration to edit it (or to copy it under a new name), and 🗑 to delete it. Saving under a built-in's name overrides that built-in for this project.

Configurations are saved in the project's **`Config/UETestTool.ini`**. Commit this file and everyone on the project gets them.

### `Config/UETestTool.ini` format

The file can also be edited by hand; the app keeps comments and ordering.

```ini
; UE Test Tool launch configurations for this project.

[Config.Client (noHMD)]
Template=Client
Args=-game -log -noHMD -WINDOWED -ResX=1200 -ResY=800

[Config.LAN Client]
Template=Client
Connect=192.168.0.10

; Overrides the built-in Standalone for this project
[Config.Standalone]
Args=-game -log -noHMD
```

| Key | Meaning |
|---|---|
| `Template` | A built-in configuration, or one defined earlier in the file. Its launch behaviour is inherited, and so are its `Args` if the section has none. |
| `Args` | The command-line arguments. |
| `Map` | Optional, `True` or `False`: whether the selected map is passed. |
| `MapOptions` | Optional, appended to the map, e.g. `?listen`. |
| `Connect` | Optional, an address to connect to instead of opening a map. |

Problems such as an unknown template are shown as a warning under the list; the file still loads.

## Where things are stored

| What | Where |
|---|---|
| Shared configurations (commit this) | `<Project>/Config/UETestTool.ini` |
| Your last map, configuration, engine and extra arguments for a project | `<Project>/Saved/UETestTool/User.ini`. `Saved` is normally git-ignored. |
| Known projects, search folders, last project, window position | `%LOCALAPPDATA%\DrsUnrealTools\UETestTool\settings.json` |

## Building

See the [repository README](../../README.md#building).

To regenerate the icon from `Assets/IconSource/UE_Test_icon_1024px.png` (the script crops the logo and stretches it vertically to fill more of the square; see the settings at its top):

```powershell
powershell -ExecutionPolicy Bypass -File Tools\UETestTool\Assets\make-icon.ps1
```
