using Timberborn.InputSystem;
using UnityEngine;

namespace ControllerSupport
{
	// Turns CursorHeightUp/CursorHeightDown (dpad up/down) into one z step at a time. Same repeat/
	// hysteresis shape as GamepadGridStepReader, copied rather than shared since this one drives a
	// single button pair rather than a stick/d-pad-derived axis.
	internal class GamepadHeightStepReader
	{
		private const float InitialRepeatDelay = 0.4f;
		private const float RepeatInterval = 0.12f;

		private const string UpKey = "CursorHeightUp";
		private const string DownKey = "CursorHeightDown";

		private int _heldDirection;
		private float _nextRepeatTime;

		// Returns the z step to take this frame (+1/-1), or zero when neither key is held or still
		// inside the repeat delay.
		public int ReadStep(InputService inputService)
		{
			var direction = ReadDirection(inputService);
			if (direction == 0)
			{
				_heldDirection = 0;
				return 0;
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
				return 0;
			}

			_nextRepeatTime = now + RepeatInterval;
			return direction;
		}

		public void Reset()
		{
			_heldDirection = 0;
			_nextRepeatTime = 0f;
		}

		private static int ReadDirection(InputService inputService)
		{
			var up = inputService.IsKeyHeld(UpKey);
			var down = inputService.IsKeyHeld(DownKey);
			return up == down ? 0 : up ? 1 : -1;
		}
	}
}
