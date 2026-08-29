using Timberborn.KeyBindingSystem;
using UnityEngine;

namespace ControllerSupport
{
	// Turns the stick and d-pad into discrete navigation steps.
	//
	// Directions come out quantised to one of eight compass points and already converted to UI Toolkit's
	// coordinate space, where y grows downwards - the opposite of the stick. Quantising here is what
	// lets the navigator stay honest about "the element to my left" instead of re-deriving what the
	// player meant from a raw analog vector on every step.
	internal class GamepadReader
	{
		private const float PressZone = 0.5f;
		private const float ReleaseZone = 0.35f;
		private const float InitialRepeatDelay = 0.4f;
		private const float RepeatInterval = 0.12f;
		private const float DiagonalRatio = 0.55f;

		// Matches the Ids of this mod's own KeyBinding.CursorHeightUp/Down.blueprint.json - the d-pad's
		// vertical half, borrowed here for menu navigation. See ReadDirection.
		private const string CursorHeightUpKey = "CursorHeightUp";
		private const string CursorHeightDownKey = "CursorHeightDown";

		private Vector2Int _heldDirection;
		private float _nextRepeatTime;

		// Returns the step to take this frame, or zero when the stick is idle or still inside the
		// repeat delay.
		public Vector2Int ReadMove(KeyBindingRegistry registry)
		{
			var direction = ReadDirection(registry);
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

		private Vector2Int ReadDirection(KeyBindingRegistry registry)
		{
			// Hysteresis: once a direction is held, a smaller magnitude keeps it alive. Without it a
			// stick resting near the threshold flickers between "held" and "idle", re-triggering the
			// initial repeat delay over and over.
			var threshold = _heldDirection == Vector2Int.zero ? PressZone : ReleaseZone;

			// GamepadMoveUp/Down/Left/Right each carry both the left stick and the d-pad's horizontal
			// half as Primary/Secondary, so this one read already reflects whichever source (or both)
			// the player is actually pushing.
			var stick = GamepadAxis.Read(registry, GamepadAxis.Move);

			// The d-pad's *vertical* half is not on GamepadMove: it belongs to CursorHeightUp/Down, the
			// world cursor's height control, which cannot share an action with "move north" without one
			// press doing both. Folding it back in here is what keeps the whole d-pad usable for menu
			// navigation - this class only ever runs while no world cursor tool is engaged (see
			// GamepadNavigationInputProcessor and GamepadPlacementState.ToolEngaged), so the two
			// meanings never overlap in time and no dedicated fifth/sixth binding is needed.
			var heightY = registry.GetRawValue(CursorHeightUpKey) - registry.GetRawValue(CursorHeightDownKey);
			if (Mathf.Abs(heightY) > Mathf.Abs(stick.y))
			{
				stick.y = heightY;
			}

			return stick.magnitude >= threshold ? Quantize(stick) : Vector2Int.zero;
		}

		// Stick y points up, UI Toolkit's y points down, so the vertical axis is inverted here.
		//
		// Eight directions, not four, but not eight equal slices either. A diagonal only counts when the
		// weaker axis is at least DiagonalRatio of the stronger one, which gives each diagonal a narrow
		// band and leaves the cardinals generous. That matters because a diagonal is a precise request -
		// there is often nothing in the corner and the move does nothing - so a sloppy push meaning
		// "upwards" must not be read as one.
		private static Vector2Int Quantize(Vector2 raw)
		{
			var x = Mathf.Abs(raw.x);
			var y = Mathf.Abs(raw.y);
			var stepX = raw.x > 0f ? 1 : -1;
			var stepY = raw.y > 0f ? -1 : 1;

			if (Mathf.Min(x, y) >= Mathf.Max(x, y) * DiagonalRatio)
			{
				return new Vector2Int(stepX, stepY);
			}

			return x >= y ? new Vector2Int(stepX, 0) : new Vector2Int(0, stepY);
		}
	}
}
