# Changelog

All notable changes to this project will be documented in this file.

## [Unreleased]

### Added

- Persistent gamepad input-hint strip ("(A) Select", etc.) showing the player's most relevant actions for whatever they're currently doing - entity panel, dialog, building placement, area selection, scrollable list, bottom bar. Configurable to appear at the Top or Bottom of the screen, or turned off, via a new mod setting
- "Hide cursor" mod setting (Always / Auto / Never, default Auto) to control whether the cursor is automatically hidden while using a gamepad, always hidden, or never hidden
- "Focus entity panel on exiting select mode" mod setting (default on) to jump the gamepad cursor straight onto the entity panel when backing out of select mode with something selected, instead of leaving it on the bottom bar

### Changed

- Gamepad button icons in shortcut hints are now always shown; removed the "Show gamepad button icons in shortcut hints" mod setting
- Cursor autohide now applies globally whenever the gamepad is in control - main menu, camera navigation, dialogs, any tool - instead of only inside building placement, area selection, select mode and zipline connection

## [1.1.2.0.6] - 2026-08-30

### Added

- Gamepad rumble when an unstable core explodes, when dynamite explodes, a hazardous weather warning appears or a wonder is completed
- Gamepad select mode now shows unfinished buildings as their greyed-out finished model
- 3D gamepad cursor movement: dpad up/down moves the building-placement/area-selection/select-tool cursor's height
- The Increase/Decrease Floodgate Height shoulders now also drive the mechanical/compact/deep pump's flow rate, water depth/flow/contamination sensor thresholds, the power meter's and resource counter's percent thresholds (when shown as a slider), and the weather station's early activation hours - whichever single slider the selected entity's panel is currently showing
- The right stick now scrolls a beaver's needs list in the entity panel

### Changed

- Bottom bar wraps around
- Shortcut hint labels now show the gamepad button instead of the keyboard key whenever a gamepad is connected, even alongside a mouse/keyboard
- Shortcut hint labels now show the actual gamepad button icon instead of text (e.g. rotate/flip's [R]/[Shift-R]/[F] hints) when a gamepad is connected; can be turned off in mod settings to fall back to text
- The right stick now scrolls whichever list is actually nearest the current selection instead of always the first list found in a panel

### Fixed

- The gamepad cursor's selection is no longer one voxel below the highlighted face on the tools that pick objects
- The gamepad select tool no longer selects the same object from multiple different cursor heights
- The zipline connection tool now publishes its own cursor ray origin instead of inheriting whatever the last tool left behind on the shared placement state
- The automation lever's Switch On/Off button is now selectable and activatable with the gamepad (it drives the lever from raw pointer press/release instead of a click)
- Numeric text fields (IntegerField/FloatField - the power meter's and resource counter's threshold fields, among others) are now selectable with the gamepad; only string TextFields were reachable before
- The Increase/Decrease Floodgate Height keybind labels are renamed to Increase/Decrease Entity Slider in the keybind settings menu, since the shoulders now drive far more than floodgates - localized into all 14 shipped languages
- The flow and contamination sensors' threshold slider handle now updates immediately when its value changes from outside the slider itself (the gamepad shoulders, previously only reflected after closing and reopening the entity panel); the depth sensor's own slider was unaffected, since it already refreshed live
- The resource counter's fill-rate threshold slider handle now also updates immediately when moved with the gamepad shoulders, same fix as the flow/contamination sensors above
- The building deconstruction tool's "goods recovered" tooltip now anchors to the gamepad cursor's grid position instead of the real (hidden) mouse cursor while an area-selection tool is gamepad-driven, matching the fix already in place for the zipline connection tool's preview tooltip
- The scrollable mod list is no longer selectable

## [1.1.2.0.5] - 2026-08-28

### Added

- Gamepad support for the zipline connection tool: the stick jumps directly between candidate towers/poles in the pushed direction instead of moving a cursor.
- Optional support for the Cutter Tool and Grid Cutting workshop mods' tree-cutting tools
- Optional support for the Building Blueprints workshop mod's create/build/demolish tools

### Fixed

- Nested buttons are now reachable
- Buttons that only wire up Unity's native `clicked` event instead of Timberborn's own click convention (used by timber-ui) are now selectable and clickable with the gamepad
- Exit is now the default button on the confirm quit prompt
- Tooltips and info cards on the bottombar now appear when using a controller
- Placement/selection/zipline tools no longer briefly show gamepad-controlled mode (cursor hidden) when activated with the mouse instead of the gamepad's Confirm button
- The no-gamepad-detected startup popup (and its Steam Input layout-picker link) no longer shows on GOG/Epic builds, since Steam Input and the `steam://` link are Steam-only
- The zipline connection tooltip now anchors between the two towers being connected when the gamepad drives the tool, instead of following the real mouse cursor which may not be anywhere near them; still follows the cursor as normal when the mouse is in control

## [1.1.2.0.4] - 2026-08-27

### Added

- Gamepad support for the map editor's absolute/relative terrain height brushes and natural resource spawning/removal brushes
- Optional support for the FPP mod

### Fixed

- Gamepad UI cursor now correctly defaults to the select tool
- Natural resource spawning/removal brushes no longer immediately apply the instant they were selected
- Mouse/keyboard can now be used for building placement and all area tools alongside the controller; the system cursor now hides while the gamepad is driving and reappears the instant the mouse moves
- Shadows no longer blur while using certain tools

## [1.1.2.0.3] - 2026-08-27

### Added

- Rebindable keybind for the camera pan/rotate modifier (hold to rotate the camera with the stick instead of panning), default bound to right stick press
- Gamepad support for throttling valve and fill valve sliders, using the same keybind as floodgate height
- Localizations for all languages
- Keybind listener now recognizes stick inputs
- Show steam input warning if no controller found

### Changed

- Confirm/Cancel actions (UI navigation, entity selection, building/area placement) now follow whatever is bound to Confirm/Cancel instead of being hardcoded to specific gamepad buttons
- Camera pan/rotate modifier is no longer hardcoded to the right stick click; it now reads the new rebindable keybind above
- While a floodgate or valve is still under construction, builder priority now always takes the shared shoulder buttons over its own height/flow control, matching workplace priority's existing behavior; the height/flow control takes over once construction finishes

### Fixed

- Workplace priority controls could get permanently blocked by a leftover construction-priority control on a finished building when both were bound to the same gamepad button
- Crash on startup due to malformed localization file headers
- Throttling valve and fill valve sliders no longer also change the game speed when adjusted with the gamepad

## [1.1.2.0.2] - 2026-08-25

### Added

- Analog zoom

### Changed

- Removed custom rightstick keybinds. Moved to existing camera keybinds

### Fixed

- Storages can now have goods assigned
- Unrelevant elements can no longer be selected

## [1.1.2.0.1] - 2026-08-24

### Added

- Initial release

[unreleased]: https://github.com/agroqirax/timberborn-controller-support/compare/v1.1.2.0.3...HEAD
[1.1.2.0.6]: https://github.com/agroqirax/timberborn-controller-support/releases/tag/v1.1.2.0.6
[1.1.2.0.5]: https://github.com/agroqirax/timberborn-controller-support/releases/tag/v1.1.2.0.5
[1.1.2.0.4]: https://github.com/agroqirax/timberborn-controller-support/releases/tag/v1.1.2.0.4
[1.1.2.0.3]: https://github.com/agroqirax/timberborn-controller-support/releases/tag/v1.1.2.0.3
[1.1.2.0.2]: https://github.com/agroqirax/timberborn-controller-support/releases/tag/v1.1.2.0.2
[1.1.2.0.1]: https://github.com/agroqirax/timberborn-controller-support/releases/tag/1.1.2.0.1
