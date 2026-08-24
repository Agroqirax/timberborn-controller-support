using System;
using Timberborn.CameraSystem;
using Timberborn.InputSystem;
using Timberborn.KeyBindingSystem;
using Timberborn.SingletonSystem;
using UnityEngine;
using UnityEngine.InputSystem;

// Both Timberborn and Unity ship an InputSettings; this file wants Timberborn's.
using InputSettings = Timberborn.InputSystem.InputSettings;

namespace ControllerSupport
{
	// Right stick pans the camera, leaving the left stick to the UI. Holding the right stick down
	// (R3) switches it to rotating the camera instead - the gamepad equivalent of holding RMB and
	// dragging the mouse, which is MouseCameraController.RotateCamera's job on mouse+keyboard.
	//
	// Panning is modelled on the game's own KeyboardCameraController: same speed formula, so the
	// player's "keyboard camera speed" setting still means something, and the same per-frame time
	// cap so a hitch cannot fling the camera across the map. The one deliberate difference is that
	// the movement vector is not normalised - a stick is analog, and a gentle push should pan gently.
	//
	// Rotation is modelled on the same file's RotationUpdate rather than MouseCameraController's -
	// that one turns a per-frame *axis value* (its own analog range is effectively -1..1, same as a
	// stick) into an angle delta, whereas MouseCameraController.RotateCamera consumes raw per-frame
	// mouse-pixel deltas, which are a different unit range entirely and would need re-tuning to feel
	// right from a stick.
	//
	// CameraService.MoveCameraBy already rotates the delta by the camera's horizontal angle, so
	// passing the stick straight through gives camera-relative panning for free.
	internal class GamepadCameraInputProcessor : ILoadableSingleton, IUnloadableSingleton, IInputProcessor
	{
		private const float Deadzone = 0.2f;
		private const float MaxFrameTime = 0.2f;
		private const float SpeedScale = 50f;
		private const float RotationSpeedScale = 175f;
		private const float FailureLogInterval = 30f;

		private readonly InputService _inputService;
		private readonly CameraService _cameraService;
		private readonly InputSettings _inputSettings;
		private readonly PanelTracker _panelTracker;
		private readonly KeyBindingRegistry _keyBindingRegistry;

		private float _nextFailureLogTime;

		public GamepadCameraInputProcessor(InputService inputService, CameraService cameraService,
			InputSettings inputSettings, PanelTracker panelTracker, KeyBindingRegistry keyBindingRegistry)
		{
			_inputService = inputService;
			_cameraService = cameraService;
			_inputSettings = inputSettings;
			_panelTracker = panelTracker;
			_keyBindingRegistry = keyBindingRegistry;
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
				var gamepad = Gamepad.current;
				if (gamepad != null && !_panelTracker.HasStackedPanel)
				{
					if (gamepad.rightStickButton.isPressed)
					{
						Rotate();
					}
					else
					{
						Pan();
					}
				}
			}
			catch (Exception e)
			{
				ReportFailure(e);
			}

			// Never consume: panning/rotating the camera should not stop anything else from seeing this
			// frame, exactly as the game's own camera controllers behave.
			return false;
		}

		// Deadzone-rescaled analog direction and throttle for the right stick, or null under the
		// deadzone. Shared between Pan and Rotate so easing in from a standstill behaves the same way
		// for both.
		private bool TryReadStick(out Vector2 direction, out float throttle)
		{
			var stick = GamepadAxis.Read(_keyBindingRegistry, GamepadAxis.RightStick);
			var magnitude = stick.magnitude;
			if (magnitude < Deadzone)
			{
				direction = Vector2.zero;
				throttle = 0f;
				return false;
			}

			throttle = (magnitude - Deadzone) / (1f - Deadzone);
			direction = stick / magnitude;
			return true;
		}

		private void Pan()
		{
			if (!TryReadStick(out var direction, out var throttle))
			{
				return;
			}

			var speed = (_inputSettings.KeyboardCameraMovementSpeed * SpeedScale + 1f)
				* _cameraService.ZoomSpeedScale
				* Mathf.Min(Time.unscaledDeltaTime, MaxFrameTime);

			var delta = new Vector3(direction.x, 0f, direction.y) * (throttle * speed);
			_cameraService.MoveCameraBy(delta);
		}

		// R3 held: the stick orbits the camera instead of panning it, same trade MouseCameraController
		// makes for RMB-held-and-dragging. Both axes are negated relative to the mouse version -
		// dragging-the-world reads fine on a mouse (push right to drag the world left, i.e. look
		// right) but feels backwards on a stick, where the expected convention is "the stick points
		// where the camera looks" (flight-stick / third-person-camera style). So stick right looks
		// right, stick up looks up.
		private void Rotate()
		{
			if (!TryReadStick(out var direction, out var throttle))
			{
				return;
			}

			var speed = (_inputSettings.KeyboardCameraRotationSpeed * RotationSpeedScale + 1f)
				* Mathf.Min(Time.unscaledDeltaTime, MaxFrameTime);

			_cameraService.ModifyHorizontalAngle(-direction.x * throttle * speed);
			_cameraService.ModifyVerticalAngle(direction.y * throttle * speed);
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
