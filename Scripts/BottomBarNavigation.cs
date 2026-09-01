using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace ControllerSupport
{
	// BottomBarPanel builds the toolbar in two halves. The always-visible category buttons go into
	// "MainSection", and every category's row of tools goes into "SubSection", where all but the open
	// one are hidden. So confirming a category does not move the selection anywhere - a row just
	// appears above it - and the player is left pointing at the category they have already chosen,
	// one push away from closing it again.
	//
	// This is the geometry the generic navigator cannot infer: the row that appeared is the one thing
	// the player was asking for, and its first entry is the one they most likely want.
	internal static class BottomBarNavigation
	{
		private const string MainSectionName = "MainSection";
		private const string SubSectionName = "SubSection";
		private const float Tolerance = 2f;

		// The container holding every category's tool row, or null if this element is not a bottom bar
		// category to begin with.
		public static VisualElement SubSectionFor(VisualElement element)
		{
			var mainSection = AncestorNamed(element, MainSectionName);
			if (mainSection == null)
			{
				return null;
			}

			// Starting above MainSection rather than at it, since the two are separate branches of the
			// bottom bar and only their shared ancestor can see both.
			for (var current = mainSection.hierarchy.parent; current != null; current = current.hierarchy.parent)
			{
				var subSection = current.Q<VisualElement>(SubSectionName);
				if (subSection != null)
				{
					return subSection;
				}
			}

			return null;
		}

		// True once the player has actually moved onto a tool inside an OPEN category's row - unlike
		// SubSectionFor above, which answers "is this element a bottom-bar category button at all" and
		// returns non-null for a MainSection button whether or not its row has ever been opened.
		// GamepadHintResolver needs this stronger distinction: B only usefully closes a category's row
		// once one is actually open, not merely because the ring happens to be sitting on a category
		// that could open one - conflating the two made the Cancel hint show while just browsing
		// MainSection with nothing open (reported 2026-08-31, "it's just when the cursor is on the
		// bottombar").
		public static bool IsWithinOpenSubSection(VisualElement element)
		{
			return AncestorNamed(element, SubSectionName) != null;
		}

		// Where the cursor should start out on the bare HUD: the leftmost category in the always-visible
		// row, which is the cursor tool in the Game scene. MainSection only shows up among the
		// candidates at all when the scope is the bare HUD - a stacked panel's own scope never contains
		// it - so this naturally has nothing to say in a menu, a dialog or a dropdown, and needs no
		// scene check to stay out of their way.
		public static VisualElement DefaultTool(List<VisualElement> candidates)
		{
			foreach (var candidate in candidates)
			{
				var mainSection = AncestorNamed(candidate, MainSectionName);
				if (mainSection != null)
				{
					return Leftmost(candidates, mainSection);
				}
			}

			return null;
		}

		// The tools a player reaches for most sit at the left of the row, so that is where to land.
		public static VisualElement Leftmost(List<VisualElement> candidates, VisualElement subSection)
		{
			VisualElement best = null;
			var bestX = 0f;
			var bestY = 0f;

			foreach (var candidate in candidates)
			{
				if (!subSection.Contains(candidate))
				{
					continue;
				}

				var bound = candidate.worldBound;
				var isBetter = best == null
					|| bound.xMin < bestX - Tolerance
					|| (bound.xMin < bestX + Tolerance && bound.yMin < bestY);

				if (isBetter)
				{
					best = candidate;
					bestX = bound.xMin;
					bestY = bound.yMin;
				}
			}

			return best;
		}

		// SpatialNavigator deliberately does not wrap - see ARCHITECTURE.md, it used to and that read as
		// broken in a two-row toolbar, where up/down from the top row landed on the bottom one instead of
		// stopping. But MainSection and SubSection are each a single row, so left/right wrapping there
		// carries none of that ambiguity - called only as a fallback once SpatialNavigator.Next has
		// already come back empty for a horizontal push.
		public static VisualElement WrapHorizontal(List<VisualElement> candidates, VisualElement current, Vector2Int direction)
		{
			if (current == null || direction.x == 0 || direction.y != 0)
			{
				return null;
			}

			var bar = NearestBarSection(current);
			if (bar == null)
			{
				return null;
			}

			VisualElement best = null;
			var bestX = 0f;

			foreach (var candidate in candidates)
			{
				if (ReferenceEquals(candidate, current) || !bar.Contains(candidate))
				{
					continue;
				}

				var x = candidate.worldBound.xMin;
				var isBetter = best == null
					|| (direction.x > 0 && x < bestX - Tolerance)
					|| (direction.x < 0 && x > bestX + Tolerance);

				if (isBetter)
				{
					best = candidate;
					bestX = x;
				}
			}

			return best;
		}

		// The nearest MainSection or SubSection ancestor - whichever bar `element` actually belongs to.
		private static VisualElement NearestBarSection(VisualElement element)
		{
			for (var current = element; current != null; current = current.hierarchy.parent)
			{
				if (current.name == MainSectionName || current.name == SubSectionName)
				{
					return current;
				}
			}

			return null;
		}

		private static VisualElement AncestorNamed(VisualElement element, string name)
		{
			for (var current = element; current != null; current = current.hierarchy.parent)
			{
				if (current.name == name)
				{
					return current;
				}
			}

			return null;
		}
	}
}
