using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace ControllerSupport
{
	// Remembers where the selection was in each scope the player has been through.
	//
	// Without this, every scope change starts from scratch: choosing a value from a dropdown dumps
	// the player back at the top of the settings page rather than on the dropdown they just set, and
	// backing out of a submenu loses the button they came in through. Restoring by element handles
	// the common case; restoring by position covers a scope that rebuilt its children while away.
	//
	// Scopes are held weakly - a closed panel should not be kept alive just because the mod recalls
	// standing in it.
	internal class SelectionMemory
	{
		// Deep enough for panel -> dropdown -> panel and a couple of nested menus, shallow enough
		// that stale entries age out on their own.
		private const int Capacity = 8;

		private readonly List<Entry> _entries = new List<Entry>();

		public void Remember(VisualElement scope, VisualElement element, Vector2 centre)
		{
			if (scope == null || element == null)
			{
				return;
			}

			var entry = Find(scope);
			if (entry == null)
			{
				entry = new Entry { Scope = new WeakReference<VisualElement>(scope) };
				_entries.Add(entry);
			}

			entry.Element = new WeakReference<VisualElement>(element);
			entry.Centre = centre;

			// Most-recently-used last, so trimming drops the scope untouched for longest.
			_entries.Remove(entry);
			_entries.Add(entry);
			if (_entries.Count > Capacity)
			{
				_entries.RemoveAt(0);
			}
		}

		// The element to re-select on returning to this scope, or null if there is nothing to go on.
		public VisualElement Restore(VisualElement scope, List<VisualElement> candidates)
		{
			if (scope == null || candidates.Count == 0)
			{
				return null;
			}

			var entry = Find(scope);
			if (entry == null)
			{
				return null;
			}

			if (entry.Element.TryGetTarget(out var element) && candidates.Contains(element))
			{
				return element;
			}

			// The scope rebuilt its children while we were away, so fall back to whatever now sits
			// closest to where the selection used to be.
			return SpatialNavigator.NearestTo(candidates, entry.Centre);
		}

		private Entry Find(VisualElement scope)
		{
			for (var i = _entries.Count - 1; i >= 0; i--)
			{
				if (!_entries[i].Scope.TryGetTarget(out var remembered))
				{
					_entries.RemoveAt(i);
					continue;
				}

				if (remembered == scope)
				{
					return _entries[i];
				}
			}

			return null;
		}

		private class Entry
		{
			public WeakReference<VisualElement> Scope;
			public WeakReference<VisualElement> Element;
			public Vector2 Centre;
		}
	}
}
