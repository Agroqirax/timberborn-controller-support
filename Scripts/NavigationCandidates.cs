using System.Collections.Generic;
using UnityEngine.UIElements;

namespace ControllerSupport
{
	// Collects the elements a player can actually aim at inside a panel.
	//
	// This is a single depth-first walk that prunes as it goes, replacing the previous
	// "query every element, then filter pairwise" pass which was quadratic in the number of hits.
	// Two rules do most of the work:
	//
	//   - A hidden or disabled subtree is skipped whole, so invisible panels never contribute.
	//   - A container only becomes a candidate if nothing inside it already is. That is what keeps
	//     clickable wrappers from sitting between the player and the real controls.
	internal static class NavigationCandidates
	{
		private const float MinimumSize = 1f;
		private const string ExtensionTogglerName = "ExtensionToggler";

		public static void Collect(VisualElement scope, List<VisualElement> into)
		{
			into.Clear();
			if (scope != null)
			{
				CollectFrom(scope, into);
			}
		}

		// Returns whether this subtree contributed a candidate.
		private static bool CollectFrom(VisualElement element, List<VisualElement> into)
		{
			// A Scroller holds the scrollbar's RepeatButtons and dragger. They are clickable and are
			// descendants of the ScrollView, but they sit outside its content-container, so they show
			// up as selections the player cannot see and make ScrollTo throw.
			if (element is Scroller || IsRedundant(element) || !IsDisplayed(element))
			{
				return false;
			}

			// Controls are leaves as far as navigation is concerned. Descending into one would let its
			// internal parts - a clickable label, a slider's dragger and track - each become their own
			// candidate, scattering a single control across several selectable positions.
			//
			// A Button is the one exception: some templates use a Button purely for its 9-slice
			// background, with no handler of its own, and either leave it inert (StockpilePriorityFragment's
			// "Background" behind the four real toggle buttons - selecting it did nothing, but nothing
			// stopped it from being a candidate) or nest the real clickable Button inside it
			// (GoodSelectionBoxItemFactory's item root is itself a handler-less NineSliceButton wrapping
			// the actual "GoodSelectionBoxItem" button - confirming grabbed the outer shell and the click
			// went nowhere). Only descend past a Button when it fails this check; every other IsControl
			// type is activated by ControlActivator through its own type-specific path rather than a raw
			// ClickEvent handler, so the same check would wrongly exclude a perfectly real Toggle/Slider.
			if (ControlActivator.IsControl(element) && !IsInertButton(element))
			{
				if (IsBigEnough(element))
				{
					into.Add(element);
					return true;
				}

				return false;
			}

			var found = false;
			var children = element.hierarchy;
			for (var i = 0; i < children.childCount; i++)
			{
				// Deliberately not short-circuiting: every child still needs collecting.
				found |= CollectFrom(children[i], into);
			}

			if (found)
			{
				return true;
			}

			// A ListView whose rows are not independently clickable. The save list registers a ClickEvent
			// per row and so is already covered above; the settlement list next to it does not - it leaves
			// selection entirely to the ListView - so without this it offered nothing to aim at at all.
			//
			// Each realised row becomes its own candidate rather than the list as a whole. A single
			// candidate spanning the whole list has to intercept every up/down push to drive selectedIndex,
			// which only lets the player leave from the top or bottom row - fine for a list that is the
			// whole point of the panel, wrong for one like the mod uploader's, where the button the player
			// actually wants sits right below it and was only reachable past the last mod. Row-level
			// candidates let ordinary spatial navigation leave from wherever the player currently is, the
			// same way it already does for the save list.
			if (element is BaseVerticalCollectionView collectionView && IsBigEnough(element))
			{
				return CollectRows(collectionView, into);
			}

			// Anything that registered a ClickEvent callback counts too. That is what brings list
			// rows - saves, maps, mods - into the rotation, since they are plain VisualElements built
			// by a factory rather than Buttons.
			if (IsBigEnough(element) && VisualElementProbe.HasClickHandler(element))
			{
				into.Add(element);
				return true;
			}

			return false;
		}

		// A Button with no click handler of its own - see the comment above IsControl's use in
		// CollectFrom. Reusing VisualElementProbe here (rather than trusting IsControl alone) is what
		// tells a real interactive Button apart from one that only exists for its 9-slice background.
		private static bool IsInertButton(VisualElement element)
		{
			return element is Button && !VisualElementProbe.HasClickHandler(element);
		}

		// Only the rows the ListView has actually realised come back non-null - virtualisation keeps
		// off-screen ones out of the tree entirely. That is exactly what is wanted: as the player
		// scrolls, newly realised rows join the candidate list on the next collect the same way any
		// other rebuilt UI does.
		private static bool CollectRows(BaseVerticalCollectionView collectionView, List<VisualElement> into)
		{
			var source = collectionView.itemsSource;
			if (source == null)
			{
				return false;
			}

			var found = false;
			for (var i = 0; i < source.Count; i++)
			{
				var row = collectionView.GetRootElementForIndex(i);
				if (row != null && IsBigEnough(row))
				{
					into.Add(row);
					found = true;
				}
			}

			return found;
		}

		// Controls that duplicate an action already reachable from a bigger, easier target next to
		// them. TopBarCounterFactory wires the same ToggleVisibility onto both the counter box and the
		// little arrow underneath it, so collecting both puts two stops where the player sees one thing
		// to aim at - and the arrow is the fiddlier half.
		private static bool IsRedundant(VisualElement element)
		{
			return element.name == ExtensionTogglerName;
		}

		private static bool IsDisplayed(VisualElement element)
		{
			return element.enabledSelf
				&& element.visible
				&& element.resolvedStyle.display != DisplayStyle.None
				&& element.resolvedStyle.opacity > 0f;
		}

		// Zero-sized and not-yet-laid-out elements are unreachable, and a NaN layout would poison
		// every distance the navigator computes.
		private static bool IsBigEnough(VisualElement element)
		{
			var layout = element.layout;
			return !float.IsNaN(layout.width)
				&& !float.IsNaN(layout.height)
				&& layout.width > MinimumSize
				&& layout.height > MinimumSize;
		}
	}
}
