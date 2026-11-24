# UE Test Tool

The UE Test Tool is an application that helps you launch Unreal Engine projects in various configurations, or even whole multiplayer sessions. Launch in a single click, without having to type complex command lines or mess about with batch files. The tool has the following key features:
- User-friendly UI.
- Project, engine and map selection.
- Customisable configurations.
- Customisable multiplayer sessions, with as many different clients and client configurations as you need.
- Follows the Windows light/dark mode and accent colour.
- Can be run from anywhere (it does not need to live in a UE project's folder).
- Preferences, customisations and recent selections are remembered.

![UE Test Tool](../../docs/images/UETestTool.png)

## Usage

1. **Project**: Select the Unreal project you wish to run. Use **Folders…** to choose where the tool looks (e.g. your repos folder and/or an Unreal Projects folder). Projects in those folders, and up to three levels below them, are found on every search. Until you add a folder, the tool shows an error and finds nothing.
   - Use **Browse…** to add a project from anywhere else.
   - Removing a folder removes its projects from the list. Projects added with Browse… stay.
   - Right-click a project to remove it from the list.
   - Projects on a disconnected drive are greyed out until the drive is available again.
2. **Engine**: Select the Unreal Engine version you wish to run with. Available engine versions are found automatically (both launcher installs and registered source builds). Engines are compared by release (e.g. 5.8), so a project made with a 5.8 source build is fine on the 5.8 launcher install, and vice versa. If the selected engine's version does not match the one associated with the selected project, a warning is displayed, with an option to **Switch** to the correct engine version (if available).
   - A project's engine association is read from its `.uproject` file. For a project associated with a source build of the engine, this is a GUID rather than a version number. If the GUID matches a source build registered on this computer, that build's version is used. Otherwise the tool looks for the version in the project's most recent log file (in `Saved/Logs`). Failing that, a warning flags the project's engine version as unknown.
3. **Map**: Select the project's map that you wish to open. The tool finds the available maps in the project's `Content` folder and in any content plugins. With the list focused, type the start of a map's name to jump to it.
4. **Configuration**: Select the configuration you wish to launch with, or pick a [session](#multiplayer-sessions) to launch a server and its clients together. Add your own extra arguments to any configuration in the right-hand text box. They are remembered per configuration, for you only.
5. **Launch**: Press this button (or double-click on a configuration or session) to launch it.
6. **Stop**: Press this button to close all launched instances that are still running, including single instances and whole multiplayer sessions. The button shows how many instances are running. Game windows are closed as if you'd clicked their close button, while a dedicated server's console is ended. Instances launched before the tool was restarted aren't tracked.

Hover over the **Launch** button to see the full command line. For a multiplayer session, this includes the server's command line and those of each client. To copy it, right-click a configuration or session and choose **Copy command line**.

**⟳** (or **F5**): Press this button to refresh and repopulate the drop-down lists. Refreshing searches again for engines, projects and maps, and re-reads the project's config files (e.g. after a `git pull`).

**+ New**: Press this button to add a custom configuration or multiplayer session, or to open the project's `Config/UETestTool.ini`.

### Built-in configurations

| Configuration | Launches as |
|---|---|
| Dedicated Server | `<map> -server -log` |
| Listen Server | `<map>?listen -game -log` |
| Client | `127.0.0.1 -game -log` (no map: it connects to a server on this computer, which sets the map) |
| Standalone | `<map> -game -log` |

The editor executable and the `.uproject` path are automatically added to the front of the command line.

## Adding configurations for a project

Select **+ New** › **New configuration…** to create a new configuration. Press ✎ to edit or copy an existing one, or 🗑 to delete one. In the configuration editor, set the following:
- **Name**: a meaningful name for the configuration (e.g. `Client (noHMD)`).
- **Template**: an existing configuration to base the new one on (defaults to the one currently selected in the tool). The new configuration launches the same way (map, `?listen` or connect address) and starts with the template's arguments.
- **Arguments**: edit or extend them (e.g. add `-noHMD -WINDOWED -ResX=1200 -ResY=800`).

Click **Save** to save the new configuration. Saving under a built-in configuration's name overrides that built-in for this project. Configurations and sessions are saved in the project's **`Config/UETestTool.ini`**. Commit this file and everyone working on the project can use them.

## Multiplayer sessions

A session launches one server and any mix of clients (e.g. a Dedicated Server, a Client and a `Client (no HMD)`). They are listed under **Sessions**, below the configurations, and launch in exactly the same way.

When you launch a session:

1. The server starts on the selected map, with its configuration's arguments and your extra arguments.
2. The tool waits until the server's log says it is listening (`LogNet: ... GameNetDriver ... IpNetDriver listening on port 7777`). The status bar counts the seconds. **Stop** cancels this wait.
3. The clients start, 1.5 seconds apart, each with its own configuration's arguments and your extra arguments. A client that connects to `127.0.0.1` gets the server's port added (e.g. `127.0.0.1:7778` for a server started with `-port=7778`).

If the server closes, or isn't listening within 3 minutes, no clients are launched and the status bar says why. To know which log to watch, the tool adds `-abslog="<Project>/Saved/Logs/<Project>-Server.log"` to the server's command line, unless its arguments already have an `-abslog=`.

Select **+ New** › **New session…** to create a new session. Press ✎ to edit or copy an existing one, or 🗑 to delete one. In the session editor, pick the server, then set how many of each client configuration to launch. Only configurations based on Dedicated Server or Listen Server can be the server, and only configurations that connect to an address (such as Client) can be a client. Like configurations, sessions are saved in the project's **`Config/UETestTool.ini`**.

A session that can't launch (e.g. because it names a configuration that was renamed or deleted) will display an appropriate warning.

## Where things are stored

| What | Where |
|---|---|
| Shared configurations and sessions (commit this) | `<Project>/Config/UETestTool.ini` |
| Your last map, configuration or session, engine and extra arguments for a project | `<Project>/Saved/UETestTool/User.ini`. `Saved` is normally git-ignored. |
| Known projects, search folders, last project, window position | `%LOCALAPPDATA%\DrsUnrealTools\UETestTool\settings.json` |

### `Config/UETestTool.ini` format

The file can also be edited by hand. The app keeps any comments or ordering you add.

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

; A session: one Launch line per instance, one of them a server
[Session.Dedicated + noHMD]
Launch=Dedicated Server
Launch=Client
Launch=Client (noHMD)
```

| Key | Meaning |
|---|---|
| `Template` | A built-in configuration, or one defined earlier in the file. Its launch behaviour is inherited, and so are its `Args` if the section has none. |
| `Args` | The command-line arguments. |
| `Map` | Optional, `True` or `False`, specifying whether the selected map is passed. |
| `MapOptions` | Optional, appended to the map, e.g. `?listen`. |
| `Connect` | Optional, an address to connect to instead of opening a map. |
| `Launch` | In a `[Session.<Name>]` section, specifies a configuration to launch. Repeat the line for each instance. A session named after a built-in one overrides it. |

Problems such as an unknown template are shown as a warning in the tool. The file still loads.
