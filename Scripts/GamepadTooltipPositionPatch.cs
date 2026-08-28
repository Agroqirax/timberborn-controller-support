using System;
using System.Reflection;
using HarmonyLib;
using Timberborn.TooltipSystem;
using UnityEngine;
using UnityEngine.UIElements;

namespace ControllerSupport
{
	// MouseTooltipPositioner always anchors the one shared Tooltip singleton to
	// InputService.MousePositionNdc - the real OS mouse position - with no idea what actually triggered
	// the tooltip. VisualElementProbe.DispatchHover synthesizes the MouseEnterEvent that makes the
	// tooltip appear for a gamepad-selected element, so left unpatched the tooltip shows up wherever the
	// real mouse last happened to be - often nowhere near the selected button, sometimes off the visible
	// HUD entirely if the mouse hasn't moved all session.
	//
	// Rather than reimplementing the offset/clamping math, this calls the real
	// CalculateCursorOffset/CalculateHorizontalPosition/CalculateVerticalPosition methods with a
	// synthetic "mouse position" derived from the selected element instead of the real mouse - so a
	// gamepad tooltip behaves exactly like a mouse one (same screen-edge clamping, same resolution
	// scaling, same below-the-point placement the real cursor gets), just anchored at the button's
	// bottom-centre instead of wherever the mouse happens to be. Anchoring below the button rather than
	// beside it is what keeps the tooltip from covering neighbouring buttons the way a naive
	// beside-the-element placement did.
	//
	// GamepadTooltipDelayPatch keeps GamepadTooltipAnchor.Current pointed at whichever element's hover
	// most recently won - mouse or gamepad, since this mod lets both drive the UI at once - so a real
	// mouse hover falls straight through to the original mouse-following positioning unchanged even
	// while a gamepad selection also exists elsewhere on screen.
	//
	// MouseTooltipPositioner and its members compile as public against this mod's Plugins reference
	// assembly (publicized for modding), but that's a compile-time stub only - the assembly the running
	// game actually loads keeps the real (internal) accessibility, and a direct call
	// (`__instance.CalculateCursorOffset()`) throws MethodAccessException at runtime the same way a
	// direct field write on Tooltip did (see GamepadTooltipDelayPatch - that one crashed the game
	// outright). Reflection sidesteps the runtime check the same way VisualElementProbe already has to
	// for internal UI Toolkit members; Harmony's own method patching (typeof/nameof below) is unaffected.
	[HarmonyPatch(typeof(MouseTooltipPositioner))]
	internal static class GamepadTooltipPositionPatch
	{
		private static readonly MethodInfo CalculateCursorOffsetMethod =
			AccessTools.Method(typeof(MouseTooltipPositioner), "CalculateCursorOffset");
		private static readonly MethodInfo CalculateHorizontalPositionMethod =
			AccessTools.Method(typeof(MouseTooltipPositioner), "CalculateHorizontalPosition");
		private static readonly MethodInfo CalculateVerticalPositionMethod =
			AccessTools.Method(typeof(MouseTooltipPositioner), "CalculateVerticalPosition");

		[HarmonyPatch(nameof(MouseTooltipPositioner.UpdatePosition))]
		[HarmonyPrefix]
		private static bool Prefix(object __instance, VisualElement visualElement)
		{
			var target = GamepadTooltipAnchor.Current;
			if (target == null || target.panel == null)
			{
				return true;
			}

			if (CalculateCursorOffsetMethod == null || CalculateHorizontalPositionMethod == null || CalculateVerticalPositionMethod == null)
			{
				return true;
			}

			try
			{
				var fraction = TargetFraction(target, visualElement.parent);
				var offset = (Vector2)CalculateCursorOffsetMethod.Invoke(__instance, null);
				var left = (float)CalculateHorizontalPositionMethod.Invoke(null, new object[] { visualElement, fraction.x, offset.x });
				var top = (float)CalculateVerticalPositionMethod.Invoke(null, new object[] { visualElement, fraction.y, offset.y });
				visualElement.style.left = left;
				visualElement.style.top = top;
			}
			catch (Exception e)
			{
				Debug.LogWarning($"[ControllerSupport] Could not anchor tooltip to gamepad selection: {e.Message}");
				return true;
			}

			return false;
		}

		// CalculateHorizontalPosition/CalculateVerticalPosition multiply the fraction they're given back
		// out against the tooltip's own PARENT's resolved width/height, not the raw OS screen - dividing
		// by Screen.width/Screen.height instead (an earlier version of this did) only happens to line up
		// for the real mouse because InputService.MousePositionNdc is itself Screen-relative; a button
		// living in a differently-scaled panel (UI scaling / reference-resolution panels) comes out
		// wrong, landing well off from where the button actually is. WorldToLocal against the tooltip's
		// own parent - the same conversion SelectionHighlighter's ring already relies on - sidesteps any
		// scale mismatch entirely by working in the exact space the reused functions expect.
		//
		// Anchoring at the button's bottom-centre rather than its top-left is what makes the tooltip
		// open fully below the button instead of across its lower half; CalculateVerticalPosition
		// expects a Y fraction where 1 = top and 0 = bottom (mirroring mouse screen space), so the
		// panel's own top-down Y fraction has to be flipped before use.
		private static Vector2 TargetFraction(VisualElement target, VisualElement tooltipParent)
		{
			var bound = target.worldBound;
			var anchorWorld = new Vector2(bound.center.x, bound.yMax);
			var local = tooltipParent.WorldToLocal(anchorWorld);

			var width = tooltipParent.resolvedStyle.width;
			var height = tooltipParent.resolvedStyle.height;
			var fractionX = width > 0f ? local.x / width : 0f;
			var fractionYTopDown = height > 0f ? local.y / height : 0f;

			return new Vector2(fractionX, 1f - fractionYTopDown);
		}
	}
}
