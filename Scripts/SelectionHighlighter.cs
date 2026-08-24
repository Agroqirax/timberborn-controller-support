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
		private static readonly Color RingColor = new Color(1f, 0.78f, 0.1f, 0.9f);
		private static readonly Color RingFill = new Color(1f, 0.78f, 0.1f, 0.12f);
		private const float RingBorderWidth = 2f;
		private const float RingCornerRadius = 4f;
		private const float FallbackBorderWidth = 2f;

		private readonly List<VisualElement> _highlighted = new List<VisualElement>();
		private readonly List<VisualElement> _pending = new List<VisualElement>();
		private readonly List<VisualElement> _targets = new List<VisualElement>();

		private VisualElement _ring;
		private VisualElement _ringHost;
		private Rect _ringPlacement;

		public void Apply(VisualElement element)
		{
			ClearHover();
			if (element == null)
			{
				DetachRing();
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

			UpdateRing(element);
		}

		// Safe to call when the elements have already been detached from the panel, which is the
		// normal case when a panel closes while something in it was selected.
		public void Clear()
		{
			DetachRing();
			ClearHover();
		}

		private void ClearHover()
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

		// Drawn on every selection, on top of whatever :hover gives us. The theme styles hover very
		// unevenly - some controls barely change, and the small ones change so little that the cursor is
		// easy to lose entirely - so the ring is what actually tells the player where they are, and hover
		// is the bonus that makes it look native.
		//
		// An absolutely positioned overlay rather than a border on the control itself: UI Toolkit lays
		// out border-box, so widening the control's own border would eat into its padding and shove the
		// icon and count around every time the selection lands on it.
		private void UpdateRing(VisualElement target)
		{
			// Parented to the panel's own root, never to the target's parent (an earlier version did
			// that - see git history). VisualElement drops its measure function the moment it gains a
			// child, which is why the ring was never parented to the target itself (a text-only Button
			// given a child collapses to nothing and wraps one character per line - what turned "read
			// more" into a vertical stack of letters). Parenting to the target's *parent* avoided that,
			// but broke worse for a target that lives in a flex-wrap row (the workplace panel's
			// per-beaver list): adding any extra child there - even one that is itself position:absolute
			// - visibly disrupted that container's wrap accounting, and the hovered row jumped to
			// roughly the combined height of every wrapped row instead of just its own (confirmed via
			// logged layout heights: 32 -> 66 while the container's own height never moved). A
			// non-wrapping row never showed this, which is what kept it from surfacing anywhere else.
			// The panel root never wraps anything, so it can take a new child safely, the same way
			// Tooltip/DropdownListDrawer already use their own dedicated roots instead of injecting into
			// whatever they're pointing at.
			var root = target?.panel?.visualTree;
			if (root == null)
			{
				DetachRing();
				return;
			}

			var worldBound = target.worldBound;
			if (float.IsNaN(worldBound.x) || float.IsNaN(worldBound.y))
			{
				DetachRing();
				return;
			}

			_ring ??= CreateRing();
			if (!ReferenceEquals(_ringHost, root))
			{
				DetachRing();
				root.hierarchy.Add(_ring);
				_ringHost = root;
			}

			// WorldToLocal converts the target's screen-space bounds into the root's own local space -
			// unlike the old parent-relative math, this holds regardless of how deep target sits below
			// root or what any of its ancestors' own borders/padding are.
			var topLeft = root.WorldToLocal(worldBound.position);
			var placement = new Rect(topLeft.x, topLeft.y, worldBound.width, worldBound.height);

			// Re-applied every frame the selection moves, and scrolling moves it constantly, so skip the
			// style writes when nothing actually changed.
			if (placement == _ringPlacement)
			{
				return;
			}

			_ringPlacement = placement;
			_ring.style.left = placement.xMin;
			_ring.style.top = placement.yMin;
			_ring.style.width = placement.width;
			_ring.style.height = placement.height;
		}

		private void DetachRing()
		{
			if (_ringHost == null)
			{
				return;
			}

			if (_ring != null && _ring.hierarchy.parent == _ringHost)
			{
				_ringHost.hierarchy.Remove(_ring);
			}

			_ringHost = null;
			_ringPlacement = default;
		}

		private static VisualElement CreateRing()
		{
			// PickingMode.Ignore keeps it out of the mouse's way, and having no click handler of its own
			// keeps it out of the candidate walk.
			var ring = new VisualElement
			{
				name = "ControllerSupportSelectionRing",
				pickingMode = PickingMode.Ignore
			};

			var style = ring.style;
			style.position = Position.Absolute;
			style.backgroundColor = RingFill;
			style.borderTopWidth = RingBorderWidth;
			style.borderBottomWidth = RingBorderWidth;
			style.borderLeftWidth = RingBorderWidth;
			style.borderRightWidth = RingBorderWidth;
			style.borderTopColor = RingColor;
			style.borderBottomColor = RingColor;
			style.borderLeftColor = RingColor;
			style.borderRightColor = RingColor;
			style.borderTopLeftRadius = RingCornerRadius;
			style.borderTopRightRadius = RingCornerRadius;
			style.borderBottomLeftRadius = RingCornerRadius;
			style.borderBottomRightRadius = RingCornerRadius;
			return ring;
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
