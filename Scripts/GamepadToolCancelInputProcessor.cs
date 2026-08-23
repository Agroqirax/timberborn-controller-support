using System;
using Timberborn.InputSystem;
using Timberborn.SingletonSystem;
using Timberborn.ToolSystem;
using Timberborn.UISound;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ControllerSupport
{
	// Gives B something to do when there is no dialog to close.
	//
	// Escape does three separate jobs in the game scene, and the panel stack only knows about the
	// first. ToolService and ToolGroupService each register themselves as input processors watching
	// InputService.Cancel: one drops back to the default tool, the other closes the open row of the
	// bottom bar. Neither ever sees a gamepad press, so B used to do nothing at all out in the world.
	//
	// The old build faked this by injecting an Escape key press into the real keyboard device, which
	// also wiped whatever the player was genuinely holding down. Both services are public; calling
	// them is exact and has no side effects.
	//
	// Registered separately from the navigation processor because ToolService does not exist in the
	// main menu, and ordered behind it - the navigation processor re-registers itself to the front on
	// every panel change and reports B as handled whenever it closes something, so this only ever runs
	// once the dialogs have had their turn. The panel check is belt and braces for the frames where
	// that ordering has not settled yet.
	internal class GamepadToolCancelInputProcessor : ILoadableSingleton, IUnloadableSingleton, IInputProcessor
	{
		private const float FailureLogInterval = 30f;

		private readonly InputService _inputService;
		private readonly PanelTracker _panelTracker;
		private readonly ToolService _toolService;
		private readonly ToolGroupService _toolGroupService;
		private readonly UISoundController _uiSoundController;

		private float _nextFailureLogTime;

		public GamepadToolCancelInputProcessor(InputService inputService, PanelTracker panelTracker,
			ToolService toolService, ToolGroupService toolGroupService, UISoundController uiSoundController)
		{
			_inputService = inputService;
			_panelTracker = panelTracker;
			_toolService = toolService;
			_toolGroupService = toolGroupService;
			_uiSoundController = uiSoundController;
		}

		public void Load()
		{
			_inputService.AddInputProcessor(this);
		}

		public void Unload()
		{
			_inputService.RemoveInputProcessor(this);
		}

		public bool ProcessInput()
		{
			try
			{
				return Cancel();
			}
			catch (Exception e)
			{
				ReportFailure(e);
				return false;
			}
		}

		private bool Cancel()
		{
			var gamepad = Gamepad.current;
			if (gamepad == null || !gamepad.buttonEast.wasPressedThisFrame || _panelTracker.HasStackedPanel)
			{
				return false;
			}

			// Same order Escape unwinds in: put the tool down first, and only then close the row it
			// came from. Pressing B twice from a half-placed building gets you all the way out.
			if (!_toolService.IsDefaultToolActive)
			{
				_toolService.SwitchToDefaultTool();
				_uiSoundController.PlayCancelSound();
				return true;
			}

			if (_toolGroupService.ActiveToolGroup != null)
			{
				_toolGroupService.ExitToolGroup();
				_uiSoundController.PlayCancelSound();
				return true;
			}

			return false;
		}

		private void ReportFailure(Exception e)
		{
			var now = Time.unscaledTime;
			if (now < _nextFailureLogTime)
			{
				return;
			}

			_nextFailureLogTime = now + FailureLogInterval;
			Debug.LogError($"[ControllerSupport] Tool cancel failed: {e}");
		}
	}
}
