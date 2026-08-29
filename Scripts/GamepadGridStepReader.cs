using Timberborn.KeyBindingSystem;
using UnityEngine;

namespace ControllerSupport
{
	// Turns the stick into one grid step at a time, camera-relative: "up" on the stick
	// always nudges away from the camera, not up the screen, so nudging the placement ghost feels
	// right no matter which way the camera is currently facing.
	//
	// Same repeat/hysteresis shape as GamepadReader, copied rather than reused - that class
	// quantizes before any rotation is applied, which UI navigation has no need for but a
	// camera-relative grid cursor does.
	internal class GamepadGridStepReader
	{
		private const float PressZone = 0.5f;
		private const float ReleaseZone = 0.35f;
		private const float InitialRepeatDelay = 0.4f;
		private const float RepeatInterval = 0.12f;
		private const float DiagonalRatio = 0.55f;

		private Vector2Int _heldDirection;
		private float _nextRepeatTime;

		// Returns the grid step to take this frame, or zero when the stick is idle or still inside
		// the repeat delay.
		public Vector2Int ReadStep(KeyBindingRegistry registry, float cameraHorizontalAngle)
		{
			var direction = ReadDirection(registry, cameraHorizontalAngle);
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

		private Vector2Int ReadDirection(KeyBindingRegistry registry, float cameraHorizontalAngle)
		{
			var threshold = _heldDirection == Vector2Int.zero ? PressZone : ReleaseZone;

			// One read of GamepadMoveUp/Down/Left/Right covers the left stick and the keyboard arrows.
			// The d-pad is deliberately not on this axis any more: its vertical half is CursorHeightUp/
			// Down, the cursor's own height control, which cannot share an action with "move north"
			// without one press doing both.
			var stick = GamepadAxis.Read(registry, GamepadAxis.Move);
			return stick.magnitude >= threshold ? Quantize(Rotate(stick, cameraHorizontalAngle)) : Vector2Int.zero;
		}

		// CameraService.MoveCameraBy rotates a (x, 0, z) world delta by Quaternion.AngleAxis(angle,
		// Vector3.up) to turn stick input into camera-relative panning; this is the same rotation,
		// worked out in 2D directly on the (x, z) plane, which Timberborn's grid X/Y map onto 1:1.
		private static Vector2 Rotate(Vector2 stick, float cameraHorizontalAngle)
		{
			var radians = cameraHorizontalAngle * Mathf.Deg2Rad;
			var sin = Mathf.Sin(radians);
			var cos = Mathf.Cos(radians);
			return new Vector2(stick.x * cos + stick.y * sin, stick.y * cos - stick.x * sin);
		}

		// Eight directions, not four, but not eight equal slices either - see GamepadReader for why
		// a diagonal needs its own narrow band rather than an even split.
		private static Vector2Int Quantize(Vector2 raw)
		{
			var x = Mathf.Abs(raw.x);
			var y = Mathf.Abs(raw.y);
			var stepX = raw.x > 0f ? 1 : -1;
			var stepY = raw.y > 0f ? 1 : -1;

			if (Mathf.Min(x, y) >= Mathf.Max(x, y) * DiagonalRatio)
			{
				return new Vector2Int(stepX, stepY);
			}

			return x >= y ? new Vector2Int(stepX, 0) : new Vector2Int(0, stepY);
		}
	}
}
