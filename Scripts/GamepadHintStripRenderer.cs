using System;
using System.Collections.Generic;
using Timberborn.CoreUI;
using Timberborn.Localization;
using UnityEngine;
using UnityEngine.UIElements;
using KeyBindingRegistry = Timberborn.KeyBindingSystem.KeyBindingRegistry;

namespace ControllerSupport
{
	// Builds the visible hint row into a container element, shared by both the Top and Bottom mounts
	// (GamepadHintStripController) so there is exactly one place that knows how to turn a hint list
	// into UI, not two divergent copies.
	//
	// Deliberately does NOT reuse block-object-placement-panel__button/__binding (ToolPanelStyle.uss) -
	// that stylesheet is only attached to ToolPanel's own loaded VisualTreeAsset, not to "Common/GameUI"
	// (see UILayout.Load), so an element mounted as a sibling of ToolPanel under Bottom-bar/Top-right
	// never inherits it: those classes silently matched nothing, which is why an earlier version of
	// this class rendered as unstyled, vertically-stacked text. Every class used below instead comes
	// from CoreStyle.uss/CommonStyle.uss/GameMiscStyle.uss, all three already attached at the root of
	// "Common/GameUI" itself (see GameUI.uxml's own <Style> tags), so they apply regardless of where in
	// that document a child is added or how it was built.
	internal class GamepadHintStripRenderer
	{
		// NineSliceVisualElement (Timberborn.CoreUI, public) is what actually paints a class's
		// --background-image/--background-slice custom properties - a plain VisualElement with the same
		// class applied does nothing, since it's NineSliceBackground's OnGenerateVisualContent that reads
		// those properties, not the USS engine itself. NineSliceButton has the same behaviour but is
		// internal to Timberborn.CoreUI and can't be constructed from a mod.
		private const string PillBackgroundClass = "button-game";
		private const string TextClass = "game-text-normal";
		private const float IconDisplayHeight = 20f;
		private const float IconLabelGap = 4f;
		private const float HintGap = 12f;
		private const float PillPadding = 4f;
		private const float PillMargin = 4f;

		private readonly VisualElement _container;
		private readonly ILoc _loc;
		private readonly KeyBindingRegistry _keyBindingRegistry;

		// A delegate rather than a captured float so the bottom mount's budget (tied to Screen.width)
		// stays correct across a window resize/UI scale change instead of freezing whatever it was at
		// construction time.
		private readonly Func<float> _maxWidthProvider;

		// Bottom mirrors BlockObjectPlacementPanel's look: each hint is its own separate pill
		// (NineSliceVisualElement + button-game). Top mounts everything inside one shared green box
		// (GamepadHintStripController gives that box the background itself), so individual hints there
		// are flat - icon+label only, no per-hint background - matching "[(A) Select (B) Cancel ...]"
		// inside one container rather than several small boxes.
		private readonly bool _wrapEachHintInPill;

		public GamepadHintStripRenderer(VisualElement container, ILoc loc, KeyBindingRegistry keyBindingRegistry,
			Func<float> maxWidthProvider, bool wrapEachHintInPill)
		{
			_container = container;
			_loc = loc;
			_keyBindingRegistry = keyBindingRegistry;
			_maxWidthProvider = maxWidthProvider;
			_wrapEachHintInPill = wrapEachHintInPill;
		}

		// Greedily adds hints from the front of the list - already most-important-first, see
		// GamepadHintResolver - stopping the moment the next one would overflow the available width. A
		// wide screen naturally keeps more hints (including the trailing "obvious" one); a narrow one
		// drops from the end first, with no separate priority field needed.
		public void Render(IReadOnlyList<GamepadHint> hints)
		{
			_container.Clear();

			var maxWidth = _maxWidthProvider();
			var usedWidth = 0f;
			foreach (var hint in hints)
			{
				if (!TryResolveIcon(hint, out var sprite))
				{
					continue;
				}

				var label = _loc.T(hint.LabelLocKey);
				var iconWidth = IconDisplayHeight * (sprite.rect.width / sprite.rect.height);
				var labelWidth = MeasureLabelWidth(label);
				var pillExtra = _wrapEachHintInPill ? 2 * (PillPadding + PillMargin) : 0f;
				var gap = usedWidth > 0f ? HintGap : 0f;
				var hintWidth = gap + pillExtra + iconWidth + IconLabelGap + labelWidth;

				if (usedWidth + hintWidth > maxWidth)
				{
					break;
				}

				usedWidth += hintWidth;
				_container.Add(BuildHintElement(sprite, iconWidth, label));
			}
		}

		// Shares GamepadHint.ResolveIconKey with GamepadHintResolver's own dedup pass, so "which icon
		// does this hint actually draw" is answered exactly once, not twice in two different ways.
		private bool TryResolveIcon(GamepadHint hint, out Sprite sprite)
		{
			var key = hint.ResolveIconKey(_keyBindingRegistry);
			sprite = key != null ? GamepadIconRegistry.Get(key) : null;
			return sprite != null;
		}

		// Added into the real container (not a detached element) so it inherits the same resolved
		// style - font asset/size - the actual label below will use, then removed immediately.
		// MeasureTextSize is the UI Toolkit API built for exactly this "would this text fit" question,
		// answering it without needing a completed layout pass first.
		private float MeasureLabelWidth(string text)
		{
			var probe = new Label(text);
			probe.AddToClassList(TextClass);
			probe.style.visibility = Visibility.Hidden;
			_container.Add(probe);
			// MeasureMode is a nested type (VisualElement.MeasureMode), not a top-level one - it can't be
			// reached with a plain "using UnityEngine.UIElements;" the way most UI Toolkit types are.
			var width = probe.MeasureTextSize(text, 0f, VisualElement.MeasureMode.Undefined, 0f,
				VisualElement.MeasureMode.Undefined).x;
			_container.Remove(probe);
			return width;
		}

		private VisualElement BuildHintElement(Sprite sprite, float iconWidth, string label)
		{
			VisualElement row;
			if (_wrapEachHintInPill)
			{
				var pill = new NineSliceVisualElement();
				pill.AddToClassList(PillBackgroundClass);
				pill.style.paddingLeft = PillPadding;
				pill.style.paddingRight = PillPadding;
				pill.style.paddingTop = PillPadding;
				pill.style.paddingBottom = PillPadding;
				pill.style.marginLeft = PillMargin;
				pill.style.marginRight = PillMargin;
				row = pill;
			}
			else
			{
				row = new VisualElement();
			}

			row.style.flexDirection = FlexDirection.Row;
			row.style.alignItems = Align.Center;

			var icon = new VisualElement
			{
				style =
				{
					backgroundImage = new StyleBackground(sprite),
					height = IconDisplayHeight,
					width = iconWidth,
					flexShrink = 0,
				},
			};
			row.Add(icon);

			var text = new Label(label) { style = { marginLeft = IconLabelGap } };
			text.AddToClassList(TextClass);
			row.Add(text);

			return row;
		}
	}
}
