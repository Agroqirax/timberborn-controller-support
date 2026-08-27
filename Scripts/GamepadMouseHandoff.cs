using Timberborn.KeyBindingSystem;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ControllerSupport
{
	// Decides which physical device is driving a gamepad-tracked cursor this frame, so
	// GamepadBuildingPlacementController/GamepadAreaSelectionController/GamepadSelectionController
	// can keep the gamepad and a real mouse interchangeable on the same tool instead of the gamepad
	// locking the mouse out for as long as the tool is active. Judged on genuine activity, not
	// stale position: a mouse that hasn't moved since the tool was entered (or since the stick last
	// moved) shouldn't be treated as "in control" just because it's technically the last thing that
	// clicked something, or placement would seed onto wherever the OS cursor happens to be resting -
	// often a bottom-bar button, not a useful world position.
	//
	// Ties (both a stick step and real mouse activity land the same frame) favour the gamepad. Per
	// ARCHITECTURE.md's Environment note, the dev's own Steam Controller can emit synthetic mouse
	// movement depending on its Steam Input layout even while also registering as Gamepad.current in
	// a gamepad-style layout, so trusting a genuine stick push over a same-frame mouse delta that
	// might not be a real second device is the safer default.
	internal class GamepadMouseHandoff
	{
		// Comfortably above analog/HID noise - not measured against real hardware, revisit if real
		// play surfaces false handoffs away from the gamepad.
		private const float MouseMovementThreshold = 2f;

		// Matches InputService's own MouseLeftKey constant - read directly off KeyBindingRegistry
		// rather than through InputService.MainMouseButtonDown, which is one of the getters
		// InputServicePlacementPatch patches and would be circular (it returns gamepad-synthesized
		// state whenever GamepadPlacementState.Active is true, exactly the state this class decides).
		private const string MouseLeftKey = "MouseLeft";

		private readonly KeyBindingRegistry _keyBindingRegistry;
		private bool _gamepadControlled = true;

		public GamepadMouseHandoff(KeyBindingRegistry keyBindingRegistry)
		{
			_keyBindingRegistry = keyBindingRegistry;
		}

		// Call once, from the owning controller's Activate()/Engage() - restarts gamepad-controlled,
		// matching the existing screen-centre seed, which was always gamepad-only.
		public void Reset()
		{
			_gamepadControlled = true;
		}

		// Call once per frame, after this frame's stick step has been read. gamepadActionDown must
		// be a fresh down-edge on whatever this controller treats as its own gamepad action button -
		// a bare press with the stick otherwise idle should still count as "the player is using the
		// gamepad" even though it produces no step.
		public bool Update(Vector2Int step, bool gamepadActionDown)
		{
			if (step != Vector2Int.zero || gamepadActionDown)
			{
				_gamepadControlled = true;
			}
			else if (MouseActive())
			{
				_gamepadControlled = false;
			}

			return _gamepadControlled;
		}

		private bool MouseActive()
		{
			var mouse = Mouse.current;
			if (mouse == null)
			{
				return false;
			}

			var moved = mouse.delta.ReadValue().sqrMagnitude >= MouseMovementThreshold * MouseMovementThreshold;
			return moved || _keyBindingRegistry.IsDown(MouseLeftKey);
		}
	}
}
