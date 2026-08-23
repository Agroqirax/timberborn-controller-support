using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace ControllerSupport
{
	// Makes the current selection look like something the game shipped.
	//
	// Timberborn's theme styles :hover but not :focus, so borrowing the hover pseudo-state gives a
	// native-looking selection for free. The catch is that a real mouse sets that state on the
	// element under the pointer *and every one of its ancestors*, whereas setting it on one element
	// only lights up rules written against that exact element. Composite controls put their visible
	// styling on an inner part - a Toggle's checkmark, a Slider's dragger, a Dropdown's field - so
	// flagging the outer control alone left them looking unselected.
	//
	// So instead of guessing, ask the panel what a pointer resting at the control's centre would be
	// over, and light up that whole chain. Whatever was set is remembered exactly, so it can be
	// undone exactly - which is what keeps stale highlights from accumulating.
	internal class SelectionHighlighter
	{
		private static readonly Color FallbackColor = new Color(1f, 0.78f, 0.1f);
		private const float FallbackBorderWidth = 2f;

		private readonly List<VisualElement> _highlighted = new List<VisualElement>();
		private readonly List<VisualElement> _pending = new List<VisualElement>();
		private readonly List<VisualElement> _targets = new List<VisualElement>();

		public void Apply(VisualElement element)
		{
			Clear();
			if (element == null)
			{
				return;
			}

			_highlighted.Add(element);

			// Composite controls say where their hover styling lives; anything else gets a plain
			// pointer-at-the-centre pick.
			_targets.Clear();
			ControlActivator.CollectHoverTargets(element, _targets);
			if (_targets.Count == 0)
			{
				AddPickedChain(element, _highlighted);
			}
			else
			{
				foreach (var target in _targets)
				{
					AddChain(target, element, _highlighted);
				}
			}

			foreach (var highlighted in _highlighted)
			{
				SetHighlighted(highlighted, true);
			}
		}

		// Safe to call when the elements have already been detached from the panel, which is the
		// normal case when a panel closes while something in it was selected.
		public void Clear()
		{
			foreach (var highlighted in _highlighted)
			{
				SetHighlighted(highlighted, false);
			}

			_highlighted.Clear();
		}

		// The elements a pointer at the centre of this control would be over, innermost first. Only
		// committed when the picked element really does sit inside the control - if something else is
		// covering it, we leave the chain alone rather than lighting up an unrelated part of the UI.
		private void AddPickedChain(VisualElement element, List<VisualElement> into)
		{
			var panel = element.panel;
			if (panel == null)
			{
				return;
			}

			var picked = panel.Pick(element.worldBound.center);
			if (picked == null || picked == element)
			{
				return;
			}

			_pending.Clear();
			for (var current = picked; current != null; current = current.hierarchy.parent)
			{
				if (current == element)
				{
					into.AddRange(_pending);
					return;
				}

				_pending.Add(current);
			}
		}

		// A real mouse sets the hover state on the element under it and every ancestor, so walk from
		// the styled part up to the control itself. Only committed once the walk actually reaches the
		// control, so an unrelated element could never drag half the tree into the highlight.
		private void AddChain(VisualElement from, VisualElement upTo, List<VisualElement> into)
		{
			_pending.Clear();
			for (var current = from; current != null; current = current.hierarchy.parent)
			{
				if (current == upTo)
				{
					foreach (var pending in _pending)
					{
						if (!into.Contains(pending))
						{
							into.Add(pending);
						}
					}

					return;
				}

				_pending.Add(current);
			}
		}

		private static void SetHighlighted(VisualElement element, bool highlighted)
		{
			if (VisualElementProbe.SupportsHoverState)
			{
				VisualElementProbe.SetHoverState(element, highlighted);
				return;
			}

			if (highlighted)
			{
				element.style.borderTopWidth = FallbackBorderWidth;
				element.style.borderBottomWidth = FallbackBorderWidth;
				element.style.borderLeftWidth = FallbackBorderWidth;
				element.style.borderRightWidth = FallbackBorderWidth;
				element.style.borderTopColor = FallbackColor;
				element.style.borderBottomColor = FallbackColor;
				element.style.borderLeftColor = FallbackColor;
				element.style.borderRightColor = FallbackColor;
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
