using System.Collections.Generic;
using Timberborn.AssetSystem;
using Timberborn.KeyBindingSystem;
using Timberborn.SingletonSystem;
using UnityEngine;

namespace ControllerSupport
{
	// Bridges IAssetLoader (Bindito-injectable) out to GamepadShortcutIcon, a plain static helper
	// called from a Harmony postfix with no DI access of its own - same _instance pattern
	// GamepadHapticsController already established for this mod's static-patch/DI boundary.
	internal class GamepadIconRegistry : ILoadableSingleton
	{
		private const string SpriteFolder = "sprites/gamepadbuttons/";

		private static GamepadIconRegistry _instance;

		private readonly IAssetLoader _assetLoader;

		private readonly KeyBindingRegistry _keyBindingRegistry;

		private readonly EventBus _eventBus;

		private readonly Dictionary<string, Sprite> _cache = new();

		public GamepadIconRegistry(IAssetLoader assetLoader, KeyBindingRegistry keyBindingRegistry, EventBus eventBus)
		{
			_assetLoader = assetLoader;
			_keyBindingRegistry = keyBindingRegistry;
			_eventBus = eventBus;
		}

		public void Load()
		{
			_instance = this;

			// Any shortcut hint already built before this singleton finished loading (Bindito gives no
			// ordering guarantee between unrelated singleton chains, and IAssetLoader can be slower to
			// resolve than a plain UI panel) showed plain text on its first Update() and, absent this,
			// would only ever pick up an icon later if a gamepad connect/disconnect edge happens to fire
			// - same staleness problem RecentInputDeviceTracker.RefreshShortcutHints solves for the
			// text-vs-keyboard swap itself, solved the same way here.
			foreach (var keyBinding in _keyBindingRegistry.KeyBindings)
			{
				_eventBus.Post(new KeyReboundEvent(keyBinding.Id));
			}
		}

		// Sprite is null both when the key has no matching icon and before this singleton has loaded -
		// callers just fall back to text either way.
		internal static Sprite Get(string key)
		{
			return _instance?.GetSprite(key);
		}

		private Sprite GetSprite(string key)
		{
			var lookupKey = key.ToLowerInvariant();
			if (_cache.TryGetValue(lookupKey, out var sprite))
			{
				return sprite;
			}

			sprite = _assetLoader.LoadSafe<Sprite>(SpriteFolder + lookupKey);
			_cache[lookupKey] = sprite;
			return sprite;
		}
	}
}
