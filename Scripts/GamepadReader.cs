using UnityEngine;
using UnityEngine.InputSystem;

namespace ControllerSupport
{
	// Turns the stick and d-pad into discrete navigation steps.
	//
	// Directions come out quantised to a single cardinal axis and already converted to UI Toolkit's
	// coordinate space, where y grows downwards - the opposite of the stick. Quantising here is what
	// lets the navigator stay honest about "the element to my left" instead of trying to interpret a
	// diagonal push.
	internal class GamepadReader
	{
		private const float PressZone = 0.5f;
		private const float ReleaseZone = 0.35f;
		private const float InitialRepeatDelay = 0.4f;
		private const float RepeatInterval = 0.12f;

		private Vector2Int _heldDirection;
		private float _nextRepeatTime;

		// Returns the step to take this frame, or zero when the stick is idle or still inside the
		// repeat delay.
		public Vector2Int ReadMove(Gamepad gamepad)
		{
			var direction = ReadDirection(gamepad);
			if (direction == Vector2Int.zero)
			{
				_heldDirection = Vector2Int.zero;
				return Vector2Int.zero;
			}

			var now = Time.unscaledTime;
			if (direction != _heldDirection)
			{
				_heldDirection = direction;
				_nextRepeatTime = now + InitialRepeatDelay;
				return direction;
			}

			if (now < _nextRepeatTime)
			{
				return Vector2Int.zero;
			}

			_nextRepeatTime = now + RepeatInterval;
			return direction;
		}

		public void Reset()
		{
			_heldDirection = Vector2Int.zero;
			_nextRepeatTime = 0f;
		}

		private Vector2Int ReadDirection(Gamepad gamepad)
		{
			// Hysteresis: once a direction is held, a smaller magnitude keeps it alive. Without it a
			// stick resting near the threshold flickers between "held" and "idle", re-triggering the
			// initial repeat delay over and over.
			var threshold = _heldDirection == Vector2Int.zero ? PressZone : ReleaseZone;

			var stick = gamepad.leftStick.ReadValue();
			if (stick.magnitude >= threshold)
			{
				return Quantize(stick);
			}

			var dpad = gamepad.dpad.ReadValue();
			return dpad.magnitude >= threshold ? Quantize(dpad) : Vector2Int.zero;
		}

		// Stick y points up, UI Toolkit's y points down, so the vertical axis is inverted here.
		private static Vector2Int Quantize(Vector2 raw)
		{
			return Mathf.Abs(raw.x) >= Mathf.Abs(raw.y)
				? new Vector2Int(raw.x > 0f ? 1 : -1, 0)
				: new Vector2Int(0, raw.y > 0f ? -1 : 1);
		}
	}
}
