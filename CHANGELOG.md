# Changelog

All notable changes to this project will be documented in this file.

## [Unreleased]

### Added

- Gamepad support for the map editor's absolute/relative terrain height brushes and natural resource spawning/removal brushes

### Fixed

- Gamepad UI cursor now correctly defaults to the select tool
- Natural resource spawning/removal brushes no longer immediately apply the instant they were selected

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
[1.1.2.0.3]: https://github.com/agroqirax/timberborn-controller-support/releases/tag/v1.1.2.0.3
[1.1.2.0.2]: https://github.com/agroqirax/timberborn-controller-support/releases/tag/v1.1.2.0.2
[1.1.2.0.1]: https://github.com/agroqirax/timberborn-controller-support/releases/tag/1.1.2.0.1
