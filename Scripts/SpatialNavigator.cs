using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace ControllerSupport
{
	// Picks the next selection from where things actually are on screen.
	//
	// Two rules carry the whole thing. A move only ever goes to something sharing your row (moving
	// sideways) or your column (moving up and down) - alignment is a hard requirement, not a
	// weighted preference. And because that alone can maroon a control that lines up with nothing,
	// a cluster which cannot reach the rest of the panel by aligned moves is allowed one hop out.
	internal static class SpatialNavigator
	{
		// Slack for "in line with" and "ahead of", in pixels, to absorb rounding in resolved layouts.
		private const float Tolerance = 2f;

		private static readonly HashSet<VisualElement> Cluster = new HashSet<VisualElement>();
		private static readonly List<VisualElement> ClusterQueue = new List<VisualElement>();
		private static readonly HashSet<VisualElement> Assigned = new HashSet<VisualElement>();

		public static VisualElement Next(List<VisualElement> candidates, VisualElement current, Vector2Int direction)
		{
			if (candidates.Count == 0)
			{
				return null;
			}

			if (current == null)
			{
				return First(candidates);
			}

			var from = current.worldBound;

			// "Nearest thing in roughly that direction" sounds more forgiving but is actively wrong on
			// a page of stacked rows: the vertical gap between two rows is far smaller than the
			// horizontal distance across one, so a sideways push scores a control in a neighbouring
			// row as a fine match and jumps to it. Requiring the bands to overlap means a sideways
			// push in a single-column list correctly does nothing.
			VisualElement best = null;
			var bestAdvance = float.MaxValue;

			// If nothing lies ahead we wrap to the far end of the same row or column, so a long list
			// still cycles - and for the same reason, only among elements we are in line with.
			VisualElement wrapped = null;
			var wrappedDistance = 0f;

			foreach (var candidate in candidates)
			{
				if (ReferenceEquals(candidate, current))
				{
					continue;
				}

				var to = candidate.worldBound;
				if (CrossAxisGap(from, to, direction) > Tolerance)
				{
					continue;
				}

				var advance = Advance(from, to, direction);
				if (advance > Tolerance)
				{
					if (advance < bestAdvance)
					{
						bestAdvance = advance;
						best = candidate;
					}
				}
				else if (-advance > wrappedDistance)
				{
					wrappedDistance = -advance;
					wrapped = candidate;
				}
			}

			return best ?? wrapped ?? Escape(candidates, current, direction);
		}

		// Nothing in our row or column lies that way, which is usually the correct answer. But a
		// control marooned in a corner - the title screen's social buttons sit in their own little
		// row, aligned with nothing in the main column - would otherwise be somewhere the player can
		// arrive and never leave. So allow a single unaligned hop, and only out of a cluster that
		// cannot reach the rest of the panel by aligned moves at all. A settings list is one big
		// cluster, so this never fires there and left/right stays quiet.
		private static VisualElement Escape(List<VisualElement> candidates, VisualElement current, Vector2Int direction)
		{
			CollectCluster(candidates, current);
			if (Cluster.Count == candidates.Count)
			{
				return null;
			}

			var from = current.worldBound;
			VisualElement best = null;
			var bestScore = float.MaxValue;

			foreach (var candidate in candidates)
			{
				if (Cluster.Contains(candidate))
				{
					continue;
				}

				var to = candidate.worldBound;
				var advance = Advance(from, to, direction);
				if (advance <= Tolerance)
				{
					continue;
				}

				var score = advance + CrossAxisGap(from, to, direction);
				if (score < bestScore)
				{
					bestScore = score;
					best = candidate;
				}
			}

			return best;
		}

		// Where to start when nothing is selected: the top-left of the *biggest* cluster, so the title
		// screen lands on the main button column rather than on whichever social button happens to sit
		// highest on the screen.
		public static VisualElement First(List<VisualElement> candidates)
		{
			VisualElement best = null;
			var bestSize = 0;

			Assigned.Clear();
			foreach (var candidate in candidates)
			{
				if (Assigned.Contains(candidate))
				{
					continue;
				}

				CollectCluster(candidates, candidate);

				VisualElement first = null;
				foreach (var member in Cluster)
				{
					Assigned.Add(member);
					first = Earlier(first, member);
				}

				if (Cluster.Count > bestSize)
				{
					bestSize = Cluster.Count;
					best = first;
				}
			}

			return best;
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

		// Everything reachable from `start` by repeatedly stepping to something that shares a row or a
		// column. One on-screen cluster of controls ends up as one set.
		private static void CollectCluster(List<VisualElement> candidates, VisualElement start)
		{
			Cluster.Clear();
			ClusterQueue.Clear();
			Cluster.Add(start);
			ClusterQueue.Add(start);

			for (var i = 0; i < ClusterQueue.Count; i++)
			{
				var bounds = ClusterQueue[i].worldBound;
				foreach (var candidate in candidates)
				{
					if (Cluster.Contains(candidate) || !SharesBand(bounds, candidate.worldBound))
					{
						continue;
					}

					Cluster.Add(candidate);
					ClusterQueue.Add(candidate);
				}
			}
		}

		// Two controls belong to the same cluster when they line up on either axis - the same
		// relationship a move needs, without caring which way it points.
		private static bool SharesBand(Rect a, Rect b)
		{
			return IntervalGap(a.yMin, a.yMax, b.yMin, b.yMax) <= Tolerance
				|| IntervalGap(a.xMin, a.xMax, b.xMin, b.xMax) <= Tolerance;
		}

		private static VisualElement Earlier(VisualElement current, VisualElement candidate)
		{
			if (current == null)
			{
				return candidate;
			}

			var a = candidate.worldBound;
			var b = current.worldBound;
			var earlier = a.y < b.y - Tolerance || (a.y <= b.y + Tolerance && a.x < b.x);
			return earlier ? candidate : current;
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
