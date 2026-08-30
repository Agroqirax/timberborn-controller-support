using Timberborn.SingletonSystem;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ControllerSupport
{
	// Shared dispatcher for every gamepad haptic pulse this mod triggers - a Harmony patch calling
	// GamepadHapticsController.Pulse(...) is meant to be the pattern for every future haptic event,
	// not something specific to explosions. Static Harmony patches can't receive Bindito's
	// constructor injection, so this class caches its own DI'd instance in Load() the way
	// GamepadPlacementState carries live state from an injected controller out to static patch code
	// elsewhere in this mod - here as a method call rather than raw fields.
	internal class GamepadHapticsController : ILoadableSingleton, IUpdatableSingleton
	{
		private static GamepadHapticsController _instance;

		private readonly ControllerHapticsSettings _settings;

		private float _remainingSeconds;

		public GamepadHapticsController(ControllerHapticsSettings settings)
		{
			_settings = settings;
		}

		public void Load()
		{
			_instance = this;
		}

		internal static void Pulse(float lowFrequency, float highFrequency, float durationSeconds)
		{
			if (_instance == null || !_instance._settings.EnableVibration.Value)
			{
				return;
			}
			_instance._remainingSeconds = durationSeconds;
			Gamepad.current?.SetMotorSpeeds(lowFrequency, highFrequency);
		}

		public void UpdateSingleton()
		{
			if (_remainingSeconds <= 0f)
			{
				return;
			}
			// unscaledDeltaTime, not deltaTime: SpeedManager drives game speed through Time.timeScale,
			// including setting it to 0 while a speed-locking overlay panel is up (e.g.
			// WonderCompletionPanel, pushed via PanelStack.PushOverlay's default lockSpeed: true) - a
			// pulse timed with deltaTime would freeze at full strength for as long as such a panel is
			// shown, then finish decaying once the player dismisses it. The rumble is a real-world
			// effect on hardware in the player's hands, so it should always track wall-clock time
			// regardless of game pause or speed multiplier.
			_remainingSeconds -= Time.unscaledDeltaTime;
			if (_remainingSeconds <= 0f)
			{
				Gamepad.current?.SetMotorSpeeds(0f, 0f);
			}
		}
	}
}
