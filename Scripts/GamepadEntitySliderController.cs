using Timberborn.AutomationBuildings;
using Timberborn.ConstructionSites;
using Timberborn.InputSystem;
using Timberborn.SelectionSystem;
using Timberborn.SingletonSystem;
using Timberborn.WaterBuildings;
using UnityEngine;

namespace ControllerSupport
{
	// Drives whichever single-slider control the selected entity's panel is currently showing, off the
	// same shoulders FloodgateFragment itself uses (IncreaseFloodgateHeight/DecreaseFloodgateHeight).
	// Started out valve-only (see git history) and grew one building at a time as each turned out to
	// have exactly the same shape: a fragment with one PreciseSlider and no IInputProcessor of its own.
	//
	// The keybind Ids themselves cannot be renamed to something less floodgate-specific despite covering
	// far more than floodgates now - FloodgateFragment (sealed, base-game) reads those two literal
	// strings itself (see ARCHITECTURE.md's keybinding-override section), so retargeting the Id would
	// silently break the one consumer that owns it natively. Reusing it is still correct: a single
	// entity only ever shows one of these fragments' sliders at a time (confirmed per-type below), so
	// there is no possible collision from driving whichever slider actually applies off the same
	// physical control - same reasoning as the original valve-only version.
	//
	// Every building here follows Floodgate's own "the fragment renders its own state every tick, this
	// class only calls the same public setters the slider's own callback would" pattern - see
	// WaterMoverFragment/DepthSensorFragment/FlowSensorFragment/ContaminationSensorFragment/
	// PowerMeterFragment/ResourceCounterFragment/WeatherStationFragment for the values mirrored below
	// (step sizes, clamping) to keep this class's steps landing on the same values the mouse-driven
	// slider would produce.
	//
	// Left out on purpose: Chronometer (Start/End sliders are visible simultaneously in TimeRange mode,
	// so a single pair of shoulders has no way to say which one it means) and FireworkLauncher (three
	// simultaneous sliders - Heading/Pitch/FlightDistance). Both stay reachable through ordinary cursor
	// navigation and the slider's own left/right step (ControlActivator.TryAdjustSlider); they just don't
	// get the always-available shoulder shortcut. PowerMeter's IntThreshold and ResourceCounter's
	// Threshold are IntegerFields, not sliders, and are handled by the general control-navigation fix
	// instead (see ControlActivator/NavigationCandidates).
	//
	// Re-registers on every selection change (see Requeue) rather than staying at a fixed position - see
	// ARCHITECTURE.md's "jump to the front" note and TimeSpeedButtonGroup's race with FloodgateFragment,
	// which this class has to win the same way since it has no native ShowFragment hook to piggyback on.
	//
	// Construction deference is explicit (the IsUnderConstruction check below), not order-based - see
	// ConstructionSiteFragmentFinishedPriorityPatch for why implicit EventBus subscriber ordering is not
	// trusted here.
	internal class GamepadEntitySliderController : ILoadableSingleton, IUnloadableSingleton, IInputProcessor
	{
		private const float ChangeTimeThreshold = 0.1f;

		// FillValveFragment.TargetHeightStep is a private field with no public equivalent on FillValve
		// itself - 0.05f mirrors it exactly (also the same value Floodgate's own HeightChangeStep uses,
		// and DepthSensorFragment's own ThresholdChangeStep).
		private const float FillValveTargetHeightStep = 0.05f;
		private const float DepthThresholdStep = 0.05f;

		// WaterMoverFragment.InitializeFragment's own SetStepWithoutNotify(0.01f) for the pump's flow
		// rate slider - no public constant on WaterMover to reuse.
		private const float FlowRateStep = 0.01f;

		// PowerMeterFragment/ResourceCounterFragment's own PercentThresholdStep/PercentThresholdChangeStep
		// - both 0.01f, no public constant on PowerMeter/ResourceCounter to reuse.
		private const float PercentThresholdStep = 0.01f;

		private const string DecreaseFloodgateHeightKey = "DecreaseFloodgateHeight";
		private const string IncreaseFloodgateHeightKey = "IncreaseFloodgateHeight";

		private readonly InputService _inputService;
		private readonly EntitySelectionService _entitySelectionService;
		private readonly PanelTracker _panelTracker;
		private readonly EventBus _eventBus;

		private float _timeSinceLastChange;
		private bool _changedOnHold;

		public GamepadEntitySliderController(InputService inputService, EntitySelectionService entitySelectionService,
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

		// Mirrors ProcessInput's own component/mode cascade below, minus the construction-site deference
		// (a caller comparing this against other shoulder-slot meanings by specificity, e.g.
		// GamepadHintResolver, already encodes that precedence itself) and minus actually driving
		// anything - just "would one of these fragments' sliders show for this selection right now".
		// Kept as a separate method rather than folding into ProcessInput so a hint-resolver caller
		// doesn't have to fabricate the decrease/increase actions actually stepping the slider.
		internal static bool HasApplicableSlider(SelectableObject selected)
		{
			// A real Floodgate is deliberately NOT one of the types ProcessInput drives below -
			// FloodgateFragment (base game) already owns IncreaseFloodgateHeight/DecreaseFloodgateHeight
			// natively for actual floodgates, this controller only reuses that same physical shoulder
			// pair for OTHER buildings. This method only answers "would these two keys drive some
			// fragment's slider right now", and for a real floodgate that's still true, just via a
			// different owner - omitting it here was a bug (reported 2026-08-31: selecting a floodgate
			// fell through to the "nothing selected" Speed hint instead of showing Adjust).
			if (selected.GetComponent<Floodgate>())
			{
				return true;
			}

			if (selected.GetComponent<ThrottlingValve>() || selected.GetComponent<FillValve>()
				|| selected.GetComponent<WaterMover>() || selected.GetComponent<DepthSensor>()
				|| selected.GetComponent<FlowSensor>() || selected.GetComponent<ContaminationSensor>())
			{
				return true;
			}

			var powerMeter = selected.GetComponent<PowerMeter>();
			if (powerMeter && powerMeter.IsPercentThreshold)
			{
				return true;
			}

			var resourceCounter = selected.GetComponent<ResourceCounter>();
			if (resourceCounter && resourceCounter.Mode == ResourceCounterMode.FillRate)
			{
				return true;
			}

			var weatherStation = selected.GetComponent<WeatherStation>();
			return weatherStation && weatherStation.EarlyActivationEnabled;
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
			// comment. A building under construction should leave the shared shoulders to
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

			var waterMover = selected.GetComponent<WaterMover>();
			if (waterMover)
			{
				return ProcessWaterMover(waterMover);
			}

			var depthSensor = selected.GetComponent<DepthSensor>();
			if (depthSensor)
			{
				return ProcessDepthSensor(depthSensor);
			}

			var flowSensor = selected.GetComponent<FlowSensor>();
			if (flowSensor)
			{
				return ProcessFlowSensor(flowSensor);
			}

			var contaminationSensor = selected.GetComponent<ContaminationSensor>();
			if (contaminationSensor)
			{
				return ProcessContaminationSensor(contaminationSensor);
			}

			// Only when PowerMeterFragment is actually showing its slider - IntThreshold mode shows an
			// IntegerField instead (see the class comment).
			var powerMeter = selected.GetComponent<PowerMeter>();
			if (powerMeter && powerMeter.IsPercentThreshold)
			{
				return ProcessPowerMeter(powerMeter);
			}

			// Only when ResourceCounterFragment is actually showing its slider - StockLevel mode shows an
			// IntegerField instead.
			var resourceCounter = selected.GetComponent<ResourceCounter>();
			if (resourceCounter && resourceCounter.Mode == ResourceCounterMode.FillRate)
			{
				return ProcessResourceCounter(resourceCounter);
			}

			// Only when WeatherStationFragment is actually showing its slider - it is hidden entirely
			// while early activation is off.
			var weatherStation = selected.GetComponent<WeatherStation>();
			if (weatherStation && weatherStation.EarlyActivationEnabled)
			{
				return ProcessWeatherStation(weatherStation);
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

		// WaterMover.SetFlowRate takes an unclamped value straight from the slider callback - no enabled/
		// disabled "unlimited past the max" concept here, unlike the two valves above.
		private bool ProcessWaterMover(WaterMover waterMover)
		{
			var current = waterMover.FlowRate;
			var max = waterMover.MaxFlowRate;

			return ProcessStep(
				decrease: () => waterMover.SetFlowRate(Mathf.Max(current - FlowRateStep, 0f)),
				increase: () => waterMover.SetFlowRate(Mathf.Min(current + FlowRateStep, max)));
		}

		private bool ProcessDepthSensor(DepthSensor sensor)
		{
			var current = sensor.Threshold;
			var min = sensor.MinThreshold;
			var max = sensor.MaxThreshold;

			return ProcessStep(
				decrease: () => sensor.SetThreshold(Mathf.Max(current - DepthThresholdStep, min)),
				increase: () => sensor.SetThreshold(Mathf.Min(current + DepthThresholdStep, max)));
		}

		private bool ProcessFlowSensor(FlowSensor sensor)
		{
			var current = sensor.Threshold;
			var max = sensor.MaxThreshold;

			return ProcessStep(
				decrease: () => sensor.SetThreshold(Mathf.Max(current - FlowSensor.Precision, 0f)),
				increase: () => sensor.SetThreshold(Mathf.Min(current + FlowSensor.Precision, max)));
		}

		private bool ProcessContaminationSensor(ContaminationSensor sensor)
		{
			var current = sensor.Threshold;

			return ProcessStep(
				decrease: () => sensor.SetThreshold(Mathf.Max(current - ContaminationSensor.Precision, 0f)),
				increase: () => sensor.SetThreshold(Mathf.Min(current + ContaminationSensor.Precision, 1f)));
		}

		private bool ProcessPowerMeter(PowerMeter powerMeter)
		{
			var current = powerMeter.PercentThreshold;

			return ProcessStep(
				decrease: () => powerMeter.SetPercentThreshold(Mathf.Max(current - PercentThresholdStep, 0f)),
				increase: () => powerMeter.SetPercentThreshold(Mathf.Min(current + PercentThresholdStep, 1f)));
		}

		private bool ProcessResourceCounter(ResourceCounter resourceCounter)
		{
			var current = resourceCounter.FillRateThreshold;

			return ProcessStep(
				decrease: () => resourceCounter.SetFillRateThreshold(Mathf.Max(current - PercentThresholdStep, 0f)),
				increase: () => resourceCounter.SetFillRateThreshold(Mathf.Min(current + PercentThresholdStep, 1f)));
		}

		// WeatherStation.SetEarlyActivationHours takes an int - WeatherStationFragment's own slider
		// callback rounds the same way (Mathf.RoundToInt) before calling it.
		private bool ProcessWeatherStation(WeatherStation weatherStation)
		{
			var current = weatherStation.EarlyActivationHours;
			var max = weatherStation.MaxEarlyActivationHours;

			return ProcessStep(
				decrease: () => weatherStation.SetEarlyActivationHours(Mathf.Max(current - 1, 0)),
				increase: () => weatherStation.SetEarlyActivationHours(Mathf.Min(current + 1, Mathf.RoundToInt(max))));
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
