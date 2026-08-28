# Changelog

All notable changes to this project will be documented in this file.

## [Unreleased]

### Added

- Gamepad support for the zipline connection tool: the stick jumps directly between candidate towers/poles in the pushed direction instead of moving a cursor.
- Optional support for the Cutter Tool and Grid Cutting workshop mods' tree-cutting tools
- Optional support for the Building Blueprints workshop mod's create/build/demolish tools

### Fixed

- Nested buttons are now reachable
- Buttons that only wire up Unity's native `clicked` event instead of Timberborn's own click convention (used by timber-ui) are now selectable and clickable with the gamepad
- Exit is now the default button on the confirm quit prompt
- Tooltips and info cards on the bottombar now appear when using a controller

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
[1.1.2.0.4]: https://github.com/agroqirax/timberborn-controller-support/releases/tag/v1.1.2.0.4
[1.1.2.0.3]: https://github.com/agroqirax/timberborn-controller-support/releases/tag/v1.1.2.0.3
[1.1.2.0.2]: https://github.com/agroqirax/timberborn-controller-support/releases/tag/v1.1.2.0.2
[1.1.2.0.1]: https://github.com/agroqirax/timberborn-controller-support/releases/tag/1.1.2.0.1
