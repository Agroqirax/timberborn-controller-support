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
				|| element is Dropdown;
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

				default:
					Click(element);
					return;
			}
		}

		// Handles a left/right push landing on a control that wants it. Returns false to let the
		// push fall through to normal navigation.
		public static bool TryAdjust(VisualElement element, int delta)
		{
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
		}
	}
}
