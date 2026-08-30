using Timberborn.HazardousWeatherSystemUI;
using Timberborn.SingletonSystem;

namespace ControllerSupport
{
	// HazardousWeatherApproachingEvent is posted once per cycle by
	// HazardousWeatherApproachingTimer.NotifyHazardousWeatherApproaching(), on the same day the base
	// game shows its own banner and plays the warning sound - a real public EventBus event, no
	// Harmony patch needed (unlike UnstableCoreExplosionHapticsPatch, which has to reach an internal
	// game type).
	internal class HazardousWeatherHapticsNotifier : ILoadableSingleton, IUnloadableSingleton
	{
		private readonly EventBus _eventBus;

		public HazardousWeatherHapticsNotifier(EventBus eventBus)
		{
			_eventBus = eventBus;
		}

		public void Load()
		{
			_eventBus.Register(this);
		}

		public void Unload()
		{
			_eventBus.Unregister(this);
		}

		[OnEvent]
		public void OnHazardousWeatherApproaching(HazardousWeatherApproachingEvent hazardousWeatherApproachingEvent)
		{
			GamepadHapticsController.Pulse(0.3f, 0.3f, 0.5f);
		}
	}
}
