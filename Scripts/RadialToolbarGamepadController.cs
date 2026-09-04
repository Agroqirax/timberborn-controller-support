using Timberborn.InputSystem;
using Timberborn.KeyBindingSystem;
using Timberborn.SingletonSystem;
using UnityEngine;
using UnityEngine.UIElements;

namespace ControllerSupport
{
	// Drives the optional RadialToolbar workshop mod's wedge selection from the left stick, in
	// parallel with (never replacing) the real mouse/trackpad-hover-and-click path it already
	// supports natively. See RadialToolbarIntegration for why this exists as a Harmony-adjacent
	// singleton rather than patches alone: the preview needs a per-frame driver, and the confirm step
	// needs a real registered IInputProcessor to win a priority race against RadialToolbar's own.
	internal class RadialToolbarGamepadController : ILoadableSingleton, ILateUpdatableSingleton, IInputProcessor
	{
		// Stick magnitude below this is treated as centred - leaves the native mouse/trackpad-driven
		// hover fully in control while the player isn't actively aiming with the stick, so the two
		// input paths don't fight every frame. Lower than GamepadReader.PressZone (0.5f) deliberately:
		// this drives continuous aiming, not a discrete step, so it should engage on a light push.
		private const float Deadzone = 0.15f;

		// Only needs to clear ToolbarSegmentProvider.GetSegmentAt's own dead-centre exclusion
		// (sqrMagnitude < 0.0001) and stay within the fullscreen element's own contentRect - a few
		// hundred pixels comfortably satisfies both on any real display.
		private const float PreviewRadius = 250f;

		// Bridges RadialToolbarIntegration's static Harmony postfixes (on ToolbarController.Show/
		// Dismiss) to this singleton instance, the same way GamepadPlacementState bridges patches to
		// the mod's other per-frame controllers. AsSingleton guarantees exactly one of these ever
		// exists per session.
		public static RadialToolbarGamepadController Instance;

		private readonly InputService _inputService;
		private readonly KeyBindingRegistry _keyBindingRegistry;

		public RadialToolbarGamepadController(InputService inputService, KeyBindingRegistry keyBindingRegistry)
		{
			_inputService = inputService;
			_keyBindingRegistry = keyBindingRegistry;
		}

		public void Load()
		{
			Instance = this;
		}

		// Called from RadialToolbarIntegration's ToolbarController.Show() postfix. Registering here
		// (rather than once at Load()) is what lets this sit *after* ToolbarController's own
		// AddInputProcessor(this) in InputService's list every time the toolbar opens - InputService
		// walks that list last-added-first, so being added after means ProcessInput below is asked
		// before ToolbarController's own, which is what lets it intercept UIConfirm. Remove-then-add
		// mirrors GamepadNavigationInputProcessor's own re-registration pattern - harmless no-op if not
		// currently registered.
		public void NotifyShown()
		{
			_inputService.RemoveInputProcessor(this);
			_inputService.AddInputProcessor(this);
		}

		public void NotifyDismissed()
		{
			_inputService.RemoveInputProcessor(this);
		}

		// Preview: highlights whatever wedge the stick is aimed at, every frame, purely by calling
		// RadialToolbar's own public HighlightSegment - never selects anything itself.
		public void LateUpdateSingleton()
		{
			if (!RadialToolbarState.IsOpen)
			{
				return;
			}

			var element = RadialToolbarState.ToolbarElementInstance as VisualElement;
			if (element == null || RadialToolbarState.SegmentProviderInstance == null
				|| RadialToolbarState.HighlightSegmentMethod == null || RadialToolbarState.GetSegmentAtMethod == null)
			{
				return;
			}

			var stick = GamepadAxis.Read(_keyBindingRegistry, GamepadAxis.Move);
			if (stick.sqrMagnitude < Deadzone * Deadzone)
			{
				return;
			}

			// Direction only - magnitude past the deadzone is discarded, so every direction (cardinal
			// or diagonal) reaches the same fixed point on a perfect circle around the hub. Raw
			// per-axis stick output isn't circular, so normalizing first is what keeps diagonals exactly
			// as easy to hit as cardinals, matching RadialToolbar's own even angular slicing regardless
			// of whether it's configured for 4 or 8 segments.
			//
			// GamepadAxis.Read is up-positive (matches physical stick tilt), but
			// ToolbarSegmentProvider.GetSegmentAt works in UI Toolkit's y-down screen space - confirmed
			// from its Direction.Up wedge sitting at angle -90, which only matches a point above centre
			// (negative Y in that space). Without the flip, pushing up landed in the Down wedge instead -
			// GamepadReader's own quantizer elsewhere in this mod inverts Y for this exact reason.
			var direction = new Vector2(stick.x, -stick.y).normalized;
			var center = element.contentRect.center;
			var aim = center + direction * PreviewRadius;
			var index = RadialToolbarState.GetSegmentAtMethod.Invoke(RadialToolbarState.SegmentProviderInstance,
				new object[] { (Vector3)aim });

			RadialToolbarState.HighlightSegmentMethod.Invoke(RadialToolbarState.ToolbarElementInstance,
				new object[] { index });
		}

		// Confirm: A/UIConfirm commits whatever is currently highlighted - by mouse hover or by the
		// stick preview above - instead of RadialToolbar's own native meaning for that same key
		// ("close the menu", see ToolbarController.ProcessInput). Returning true here is what stops
		// ToolbarController.ProcessInput from also running this same frame - see NotifyShown above for
		// why registration order guarantees this runs first.
		public bool ProcessInput()
		{
			if (!RadialToolbarState.IsOpen || !_inputService.UIConfirm)
			{
				return false;
			}

			if (!RadialToolbarState.LastHighlighted.HasValue || RadialToolbarState.OnSegmentChosenMethod == null
				|| RadialToolbarState.ToolbarControllerInstance == null)
			{
				return false;
			}

			RadialToolbarState.OnSegmentChosenMethod.Invoke(RadialToolbarState.ToolbarControllerInstance,
				new object[] { RadialToolbarState.LastHighlighted.Value });
			return true;
		}
	}
}
