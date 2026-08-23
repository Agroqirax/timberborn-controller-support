using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace ControllerSupport
{
	// Picks the next selection from where things actually are on screen.
	//
	// The previous version stepped an index through the candidate list in tree order, so left/right
	// and up/down did the same thing and the selection wrapped into unrelated parts of the panel.
	// Scoring by geometry means a push left lands on the thing to the left, and it needs no
	// knowledge of any particular panel's layout.
	internal static class SpatialNavigator
	{
		// How much a candidate is punished for sitting off to the side of the travel direction.
		// High enough that a well-aligned element several rows away still beats a near neighbour in
		// the next column over.
		private const float CrossAxisPenalty = 3f;

		// Slack for "in line with" and "ahead of", in pixels, to absorb rounding in resolved layouts.
		private const float Tolerance = 2f;

		public static VisualElement Next(List<VisualElement> candidates, VisualElement current, Vector2Int direction)
		{
			if (candidates.Count == 0)
			{
				return null;
			}

			if (current == null)
			{
				return FirstInReadingOrder(candidates);
			}

			var from = current.worldBound;

			VisualElement best = null;
			var bestScore = float.MaxValue;

			// If nothing lies ahead we wrap to the far end of the same row or column, so a long list
			// still cycles - but only among elements we are genuinely in line with, which stops a
			// wrap from jumping into a different column.
			VisualElement wrapped = null;
			var wrappedDistance = 0f;

			foreach (var candidate in candidates)
			{
				if (ReferenceEquals(candidate, current))
				{
					continue;
				}

				var to = candidate.worldBound;
				var advance = Advance(from, to, direction);
				var offset = CrossAxisGap(from, to, direction);

				if (advance > Tolerance)
				{
					var score = advance + offset * CrossAxisPenalty;
					if (score < bestScore)
					{
						bestScore = score;
						best = candidate;
					}
				}
				else if (offset <= Tolerance && -advance > wrappedDistance)
				{
					wrappedDistance = -advance;
					wrapped = candidate;
				}
			}

			return best ?? wrapped;
		}

		// Used to recover the selection after a panel rebuilds its children - ListView recycles row
		// elements, so the element that was selected is often simply gone. Re-selecting whatever now
		// sits closest to where the player last was keeps the cursor still instead of snapping it
		// back to the top of the list.
		public static VisualElement NearestTo(List<VisualElement> candidates, Vector2 point)
		{
			VisualElement best = null;
			var bestDistance = float.MaxValue;

			foreach (var candidate in candidates)
			{
				var distance = (candidate.worldBound.center - point).sqrMagnitude;
				if (distance < bestDistance)
				{
					bestDistance = distance;
					best = candidate;
				}
			}

			return best;
		}

		private static VisualElement FirstInReadingOrder(List<VisualElement> candidates)
		{
			VisualElement best = null;

			foreach (var candidate in candidates)
			{
				if (best == null)
				{
					best = candidate;
					continue;
				}

				var a = candidate.worldBound;
				var b = best.worldBound;
				if (a.y < b.y - Tolerance || (a.y <= b.y + Tolerance && a.x < b.x))
				{
					best = candidate;
				}
			}

			return best;
		}

		// How far the candidate lies along the travel direction, centre to centre. Negative means it
		// is behind us.
		private static float Advance(Rect from, Rect to, Vector2Int direction)
		{
			var delta = to.center - from.center;
			return direction.x != 0 ? delta.x * direction.x : delta.y * direction.y;
		}

		// The gap between the two elements on the axis across the travel direction, measured edge to
		// edge so anything overlapping our row or column scores a clean zero regardless of size.
		private static float CrossAxisGap(Rect from, Rect to, Vector2Int direction)
		{
			return direction.x != 0
				? IntervalGap(from.yMin, from.yMax, to.yMin, to.yMax)
				: IntervalGap(from.xMin, from.xMax, to.xMin, to.xMax);
		}

		private static float IntervalGap(float aMin, float aMax, float bMin, float bMax)
		{
			return Mathf.Max(0f, Mathf.Max(aMin - bMax, bMin - aMax));
		}
	}
}
