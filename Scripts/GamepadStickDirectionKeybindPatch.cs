using System.Reflection;
using HarmonyLib;
using Timberborn.KeyBindingSystem;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.LowLevel;

namespace ControllerSupport
{
	// InputBindingListener.ValidateInput never even sees a stick's up/down/left/right sub-controls:
	// InputEventPtr.EnumerateChangedControls (called from OnInputSystemEvent) skips every control
	// flagged IsSynthetic/UsesStateFromOtherControl before ValidateInput is ever invoked (see Unity's
	// InputEventControlEnumerator.MoveNext, the "IncludeSyntheticControls" check) - and StickControl's
	// four directions are declared exactly that way (Controls/StickControl.cs), reusing the x/y axis's
	// own memory rather than owning any of their own. So there is nothing IsButton-side to patch; the
	// direction controls are filtered out one level up, before the button check ever runs.
	// leftStickPress/rightStickPress (l3/r3) are real buttons on their own memory and were never
	// affected.
	//
	// What the enumerator does yield instead is the stick's real leaf controls, x and y, since a stick
	// push does change their backing memory. Those reach ValidateInput as plain AxisControls, which
	// IsButton rejects for a different reason (not a ButtonControl at all) and IsMouseScroll doesn't
	// recognise either, so the base game silently drops the event. The fix mirrors the mouse-scroll
	// case already in this file (IsMouseScroll/ConvertMouseScroll): read the axis's processed value
	// (ReadValueFromEvent runs it through the "axisDeadzone" processor declared on x/y, so resting
	// noise already reads as exactly 0) and resolve it to the corresponding synthetic direction control
	// by sign, then finish listening with that control - the same object a real button press would have
	// produced, so downstream rebinding logic (which reads inputControl.path) is none the wiser.
	[HarmonyPatch(typeof(InputBindingListener), "ValidateInput")]
	internal static class StickAxisValidateInputPatch
	{
		private static readonly MethodInfo NotifyAndFinishListeningMethod =
			AccessTools.Method(typeof(InputBindingListener), "NotifyAndFinishListening");

		[HarmonyPrefix]
		private static bool Prefix(
			InputBindingListener __instance, InputEventPtr inputEvent, InputControl inputControl, ref bool __result)
		{
			if (!(inputControl is AxisControl axisControl) || inputControl is ButtonControl ||
				!(inputControl.parent is StickControl stick))
			{
				return true;
			}

			var value = axisControl.ReadValueFromEvent(inputEvent);

			InputControl direction;
			if (axisControl == stick.x)
			{
				direction = value > 0f ? stick.right : (value < 0f ? stick.left : null);
			}
			else if (axisControl == stick.y)
			{
				direction = value > 0f ? stick.up : (value < 0f ? stick.down : null);
			}
			else
			{
				return true;
			}

			if (direction == null)
			{
				return true;
			}

			NotifyAndFinishListeningMethod.Invoke(__instance, new object[] { direction, InputModifiers.None });
			__result = true;
			return false;
		}
	}
}
