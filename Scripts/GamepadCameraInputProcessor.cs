using System;
using Timberborn.CameraSystem;
using Timberborn.InputSystem;
using Timberborn.SingletonSystem;
using UnityEngine;
using UnityEngine.InputSystem;

// Both Timberborn and Unity ship an InputSettings; this file wants Timberborn's.
using InputSettings = Timberborn.InputSystem.InputSettings;

namespace ControllerSupport
{
	// Right stick pans the camera, leaving the left stick to the UI.
	//
	// Modelled on the game's own KeyboardCameraController: same speed formula, so the player's
	// "keyboard camera speed" setting still means something, and the same per-frame time cap so a
	// hitch cannot fling the camera across the map. The one deliberate difference is that the
	// movement vector is not normalised - a stick is analog, and a gentle push should pan gently.
	//
	// CameraService.MoveCameraBy already rotates the delta by the camera's horizontal angle, so
	// passing the stick straight through gives camera-relative panning for free.
	internal class GamepadCameraInputProcessor : ILoadableSingleton, IUnloadableSingleton, IInputProcessor
	{
		private const float Deadzone = 0.2f;
		private const float MaxFrameTime = 0.2f;
		private const float SpeedScale = 50f;
		private const float FailureLogInterval = 30f;

		private readonly InputService _inputService;
		private readonly CameraService _cameraService;
		private readonly InputSettings _inputSettings;
		private readonly PanelTracker _panelTracker;

		private float _nextFailureLogTime;

		public GamepadCameraInputProcessor(InputService inputService, CameraService cameraService,
			InputSettings inputSettings, PanelTracker panelTracker)
		{
			_inputService = inputService;
			_cameraService = cameraService;
			_inputSettings = inputSettings;
			_panelTracker = panelTracker;
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
				Pan();
			}
			catch (Exception e)
			{
				ReportFailure(e);
			}

			// Never consume: panning the camera should not stop anything else from seeing this frame,
			// exactly as the game's own camera controllers behave.
			return false;
		}

		private void Pan()
		{
			// While a menu or dialog is up the right stick scrolls it instead, so stand down rather than
			// panning the world out from behind the panel the player is reading.
			if (_panelTracker.HasStackedPanel)
			{
				return;
			}

			var gamepad = Gamepad.current;
			if (gamepad == null)
			{
				return;
			}

			var stick = gamepad.rightStick.ReadValue();
			var magnitude = stick.magnitude;
			if (magnitude < Deadzone)
			{
				return;
			}

			// Rescale past the deadzone so the pan eases in from a standstill instead of jumping to
			// deadzone speed the moment the stick registers.
			var throttle = (magnitude - Deadzone) / (1f - Deadzone);
			var direction = stick / magnitude;

			var speed = (_inputSettings.KeyboardCameraMovementSpeed * SpeedScale + 1f)
				* _cameraService.ZoomSpeedScale
				* Mathf.Min(Time.unscaledDeltaTime, MaxFrameTime);

			var delta = new Vector3(direction.x, 0f, direction.y) * (throttle * speed);
			_cameraService.MoveCameraBy(delta);
		}

		private void ReportFailure(Exception e)
		{
			var now = Time.unscaledTime;
			if (now < _nextFailureLogTime)
			{
				return;
			}

			_nextFailureLogTime = now + FailureLogInterval;
			Debug.LogError($"[ControllerSupport] Camera panning failed: {e}");
		}
	}
}
