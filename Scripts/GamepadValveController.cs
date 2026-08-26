using Timberborn.ConstructionSites;
using Timberborn.InputSystem;
using Timberborn.SelectionSystem;
using Timberborn.SingletonSystem;
using Timberborn.WaterBuildings;
using UnityEngine;

namespace ControllerSupport
{
	// ThrottlingValveFragment/FillValveFragment (Timberborn.WaterBuildingsUI) are purely mouse/slider-
	// driven - unlike FloodgateFragment, neither wires up any IInputProcessor or keybind at all, on
	// either keyboard or gamepad. This reuses IncreaseFloodgateHeight/DecreaseFloodgateHeight - the same
	// rebindable keybind FloodgateFragment itself drives its height slider with - rather than adding a
	// dedicated keybind: a single entity is never more than one of Floodgate/ThrottlingValve/FillValve
	// (confirmed against every building blueprint - each ships exactly one of
	// FloodgateSpec/ThrottlingValveSpec/FillValveSpec, never more than one), so there is no possible
	// collision from driving whichever slider actually applies to the current selection off the same
	// physical control.
	//
	// Both valve types share Floodgate's own "step past the max = disable/unlimited" slider convention -
	// ThrottlingValveFragment.SetOutflowLimit/FillValveFragment.SetTargetHeight both disable-and-clamp
	// once the requested value exceeds the max, and SetOutflowLimit/SetTargetHeightAndSynchronize are
	// mirrored here exactly. The fragments' own UpdateFragment re-reads the live component value into
	// the slider every tick unconditionally, so driving the component's public setters directly - the
	// same setters the fragments themselves call from their slider callbacks - keeps the on-screen
	// slider in sync with no extra work needed here.
	//
	// Re-registers on every selection change (see Requeue) rather than staying at a fixed position,
	// the same "jump to the front" trick GamepadNavigationInputProcessor uses for panels/tools (see
	// ARCHITECTURE.md). This matters here specifically because of TimeSpeedButtonGroup
	// (Timberborn.TimeSpeedButtonSystem) - its own ProcessInput reads IncreaseSpeed/DecreaseSpeed and
	// *always* returns false, win or lose, so it never blocks anything - but by the same token nothing
	// stops IT from firing either, unless something checked earlier in InputService's
	// last-registered-first walk claims the press first by returning true. FloodgateFragment already
	// wins that race for free, purely because ShowFragment (re)registers it fresh on every selection,
	// which is always more recent than TimeSpeedButtonGroup's one-time registration at HUD load. This
	// class has no such native ShowFragment hook to piggyback on, so it earns the same freshness
	// explicitly by re-registering itself on SelectableObjectSelectedEvent/
	// SelectableObjectUnselectedEvent - both posted by EntitySelectionService, and both also driving
	// EntityPanel's own fragment refresh.
	//
	// Construction deference is handled separately and explicitly (the IsUnderConstruction check
	// below), not by relying on registration order at all: unlike the Floodgate/TimeSpeedButtonGroup
	// relationship above, this class and ConstructionSiteFragment would both be reacting to the exact
	// same SelectableObjectSelectedEvent, on two independent EventBus subscribers - which one ends up
	// "more recent" afterward depends on EventBus subscriber registration order, which is exactly the
	// kind of implicit ordering that caused the original ConstructionSiteFragment/WorkplaceFragment bug
	// this mod already had to patch around (see ConstructionSiteFragmentFinishedPriorityPatch). An
	// explicit state check sidesteps that risk entirely rather than adding a second implicit-order bet.
	internal class GamepadValveController : ILoadableSingleton, IUnloadableSingleton, IInputProcessor
	{
		private const float ChangeTimeThreshold = 0.1f;

		// FillValveFragment.TargetHeightStep is a private field with no public equivalent on FillValve
		// itself - 0.05f mirrors it exactly (also the same value Floodgate's own HeightChangeStep uses).
		private const float FillValveTargetHeightStep = 0.05f;

		private const string DecreaseFloodgateHeightKey = "DecreaseFloodgateHeight";
		private const string IncreaseFloodgateHeightKey = "IncreaseFloodgateHeight";

		private readonly InputService _inputService;
		private readonly EntitySelectionService _entitySelectionService;
		private readonly PanelTracker _panelTracker;
		private readonly EventBus _eventBus;

		private float _timeSinceLastChange;
		private bool _changedOnHold;

		public GamepadValveController(InputService inputService, EntitySelectionService entitySelectionService,
			PanelTracker panelTracker, EventBus eventBus)
		{
			_inputService = inputService;
			_entitySelectionService = entitySelectionService;
			_panelTracker = panelTracker;
			_eventBus = eventBus;
		}

		public void Load()
		{
			_inputService.AddInputProcessor(this);
			_eventBus.Register(this);
		}

		public void Unload()
		{
			_eventBus.Unregister(this);
			_inputService.RemoveInputProcessor(this);
		}

		[OnEvent]
		public void OnSelectableObjectSelected(SelectableObjectSelectedEvent selectableObjectSelectedEvent)
		{
			Requeue();
		}

		[OnEvent]
		public void OnSelectableObjectUnselected(SelectableObjectUnselectedEvent selectableObjectUnselectedEvent)
		{
			Requeue();
		}

		private void Requeue()
		{
			_inputService.RemoveInputProcessor(this);
			_inputService.AddInputProcessor(this);
		}

		public bool ProcessInput()
		{
			if (_panelTracker.HasStackedPanel || !_entitySelectionService.IsAnythingSelected)
			{
				Reset();
				return false;
			}

			var selected = _entitySelectionService.SelectedObject;

			// Explicit, state-based deference rather than relying on registration order - see the class
			// comment. A valve under construction should leave the shared shoulders to
			// ConstructionSiteFragment's builder-priority controls, the same rule already applied to
			// floodgates (FloodgateFragmentUnderConstructionPatch).
			var constructionSite = selected.GetComponent<ConstructionSite>();
			if (constructionSite && constructionSite.Enabled)
			{
				Reset();
				return false;
			}

			var throttlingValve = selected.GetComponent<ThrottlingValve>();
			if (throttlingValve)
			{
				return ProcessThrottlingValve(throttlingValve);
			}

			var fillValve = selected.GetComponent<FillValve>();
			if (fillValve)
			{
				return ProcessFillValve(fillValve);
			}

			Reset();
			return false;
		}

		private bool ProcessThrottlingValve(ThrottlingValve valve)
		{
			var sliderMax = valve.MaxOutflowLimit + valve.OutflowLimitStep;
			var current = valve.OutflowLimitEnabled ? valve.OutflowLimit : sliderMax;

			return ProcessStep(
				decrease: () => SetOutflowLimit(valve, Mathf.Max(current - valve.OutflowLimitStep, 0f)),
				increase: () => SetOutflowLimit(valve, Mathf.Min(current + valve.OutflowLimitStep, sliderMax)));
		}

		private static void SetOutflowLimit(ThrottlingValve valve, float value)
		{
			if (value > valve.MaxOutflowLimit)
			{
				valve.SetOutflowLimitEnabledAndSynchronize(false);
				valve.SetOutflowLimitAndSynchronize(valve.MaxOutflowLimit);
			}
			else
			{
				valve.SetOutflowLimitEnabledAndSynchronize(true);
				valve.SetOutflowLimitAndSynchronize(value);
			}
		}

		private bool ProcessFillValve(FillValve valve)
		{
			var sliderMax = valve.MaxTargetHeight + FillValveTargetHeightStep;
			var current = valve.TargetHeightEnabled
				? Mathf.Clamp(valve.ClampedTargetHeight, valve.MinTargetHeight, valve.MaxTargetHeight)
				: sliderMax;

			return ProcessStep(
				decrease: () => SetTargetHeight(valve, Mathf.Max(current - FillValveTargetHeightStep, valve.MinTargetHeight)),
				increase: () => SetTargetHeight(valve, Mathf.Min(current + FillValveTargetHeightStep, sliderMax)));
		}

		private static void SetTargetHeight(FillValve valve, float value)
		{
			if (value > valve.MaxTargetHeight)
			{
				valve.SetTargetHeightEnabledAndSynchronize(false);
				valve.SetTargetHeightAndSynchronize(valve.MaxTargetHeight);
			}
			else
			{
				valve.SetTargetHeightEnabledAndSynchronize(true);
				valve.SetTargetHeightAndSynchronize(value);
			}
		}

		// Mirrors FloodgateFragment.ProcessInput's own hold-to-repeat timing exactly: an immediate step
		// on press, then repeated steps every ChangeTimeThreshold while held, with IsKeyUp guarding
		// against a double step on release of a press that already stepped on the initial hold check.
		private bool ProcessStep(System.Action decrease, System.Action increase)
		{
			if (_inputService.IsKeyHeld(DecreaseFloodgateHeightKey))
			{
				if (_timeSinceLastChange > ChangeTimeThreshold)
				{
					decrease();
					_changedOnHold = true;
					_timeSinceLastChange = 0f;
				}
				_timeSinceLastChange += Time.unscaledDeltaTime;
				return true;
			}
			if (_inputService.IsKeyUp(DecreaseFloodgateHeightKey) && !_changedOnHold)
			{
				decrease();
				return true;
			}
			if (_inputService.IsKeyHeld(IncreaseFloodgateHeightKey))
			{
				if (_timeSinceLastChange > ChangeTimeThreshold)
				{
					increase();
					_changedOnHold = true;
					_timeSinceLastChange = 0f;
				}
				_timeSinceLastChange += Time.unscaledDeltaTime;
				return true;
			}
			if (_inputService.IsKeyUp(IncreaseFloodgateHeightKey) && !_changedOnHold)
			{
				increase();
				return true;
			}

			Reset();
			return false;
		}

		private void Reset()
		{
			_timeSinceLastChange = 0f;
			_changedOnHold = false;
		}
	}
}
