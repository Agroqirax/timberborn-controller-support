using Timberborn.InputSystem;
using Timberborn.KeyBindingSystem;
using Timberborn.SingletonSystem;
using UnityEngine.InputSystem;

namespace ControllerSupport
{
	// Latches which physical device most recently showed genuine activity - mouse movement/click or a
	// gamepad stick/Confirm/Cancel press - independent of whether any gamepad tool controller
	// (GamepadBuildingPlacementController/GamepadAreaSelectionController/GamepadZiplineConnectionController)
	// is even running yet. Those controllers only start reading input once their own tool is already
	// ToolService.ActiveTool, which for a mouse click is a frame or more after the click itself (the
	// panel button's ClickEvent has to close the panel and swap the tool before the controller's own
	// type check passes) - by then KeyBindingRegistry's IsDown/IsUp edges for that click have already
	// come and gone, so GamepadMouseHandoff.Reset() sampling them directly at Activate() time missed a
	// mouse-triggered activation and fell back to its gamepad-controlled default. Running continuously
	// as a plain IUpdatableSingleton (not tied to any tool or input-processor chain) means the latch
	// already reflects the click by the time a controller's Activate() asks, however many frames late
	// that turns out to be.
	internal class RecentInputDeviceTracker : ILoadableSingleton, IUpdatableSingleton
	{
		// Same threshold/ids GamepadMouseHandoff itself uses for "the mouse is genuinely active".
		private const float MouseMovementThreshold = 2f;
		private const string MouseLeftKey = "MouseLeft";

		// Matches the Ids of this mod's own KeyBinding.CursorHeightUp/Down.blueprint.json.
		private const string CursorHeightUpKey = "CursorHeightUp";
		private const string CursorHeightDownKey = "CursorHeightDown";

		// Comfortably above stick deadzone noise - GamepadAxis.Read already returns 0 below the
		// underlying control's own deadzone, so this just guards against HID jitter on top of that.
		private const float GamepadAxisThreshold = 0.2f;

		private readonly KeyBindingRegistry _keyBindingRegistry;
		private readonly InputService _inputService;

		public bool GamepadControlled { get; private set; } = true;

		public RecentInputDeviceTracker(KeyBindingRegistry keyBindingRegistry, InputService inputService)
		{
			_keyBindingRegistry = keyBindingRegistry;
			_inputService = inputService;
		}

		public void Load()
		{
		}

		public void UpdateSingleton()
		{
			if (GamepadActive())
			{
				GamepadControlled = true;
			}
			else if (MouseActive())
			{
				GamepadControlled = false;
			}
		}

		// CursorHeightUp/Down count too. They are the only gamepad control this mod binds that isn't
		// reachable through Move/UIConfirm/UICancel, and leaving them out meant a player who had last
		// touched the mouse could press dpad up/down forever with nothing happening: the latch stayed
		// mouse-controlled, so every cursor controller's own handoff kept standing down, and the branch
		// that reads the height keys at all is inside the branch a gamepad press is supposed to enter.
		private bool GamepadActive()
		{
			return _inputService.UIConfirm || _inputService.UICancel
				|| _inputService.IsKeyHeld(CursorHeightUpKey) || _inputService.IsKeyHeld(CursorHeightDownKey)
				|| GamepadAxis.Read(_keyBindingRegistry, GamepadAxis.Move).sqrMagnitude
					>= GamepadAxisThreshold * GamepadAxisThreshold;
		}

		private bool MouseActive()
		{
			var mouse = Mouse.current;
			if (mouse == null)
			{
				return false;
			}

			var moved = mouse.delta.ReadValue().sqrMagnitude >= MouseMovementThreshold * MouseMovementThreshold;
			return moved || _keyBindingRegistry.IsDown(MouseLeftKey) || _keyBindingRegistry.IsUp(MouseLeftKey);
		}
	}
}
