using UnityEngine;
using UnityEngine.UIElements;

namespace ControllerSupport
{
	// Makes the current selection look like something the game shipped.
	//
	// Timberborn's theme styles :hover but not :focus, so borrowing the hover pseudo-state gives a
	// native-looking selection for free. The pseudo-state is internal, hence the probe; when a future
	// Unity version renames it we fall back to drawing our own border rather than showing nothing.
	internal static class SelectionHighlighter
	{
		private static readonly Color FallbackColor = new Color(1f, 0.78f, 0.1f);
		private const float FallbackBorderWidth = 2f;

		public static void Apply(VisualElement element)
		{
			if (element == null)
			{
				return;
			}

			if (VisualElementProbe.SupportsHoverState)
			{
				VisualElementProbe.SetHoverState(element, hovered: true);
				return;
			}

			element.style.borderTopWidth = FallbackBorderWidth;
			element.style.borderBottomWidth = FallbackBorderWidth;
			element.style.borderLeftWidth = FallbackBorderWidth;
			element.style.borderRightWidth = FallbackBorderWidth;
			element.style.borderTopColor = FallbackColor;
			element.style.borderBottomColor = FallbackColor;
			element.style.borderLeftColor = FallbackColor;
			element.style.borderRightColor = FallbackColor;
		}

		// Safe to call on an element that has already been detached from the panel - which is the
		// normal case when a panel closes while something in it was selected. Leaving the hover state
		// behind on a recycled element is exactly how the old build grew its ghost highlights.
		public static void Remove(VisualElement element)
		{
			if (element == null)
			{
				return;
			}

			if (VisualElementProbe.SupportsHoverState)
			{
				VisualElementProbe.SetHoverState(element, hovered: false);
				return;
			}

			element.style.borderTopWidth = StyleKeyword.Null;
			element.style.borderBottomWidth = StyleKeyword.Null;
			element.style.borderLeftWidth = StyleKeyword.Null;
			element.style.borderRightWidth = StyleKeyword.Null;
			element.style.borderTopColor = StyleKeyword.Null;
			element.style.borderBottomColor = StyleKeyword.Null;
			element.style.borderLeftColor = StyleKeyword.Null;
			element.style.borderRightColor = StyleKeyword.Null;
		}
	}
}
