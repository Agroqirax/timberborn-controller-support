using System.Collections.Generic;
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
