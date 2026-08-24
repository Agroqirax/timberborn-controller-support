# Controller Support

Add controller support to timberborn.

In some cases, most notable the building placement & planting tools, it was necessary to completely remove mouse input so controllers work properly. Aside from this case the game largely remains operable with a keyboard and mouse.

> [!NOTE]
> This is still very early and really buggy. Please submit bug reports on the discord in `#🚂individual-mods > 🎮 Controller Support` or in [github issues](https://github.com/agroqirax/timberborn-controller-support/issues).

> [!IMPORTANT]
> On games that report they do not support controller input steam automatically choses the "**Keyboard (WASD) and Mouse**" layout which makes the controller invisible to timberborn.
> To make the controller work, open the controller settings in steam, go to "**Templates**" and select & apply "**Gamepad with Mouse Trackpad**".
> I'm also working on a custom layout. To try it go to "**Search**" and download "**Timberborn: Controller support for timberborn**" by "**Agroqirax**"

> [!TIP]
> During the mod manager on startup the controller won't work yet because mods aren't loaded at that point (so you can't continue without using the mouse or keyboard). To skip the mod manager on steam go to timberborn, properties and add `-skipModManager` to the launch options field. Should mods break or the game crash remove this parameter and you'll get the mod manager back so you can turn mods off.

## Install

- [Steam workshop](https://steamcommunity.com/sharedfiles/filedetails/?id=xxxxxx): Click subscribe
- [Mod.io](https://mod.io/g/timberborn/m/controller-support): Download & extract to `~/Documents/Timberborn/Mods/controller-support`.
- [Github](https://github.com/agroqirax/timberborn-controller-support/releases/latest): Download & extract to `~/Documents/Timberborn/Mods/controller-support`.

## Controls

> [!NOTE]
> Most controls are not rebindable yet. You may have some success with steam input but this is being worked on.

<link rel="stylesheet" href="https://fonts.googleapis.com/css2?family=Material+Symbols+Outlined:opsz,wght,FILL,GRAD@24,400,0,0" />

<style>
    .material-symbols-outlined {
        vertical-align: middle;
    }
</style>

### Menus

<span class="material-symbols-outlined">game_stick_left</span><span class="material-symbols-outlined">gamepad</span> Navigate UI<br>
<span class="material-symbols-outlined">game_stick_right</span> Scroll<br>
<span class="material-symbols-outlined">hdr_auto</span> Confirm/select<br>
<span class="material-symbols-outlined">b_circle</span> Cancel/back<br>

### Game

<span class="material-symbols-outlined">game_stick_left</span><span class="material-symbols-outlined">gamepad</span> Navigate UI<br>
<span class="material-symbols-outlined">game_stick_right</span> Pan camera<br>
<span class="material-symbols-outlined">game_stick_r3</span> Angle camera<br>
<span class="material-symbols-outlined">game_trigger_left</span> Zoom in<br>
<span class="material-symbols-outlined">game_trigger_right</span> Zoom out<br>
<span class="material-symbols-outlined">game_button_l1</span> Decrease workplace priority<br>
<span class="material-symbols-outlined">game_button_r1</span> Increase workplace priority<br>
<span class="material-symbols-outlined">game_button_l1</span> Decrease floodgate height<br>
<span class="material-symbols-outlined">game_button_r1</span> Increase floodgate height<br>
<span class="material-symbols-outlined">hdr_auto</span> Confirm/select<br>
<span class="material-symbols-outlined">b_circle</span> Cancel/back<br>
<span class="material-symbols-outlined">x_circle</span> Delete building<br>
<span class="material-symbols-outlined">y_circle</span> Unique building action<br>

### Building/selecting mode

<span class="material-symbols-outlined">game_stick_left</span><span class="material-symbols-outlined">gamepad</span> Move selection<br>
<span class="material-symbols-outlined">game_button_l1</span> Rotate left<br>
<span class="material-symbols-outlined">game_button_r1</span> Rotate right<br>
<span class="material-symbols-outlined">hdr_auto</span> Place<br>
<span class="material-symbols-outlined">b_circle</span> Cancel<br>
<span class="material-symbols-outlined">y_circle</span> Flip building<br>
