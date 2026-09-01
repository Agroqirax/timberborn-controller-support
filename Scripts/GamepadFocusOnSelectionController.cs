using Timberborn.InputSystem;
using Timberborn.SelectionSystem;
using Timberborn.SingletonSystem;

namespace ControllerSupport
{
	// Left-stick click (KeyBinding.FocusOnSelection.blueprint.json, <Gamepad>/leftStickPress) does
	// exactly what the entity panel's own Focus button does - that button is
	// Timberborn.EntityPanelSystem.FollowObjectFragment (tooltip loc key "EntityPanel.Focus"), and its
	// click handler calls EntitySelectionService.SelectAndFollow, not SelectAndFocusOn. The two look
	// similar but only SelectAndFollow actually recentres the camera through CameraTargeter.Follow.
	//
	// IPriorityInputProcessor, not IUpdatableSingleton - confirmed by logging (2026-09-01) that
	// KeyBindingRegistry.IsDown's pulse never reached a plain IUpdatableSingleton here:
	// InputService.UpdateSingleton itself calls CallInputProcessors() BEFORE InputUpdater.Update()
	// recomputes IsDown/IsHeld for the frame, and this class's own UpdateSingleton was landing after
	// InputService's in the (unordered) IUpdatableSingleton list - by the time it read IsDown, that
	// frame's pulse had already been superseded. Every other gamepad controller in this mod already
	// reads input via ProcessInput (called from inside CallInputProcessors, ahead of that
	// recomputation) for exactly this reason; this class just hadn't followed suit yet.
	internal class GamepadFocusOnSelectionController : ILoadableSingleton, IPriorityInputProcessor
	{
		private const string FocusOnSelectionKey = "FocusOnSelection";

		private readonly InputService _inputService;
		private readonly EntitySelectionService _entitySelectionService;

		public GamepadFocusOnSelectionController(InputService inputService,
			EntitySelectionService entitySelectionService)
		{
			_inputService = inputService;
			_entitySelectionService = entitySelectionService;
		}

		public void Load()
		{
			_inputService.AddInputProcessor(this);
		}

		public void ProcessInput()
		{
			if (!_inputService.IsKeyDown(FocusOnSelectionKey))
			{
				return;
			}

			if (_entitySelectionService.IsAnythingSelected)
			{
				_entitySelectionService.SelectAndFollow(_entitySelectionService.SelectedObject);
			}
		}
	}
}
