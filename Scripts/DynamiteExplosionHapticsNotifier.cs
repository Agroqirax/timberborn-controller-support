using Timberborn.Explosions;
using Timberborn.SingletonSystem;

namespace ControllerSupport
{
	// Dynamite.Detonate() posts DynamiteDetonatedEvent once per individual dynamite block - a real
	// public EventBus event, no Harmony patch needed. All 3 dynamite variants share this same
	// component (they only differ by DynamiteSpec's Depth/prefab), so this covers all of them.
	//
	// Dynamite ignites finished neighbouring dynamite on detonation (Dynamite.TriggerNeighbors), so a
	// line or field of them chain-explodes one after another rather than all at once. Pulse()
	// unconditionally resets GamepadHapticsController's remaining duration on every call, so a rapid
	// burst of detonations just keeps re-extending the rumble - it only starts counting down, and
	// eventually stops, after the last detonation in the chain. No extra bookkeeping needed here for
	// that to hold at any event count.
	internal class DynamiteExplosionHapticsNotifier : ILoadableSingleton, IUnloadableSingleton
	{
		private readonly EventBus _eventBus;

		public DynamiteExplosionHapticsNotifier(EventBus eventBus)
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
		public void OnDynamiteDetonated(DynamiteDetonatedEvent dynamiteDetonatedEvent)
		{
			// Matches UnstableCoreExplosionHapticsPatch's feel - both are explosions.
			GamepadHapticsController.Pulse(0.7f, 0.4f, 0.35f);
		}
	}
}
