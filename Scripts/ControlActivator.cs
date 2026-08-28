using System.Collections.Generic;
using Timberborn.CoreUI;
using Timberborn.DropdownSystem;
using UnityEngine;
using UnityEngine.UIElements;

namespace ControllerSupport
{
	// Everything the mod knows about Timberborn's individual controls: which ones are a single
	// navigation target, how to press one, and which ones answer to a sideways push.
	//
	// Treating composite controls as leaves matters more than it looks. A Slider is built from a
	// track, a drag container and a dragger, several of which are independently clickable - so
	// descending into one turned a single control into a scatter of candidates at slightly different
	// positions. That is why a slider could highlight when approached from below and not from above:
	// the two approaches landed on different sub-elements, only one of which had visible styling.
	internal static class ControlActivator
	{
		private const string DropdownSelectableClass = "dropdown__selectable";

		// How many sideways pushes it takes to cross a float slider's whole range.
		private const float SliderSteps = 20f;

		public static bool IsControl(VisualElement element)
		{
			return element is Button
				|| element is Toggle
				|| element is Slider
				|| element is SliderInt
				|| element is PreciseSlider
				|| element is Dropdown
				|| element is TextField
				// IntegerField/FloatField derive from TextValueField<T>, a sibling of TextField's own
				// TextInputBaseField<string> - not a subclass of it - so `is TextField` alone misses them.
				// PowerMeterFragment's IntThreshold and ResourceCounterFragment's Threshold are both
				// IntegerFields, and neither was reachable before this.
				|| element is IntegerField
				|| element is FloatField;
		}

		public static void Activate(VisualElement element)
		{
			switch (element)
			{
				// A Toggle reacts to pointer events through its Clickable manipulator, not to
				// ClickEvent, so a synthesised click slides straight past it. Setting the value fires
				// the ChangeEvent the game is actually listening for.
				case Toggle toggle:
					toggle.value = !toggle.value;
					return;

				// The Dropdown itself holds no click handler - it delegates to an inner button, which
				// is "Selection" normally and the arrow when the dropdown is in buttons-only mode.
				case Dropdown dropdown:
					Click(SelectionButton(dropdown));
					return;

				// Pressing a slider should do nothing rather than jump its value; left/right adjusts.
				case Slider:
				case SliderInt:
				case PreciseSlider:
					return;

				// Focusing is what a click would do anyway for a text field, and it is also what
				// TextElementInitializer is watching for: focusing blocks the rest of input processing
				// so real typing (keyboard or the platform's on-screen keyboard) is not fought over by
				// this mod's own stick/d-pad reads. GamepadNavigationInputProcessor keeps running
				// through that via ILateUpdatableSingleton, purely to give B a way to blur back out.
				case TextField textField:
					textField.Focus();
					return;

				// Same reasoning as TextField above - IntegerField/FloatField aren't one, so they need
				// their own case (see IsControl).
				case IntegerField integerField:
					integerField.Focus();
					return;

				case FloatField floatField:
					floatField.Focus();
					return;

				// A list row that has no click handler of its own - the settlement list's case - never
				// tells its ListView anything happened, click or not. Confirm is the only signal it gets,
				// so this is the one place selection has to be driven by hand rather than left to fire as
				// a side effect of the click below.
				default:
					SyncListSelection(element);

					// LeverFragment's switch button (and PinnedLeversPanel's pinned-lever row) skip
					// ClickEvent and `clicked` entirely and drive the lever straight off
					// PointerDownEvent/PointerUpEvent - see VisualElementProbe.HasPointerPressHandler.
					// Checked ahead of Click() specifically because a real ClickEvent handler or
					// `clicked` subscriber takes priority when both happen to be present, same
					// precedence NavigationCandidates.IsInertButton already applies.
					if (!VisualElementProbe.HasClickHandler(element)
						&& !VisualElementProbe.HasClickableDelegate(element)
						&& VisualElementProbe.HasPointerPressHandler(element))
					{
						PressAndRelease(element);
						return;
					}

					Click(element);
					return;
			}
		}

		// Handles a push landing on a control that would rather absorb it than be left by it. Returns
		// false to let the push fall through to normal navigation.
		public static bool TryAdjust(VisualElement element, Vector2Int direction)
		{
			// Everything below is a sideways gesture, and only a pure one - a diagonal should navigate.
			if (direction.x == 0 || direction.y != 0)
			{
				return false;
			}

			var delta = direction.x;
			switch (element)
			{
				case PreciseSlider preciseSlider:
					return TryAdjustSlider(preciseSlider.Q<Slider>("Slider"), delta);

				case Slider slider:
					return TryAdjustSlider(slider, delta);

				case SliderInt sliderInt:
					var stepped = Mathf.Clamp(sliderInt.value + delta, sliderInt.lowValue, sliderInt.highValue);
					if (stepped == sliderInt.value)
					{
						return true;
					}

					sliderInt.value = stepped;
					return true;

				// Only in buttons-only mode are these arrows displayed; otherwise they are
				// display:none and the push should navigate away from the dropdown as usual.
				case Dropdown dropdown:
					var arrow = dropdown.Q<Button>(delta < 0 ? "ArrowLeft" : "ArrowRight");
					if (arrow == null || arrow.resolvedStyle.display == DisplayStyle.None)
					{
						return false;
					}

					Click(arrow);
					return true;

				default:
					return false;
			}
		}

		// Every list row is its own navigation candidate now (see NavigationCandidates.CollectRows), but
		// none of them fire selection just by being aimed at - that used to auto-select whatever the
		// cursor passed over, which was fine for a list that only ever previews (SettlementList) and
		// actively wrong for one that gates an action next to it (ModUploaderBox's Upload button,
		// bound to whichever row happened to be selected when the player finally reached it). Confirm
		// is the only thing that should ever change what is selected.
		private static void SyncListSelection(VisualElement element)
		{
			if (!TryFindRow(element, out var view, out var index) || view.selectedIndex == index)
			{
				return;
			}

			view.SetSelection(index);
		}

		// Rows carry no back-reference to their list or index, so recover both by walking up to the
		// owning ListView and matching the element against each realised row. Lists here are short
		// enough that the linear scan costs nothing next to a frame.
		private static bool TryFindRow(VisualElement element, out BaseVerticalCollectionView view, out int index)
		{
			view = null;
			index = -1;

			for (var parent = element.hierarchy.parent; parent != null; parent = parent.hierarchy.parent)
			{
				if (parent is BaseVerticalCollectionView collectionView)
				{
					view = collectionView;
					break;
				}
			}

			var source = view?.itemsSource;
			if (source == null)
			{
				view = null;
				return false;
			}

			for (var i = 0; i < source.Count; i++)
			{
				if (!ReferenceEquals(view.GetRootElementForIndex(i), element))
				{
					continue;
				}

				index = i;
				return true;
			}

			view = null;
			return false;
		}

		private static bool TryAdjustSlider(Slider slider, int delta)
		{
			if (slider == null)
			{
				return false;
			}

			var step = (slider.highValue - slider.lowValue) / SliderSteps;
			var next = Mathf.Clamp(slider.value + delta * step, slider.lowValue, slider.highValue);
			if (!Mathf.Approximately(next, slider.value))
			{
				slider.value = next;
			}

			// Consumed either way: at the end of its range a slider should sit still rather than let
			// the push wander off to a neighbouring control.
			return true;
		}

		// The sub-elements a mouse would actually be over while hovering this control.
		//
		// Timberborn styles :hover on the interactive part, which for a composite control is not the
		// geometric centre of the whole thing - a Dropdown's bounds include its label, so a point in
		// the middle can land well away from the field that carries the styling, and a Slider's
		// centre sits on the track rather than anything that lights up. Picking at the centre works
		// for a Toggle and misses for these, which is why they highlighted as nothing at all.
		public static void CollectHoverTargets(VisualElement element, List<VisualElement> into)
		{
			switch (element)
			{
				// The checkmark is the part that visibly reacts, and it sits inside the toggle's input
				// wrapper. Naming it beats picking at the toggle's centre, which fails whenever
				// anything overlaps the row - the last row of a scrolling settings page being the case
				// that gave this away.
				case Toggle toggle:
					Add(into, toggle.Q("unity-checkmark"));
					Add(into, toggle.Q(className: "unity-toggle__input"));
					return;

				case Dropdown dropdown:
					Add(into, SelectionButton(dropdown));
					return;

				case PreciseSlider preciseSlider:
					AddSliderTargets(into, preciseSlider.Q<Slider>("Slider"));
					return;

				case Slider slider:
					AddSliderTargets(into, slider);
					return;

				case SliderInt sliderInt:
					AddSliderTargets(into, sliderInt);
					return;
			}
		}

		// Which of these carries the visible hover styling is a theme detail we cannot read from the
		// decompiled source, so light the whole drag area: the track, the fill and the handle.
		private static void AddSliderTargets(List<VisualElement> into, VisualElement slider)
		{
			if (slider == null)
			{
				return;
			}

			Add(into, slider.Q("unity-drag-container"));
			Add(into, slider.Q("unity-tracker"));
			Add(into, slider.Q("unity-dragger"));
		}

		private static void Add(List<VisualElement> into, VisualElement element)
		{
			if (element != null && !into.Contains(element))
			{
				into.Add(element);
			}
		}

		private static Button SelectionButton(Dropdown dropdown)
		{
			var selection = dropdown.Q<Button>("Selection");
			if (selection != null && selection.ClassListContains(DropdownSelectableClass))
			{
				return selection;
			}

			return dropdown.Q<Button>("ArrowDown") ?? selection;
		}

		// Sends PointerDownEvent then PointerUpEvent, both targeted at element rather than left to land
		// on whatever is under the mouse (same reason Click() below sets ClickEvent.target). Confirm
		// itself is momentary, so pressing and releasing back to back is the right mapping for both of
		// LeverFragment's cases: SwitchOff/SwitchOn latch levers act on the down, and spring-return
		// levers - which release on their own the instant the physical press ends - would otherwise be
		// left stuck "pressed" with no synthesised release ever coming. A held confirm still only
		// produces a single down/up pair here, unlike the dedicated per-building action keybind
		// (LeverFragment.ProcessInput's own UniqueBuildingActionKey) which does track hold-to-release -
		// this is the panel button, not that keybind, and mirrors what a quick mouse click already does.
		private static void PressAndRelease(VisualElement element)
		{
			using (var pointerDownEvent = PointerDownEvent.GetPooled())
			{
				pointerDownEvent.target = element;
				element.SendEvent(pointerDownEvent);
			}

			using var pointerUpEvent = PointerUpEvent.GetPooled();
			pointerUpEvent.target = element;
			element.SendEvent(pointerUpEvent);
		}

		private static void Click(VisualElement element)
		{
			if (element == null)
			{
				return;
			}

			// ClickEvent derives from PointerEventBase, whose dispatch routes to whatever sits under
			// the mouse unless a target is already set. Setting it is what makes the press land on the
			// selected element instead of the hovered one.
			using var clickEvent = ClickEvent.GetPooled();
			clickEvent.target = element;
			element.SendEvent(clickEvent);

			// A Button wired through its native `clicked` event (Clickable's own pointer-down/up
			// handling, not ClickEvent) never sees the dispatch above - see
			// VisualElementProbe.InvokeClickedDelegate. Safe to call unconditionally: a Timberborn button
			// that only ever used RegisterCallback<ClickEvent> has no `clicked` subscriber, so this is a
			// no-op for it.
			VisualElementProbe.InvokeClickedDelegate(element);
		}
	}
}
