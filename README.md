# Controller Support

This mod adds controller support to Timberborn, including support for navigating menus, controlling the camera, placing buildings, planting, marking tree cutting area, demolishing, and the priority tool.

Most of the game can be played with a controller, although some areas still require a mouse or keyboard. Building placement and other cursor-based tools have been redesigned to work with controller input rather than mouse input.

In some cases, most notable the building placement & planting tools, it was necessary to completely remove mouse input so controllers work properly. Aside from this case the game largely remains usable with a keyboard and mouse.

> [!NOTE]
> This is still very early and really buggy. Please submit bug reports on the discord in `#🚂individual-mods > 🎮 Controller Support` or on [github](https://github.com/agroqirax/timberborn-controller-support/issues).

## Install

> [!IMPORTANT]
> When a game reports that it doesn't support controller input steam automatically chooses the "**Keyboard (WASD) and Mouse**" layout which prevents Timberborn from receiving controller input.
> To make the controller work, open the controller settings in steam, go to "**Templates**" and select & apply "**Gamepad with Mouse Trackpad**".
> I'm also working on a custom layout. To try it go to "**Search**" and download "**Timberborn: Controller support for timberborn**" by "**Agroqirax**"

> [!TIP]
> The controller won't work while Timberborn's mod manager is open on startup because mods haven't been loaded yet. This is expected. To skip the mod manager on steam go to timberborn, properties and add `-skipModManager` to the launch options field. If the game crashes or a mod causes problems, remove `-skipModManager` from Steam's launch options to restore the mod manager and disable mods.

- [Steam workshop](https://steamcommunity.com/sharedfiles/filedetails/?id=3789502178): Click subscribe
- [Mod.io](https://mod.io/g/timberborn/m/controller-support): Download & extract to `~/Documents/Timberborn/Mods/controller-support`.
- [Github](https://github.com/agroqirax/timberborn-controller-support/releases/latest): Download & extract to `~/Documents/Timberborn/Mods/controller-support`.

## Controls

Most controls can be rebound but this is still being worked on.

> [!TIP]
> Use steam input to create custom mappings, assign the back buttons & grip sensors of the steam deck/controller and much more.

**Map editor**: Most map editor functions work with a controller, but terrain editing still requires a mouse.

### General controls

| Button            | Action           |
| ----------------- | ---------------- |
| Left stick & dpad | Navigate UI      |
| Right stick       | Scroll           |
| A                 | Confirm / Select |
| B                 | Cancel / back    |

### Game mode

| Button                         | Action                                                 |
| ------------------------------ | ------------------------------------------------------ |
| Right stick                    | Pan camera                                             |
| Right stick while pressed down | Tilt / Rotate camera                                   |
| Left trigger                   | Zoom in                                                |
| Right trigger                  | Zoom out                                               |
| Left shoulder                  | Decrease workplace priority, Decrease floodgate height |
| Right shoulder                 | Increase workplace priority, Increase floodgate height |
| X                              | Delete building                                        |
| Y                              | Unique building action                                 |
| Select / View                  | Enter [select mode](#select-mode)                      |
| Start / Menu                   | Pause building                                         |

### Building mode

| Button            | Action                  |
| ----------------- | ----------------------- |
| Left stick & dpad | Move cursor / selection |
| Left shoulder     | Rotate left             |
| Right shoulder    | Rotate right            |
| A                 | Place                   |
| Y                 | Flip building           |

### Select mode

| Button            | Action                    |
| ----------------- | ------------------------- |
| Left stick & dpad | Move cursor / selection   |
| A                 | Select / Hold to expand\* |
| B                 | Leave select mode         |
| Select / View     | Enter / leave select mode |

If there is something selectable at the cursor, it will be highlighted. Press A to select.

The updated building placement, tree cutting area, planting, demolishing & priority tools work in the same way with the exception that they are automatically (de)activated when you select their respective tool.

\*Only in tools where applicable

## Features

- Reworked building placement, selection & tools to use sticks instead of mouse cursor
- Analog inputs for movement, zoom, etc

## Known issues

- Terrain editing in the map editor still requires a mouse.
- Right stick is not rebindable
- Key rebind listener cannot bind joystick movements

[controller-layout]: https://www.padcrafter.com/?templates=Menus%7CGame%7CBuild+%2F+Select+mode&col=%23242424%2C%23606A6E%2C%23FFFFFF&outline=0&plat=0&timestamp=1787590118463&dpadUp=Navigate+Up%7CNavigate+UI+Up%7CMove+Selection+Up&dpadRight=Navigate+Right%7CNavigate+UI+Right%7CMove+Selection+Right&dpadLeft=Navigate+Left%7CNavigate+UI+Left%7CMove+Selection+Left&dpadDown=Navigate+Down%7CNavigate+UI+Down%7CMove+Selection+Down&leftStick=Navigate%7CNavigate+UI%7CMove+selection&backButton=%7CToggle+select+mode%7CExit+select+mode&startButton=%7CPause+building&rightStick=Scroll%7CPan+camera%7CPan+Camera&aButton=Confirm+%2F+Select%7CConfirm+%2F+Select%7CPlace&bButton=Cancel+%2F+Back%7CCancel+%2F+Back%7CCancel+%2F+exit&rightStickClick=%7CTilt%2FRotate+camera%7CTilt%2FRotate+Camera&xButton=%7CDelete+building&yButton=%7CUnique+building+action%7CFlip+building&rightTrigger=%7CZoom+out%7CZoom+out&leftTrigger=%7CZoom+in%7CZoom+in&leftBumper=%7CDecrease+workplace+priority+%2F+floodgate+height%7CRotate+left&rightBumper=%7CIncrease+workplace+priority+%2F+floodgate+height%7CRotate+right
