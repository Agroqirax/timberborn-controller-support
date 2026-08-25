using Timberborn.KeyBindingSystem;
using UnityEngine;

namespace ControllerSupport
{
	// Reconstructs an analog Vector2 from four independently-rebindable keybind actions, one per
	// direction. KeyBindingRegistry.GetRawValue(id) returns the bound InputControl's real float value
	// (not just IsDown/IsHeld's boolean edge), and Unity's synthetic per-direction sub-controls on a
	// stick or d-pad (<Gamepad>/leftStick/up, /dpad/right, ...) are ButtonControls with a continuous
	// 0..1 magnitude proportional to deflection past the deadzone - so this preserves full analog
	// speed-ramping, it isn't a discrete on/off reconstruction. Each of the four ids is free to carry
	// its own Primary+Secondary binding, which is what lets one action combine two physical sources
	// (e.g. left stick AND d-pad both driving "Up") the same way this mod's readers already did before
	// any of this was rebindable.
	internal readonly struct GamepadAxisKeys
	{
		public readonly string Up;
		public readonly string Down;
		public readonly string Left;
		public readonly string Right;

		public GamepadAxisKeys(string up, string down, string left, string right)
		{
			Up = up;
			Down = down;
			Left = left;
			Right = right;
		}
	}

	internal static class GamepadAxis
	{
		// Matches this mod's KeyBinding.GamepadMove*.blueprint.json - left stick primary, d-pad
		// secondary, one action per direction. Drives UI navigation and the world grid cursor.
		public static readonly GamepadAxisKeys Move =
			new GamepadAxisKeys("GamepadMoveUp", "GamepadMoveDown", "GamepadMoveLeft", "GamepadMoveRight");

		public static Vector2 Read(KeyBindingRegistry registry, GamepadAxisKeys keys)
		{
			var x = registry.GetRawValue(keys.Right) - registry.GetRawValue(keys.Left);
			var y = registry.GetRawValue(keys.Up) - registry.GetRawValue(keys.Down);
			return new Vector2(x, y);
		}
	}
}
