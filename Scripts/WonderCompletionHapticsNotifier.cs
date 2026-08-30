using Timberborn.GameWonderCompletion;
using Timberborn.SingletonSystem;

namespace ControllerSupport
{
	// WonderCompletedEvent is posted once by WonderCompletionCountdownStarter.Tick() when a wonder's
	// unlock countdown finishes - a real public EventBus event, no Harmony patch needed.
	internal class WonderCompletionHapticsNotifier : ILoadableSingleton, IUnloadableSingleton
	{
		private readonly EventBus _eventBus;

		public WonderCompletionHapticsNotifier(EventBus eventBus)
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
		public void OnWonderCompleted(WonderCompletedEvent wonderCompletedEvent)
		{
			GamepadHapticsController.Pulse(0.5f, 0.6f, 0.6f);
		}
	}
}
