using Timberborn.InputSystem;
using Timberborn.SingletonSystem;

namespace ControllerSupport
{
	// Single global source of truth for the system (and any game-set custom) cursor's visibility -
	// both route through the one Cursor.visible flag InputService.HideCursor/ShowCursor toggle. Runs
	// unconditionally every frame, everywhere the mod's gamepad navigation works (menus, game, map
	// editor): "Always" hides unconditionally, "Never" shows unconditionally, and "Auto" hides
	// whenever RecentInputDeviceTracker says the gamepad is in control. No dialog/tool/exit special
	// casing - those all reach the cursor through the exact same InputService calls this class makes,
	// and the mod's own gamepad UI navigation (see ARCHITECTURE.md) already drives dialogs and menus
	// without needing the OS cursor visible at all.
	internal class CursorAutohideController : IUpdatableSingleton
	{
		private readonly InputService _inputService;
		private readonly RecentInputDeviceTracker _recentInputDeviceTracker;
		private readonly CursorSettings _settings;

		public CursorAutohideController(InputService inputService,
			RecentInputDeviceTracker recentInputDeviceTracker, CursorSettings settings)
		{
			_inputService = inputService;
			_recentInputDeviceTracker = recentInputDeviceTracker;
			_settings = settings;
		}

		public void UpdateSingleton()
		{
			var hide = _settings.HideCursor.Value switch
			{
				"Always" => true,
				"Never" => false,
				_ => _recentInputDeviceTracker.GamepadControlled,
			};

			if (hide)
			{
				_inputService.HideCursor();
			}
			else
			{
				_inputService.ShowCursor();
			}
		}
	}
}
