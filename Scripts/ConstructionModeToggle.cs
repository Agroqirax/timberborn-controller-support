using System;
using System.Reflection;
using HarmonyLib;
using Timberborn.ConstructionMode;
using UnityEngine;

namespace ControllerSupport
{
	// Turns "show me what the unfinished buildings will look like" on and off the way a real tool does.
	//
	// That is `ConstructionModeService`: it swaps every unfinished building's scaffolding for a
	// greyed-out preview of the finished model (`ConstructionModeModel.EnterConstructionMode` calls
	// `BuildingModel.ShowFinishedModel`) and hides the water while it is on. The game enters it from
	// three places - a tool group carrying `ConstructionModeToolGroupSpec` in its blueprint (only
	// Demolishing and BuilderPriority, checked in Blueprints.zip), any tool implementing
	// `IConstructionModeEnabler` (every `BlockObjectTool`, plus the zipline, duplicate-settings and
	// transmitter-picker tools), and selecting an unfinished building.
	//
	// Gamepad select mode is none of those - it is a submode of the default CursorTool, not a tool of
	// its own - so it got nothing, exactly like the water toggle it already had to do by hand.
	//
	// Reflection because the two methods are private, and deliberately so rather than the alternative:
	// posting a synthetic `ToolEnteredEvent` for a tool implementing `IConstructionModeEnabler` would
	// also reach ToolWaterToggler, DescriptionPanelController, PanelToolSwitcher and the bottom bar's
	// own button-selection state, none of which should think a tool was entered. Two private calls are
	// a far smaller blast radius than one public event.
	//
	// `InConstructionMode` is public, which is what makes re-asserting cheap - see the caller: the
	// service drops out of construction mode on any `SelectableObjectUnselectedEvent`, and select mode
	// unselects whenever the player confirms on empty space, so it has to be put back.
	internal class ConstructionModeToggle
	{
		private static readonly MethodInfo EnterMethod =
			AccessTools.Method(typeof(ConstructionModeService), "EnterConstructionMode");

		private static readonly MethodInfo ExitMethod =
			AccessTools.Method(typeof(ConstructionModeService), "ExitConstructionMode");

		private readonly ConstructionModeService _constructionModeService;
		private bool _failed;

		public ConstructionModeToggle(ConstructionModeService constructionModeService)
		{
			_constructionModeService = constructionModeService;
		}

		// Safe to call every frame - the service's own Enter is a no-op once it is already on, and the
		// InConstructionMode check in front of it means the usual frame does no work at all.
		public void Enable()
		{
			if (!_constructionModeService.InConstructionMode)
			{
				Invoke(EnterMethod);
			}
		}

		// Not guaranteed to take effect, and that is correct: ConstructionModeService refuses to leave
		// construction mode while an unfinished building is selected or a construction tool group is
		// open - the same thing that happens when a mouse user closes the demolish category with a
		// half-built house still selected.
		public void Disable()
		{
			if (_constructionModeService.InConstructionMode)
			{
				Invoke(ExitMethod);
			}
		}

		// One failure disables this for good rather than throwing once per frame. Losing the finished-
		// model preview is cosmetic; a controller that stops responding because a reflected call went
		// missing in a game update is not.
		private void Invoke(MethodInfo method)
		{
			if (_failed || method == null)
			{
				return;
			}

			try
			{
				method.Invoke(_constructionModeService, null);
			}
			catch (Exception e)
			{
				_failed = true;
				Debug.LogError($"[ControllerSupport] Construction mode toggle failed, disabling it: {e}");
			}
		}
	}
}
