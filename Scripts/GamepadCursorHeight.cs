using System.Collections.Generic;
using Timberborn.BlockSystem;
using Timberborn.TerrainSystem;
using UnityEngine;

namespace ControllerSupport
{
	// Shared vertical-movement logic for the gamepad's grid cursor. GamepadBuildingPlacementController
	// and GamepadAreaSelectionController both nudge the same cursor in x/y and now also in z
	// (CursorHeightUp/Down) - this is the one place both keep the two height modes they need: free
	// movement clamped to the map's real bounds (every tool for now except the four below - narrower
	// rules come later), and terrain-snapped movement for the tools that must never mark empty air
	// (PlantingTool, CancelPlantingTool, TreeCuttingAreaSelectionTool, TreeCuttingAreaUnselectionTool).
	internal static class GamepadCursorHeight
	{
		// Same ray-origin height CameraServicePlacementPatch used exclusively before this feature -
		// well above any real map height, so a straight-down pick always lands on the true topmost
		// surface with zero risk of starting inside an obstruction's own collider (see that patch's own
		// comment). Still the default whenever the cursor sits at that natural top; only a manually
		// lowered/raised cursor switches the placement ray itself to originate near the chosen cell -
		// see GamepadPlacementState.CursorRayOriginHeight.
		public const float RayHeight = 1000f;

		// The real bedrock isn't terrain voxel z 0 - TerrainMap.IsTerrainVoxel (what
		// ITerrainService.Underground reads) special-cases any *negative* z as unconditionally solid
		// ("if (coordinates.z >= 0) {...} return true;"), and z 0 itself is ordinary, diggable, data-
		// backed terrain like every other height. So the lowest a column can ever be dug down to is z
		// 0 removed, exposing the eternal virtual bedrock at z -1 - the lowest cursor CELL (the empty
		// space resting on top of solid ground) is therefore 0, not 1. The earlier off-by-one here
		// (assuming bedrock itself was z 0) is exactly what the reported "cursor glitches once you're
		// down to bedrock" bug was: NaturalTop's scan stopped at z 0 without finding it "solid" in a
		// fully-dug column and fell through with nothing, and the old MinCursorHeight of 1 refused to
		// let the cursor ever reach the one genuinely valid cell down there.
		private const int MinCursorHeight = 0;

		// Whatever the topmost real surface is in this column right now - terrain OR a block object,
		// whichever is higher. A straight integer voxel scan (IBlockService.AnyObjectAt/
		// ITerrainService.Underground are both plain array lookups on the same discrete grid every
		// other game system uses to place things), not a raycast - no floating-point hit-point
		// rounding to get subtly wrong, and no risk of the ray's own origin starting inside a solid
		// collider and missing it (the reason CameraServicePlacementPatch's ray uses RayHeight instead
		// of the target cell - see that patch's comment). Independent of
		// GamepadPlacementState.Active/CursorRayOriginHeight, so it can't be thrown off by whatever
		// ray origin height a previous frame published.
		//
		// Ignoring block objects entirely here (an earlier version of this only looked at terrain) is
		// exactly what produced the reported "priority/demolish cursor skips heights" bug: a column
		// with a building on it has its real top at the building's own roof, not the terrain under it,
		// so the very first height press already started from the wrong cell and the placement ray it
		// derived (aimed near that wrong height) missed the building's own collider - which
		// AreaSelector's own BlockObjectRaycaster-based pick (see ARCHITECTURE.md/this mod's notes on
		// the game's selection pipeline) would then silently fall through to a totally different
		// terrain-only result for.
		//
		// The loop never has to check negative z itself and still can't fail to find a floor: a fully
		// dug-out column (z 0 removed too) simply falls out the bottom of the loop, and the guaranteed
		// virtual bedrock at z -1 means the correct answer is always exactly 0 in that case - the same
		// value MinCursorHeight now allows, instead of the previous version's stale/wrong fallback.
		public static int NaturalTop(ITerrainService terrainService, IBlockService blockService, int x, int y,
			int mapCeilingExclusive)
		{
			for (var z = mapCeilingExclusive - 1; z >= 0; z--)
			{
				var voxel = new Vector3Int(x, y, z);
				if (terrainService.Underground(voxel) || blockService.AnyObjectAt(voxel))
				{
					return z + 1;
				}
			}

			return 0;
		}

		// Keeps the cursor from ever stepping past the edge of the map, on any side - x/y stay within
		// [0, TerrainSize2D - 1] no matter what the player commands, same "just don't move" rule as a
		// height press against MinCursorHeight/the construction ceiling.
		public static Vector2Int ClampToMap(Vector2Int xy, Vector2Int terrainSize2D)
		{
			return new Vector2Int(Mathf.Clamp(xy.x, 0, terrainSize2D.x - 1), Mathf.Clamp(xy.y, 0, terrainSize2D.y - 1));
		}

		// Free vertical movement - no notion of "terrain" at all, so this happily lands in mid-air,
		// clamped only to the map's real bounds.
		//
		// Two modes, matching what the user asked for: by default (`heightLocked` false) this just
		// returns `naturalTop` every call, unconditionally - the cursor follows the terrain exactly
		// like it did before this feature existed, in every column the player pans across. The instant
		// a height key is actually pressed, it locks: `heightLocked` flips true, `lockedHeight` is
		// stamped with an absolute z, and every call after that (including every later x/y-only frame)
		// just returns/adjusts that same absolute number, completely independent of whatever
		// `naturalTop` happens to be in whatever column the player is currently over.
		//
		// An earlier version tracked a signed *offset* from naturalTop instead of an absolute height,
		// recomputing z = naturalTop + offset every call - that is what actually produced the reported
		// "moved into a hole dug to bedrock and ended up embedded in bedrock" bug: the offset carried
		// over unclamped whenever heightStep was zero (the clamp only ran inside the `if (heightStep
		// != 0)` branch), so panning from a tall column into a column with less headroom below could
		// walk z straight past MinCursorHeight with nothing to catch it. Tracking an absolute height
		// instead removes the whole class of bug - there is no "recompute against a different column's
		// baseline" step left to get wrong, and the one moment z legitimately changes (a real key
		// press) is exactly the one moment it's clamped.
		//
		// `mapCeilingExclusive` must be MapSize.TotalSize.z (the construction ceiling - terrain height
		// plus MaxHeightAboveTerrain, how far building is allowed above the tallest terrain), not
		// MaxGameTerrainHeight/ITerrainService.MaxTerrainHeight (the terrain-only ceiling, always
		// lower, and for MaxTerrainHeight also a moving target that grows as the player builds terrain
		// up) - every caller already passes MapSize.TotalSize.z; this comment is the one place that
		// contract is spelled out.
		public static int ApplyFreeHeight(int naturalTop, ref bool heightLocked, ref int lockedHeight, int heightStep,
			int mapCeilingExclusive)
		{
			if (!heightLocked)
			{
				if (heightStep == 0)
				{
					return naturalTop;
				}

				heightLocked = true;
				lockedHeight = naturalTop;
			}

			if (heightStep != 0)
			{
				lockedHeight = Mathf.Clamp(lockedHeight + heightStep, MinCursorHeight, mapCeilingExclusive - 1);
			}

			return lockedHeight;
		}

		// Snapped movement for planting/tree-cutting - only ever lands on a real "resting on terrain"
		// level (GetAllHeightsInCell), never on the empty air between two platform layers. Re-snaps to
		// the nearest real level every call, so it self-corrects the instant x/y crosses into a column
		// with different layers, then steps one further level per heightStep. `isTopLevel` tells the
		// caller whether the result is the column's topmost level, i.e. exactly where the un-adjusted
		// cursor would already be - callers use that to decide whether the placement ray still gets to
		// use the safe, obstruction-proof RayHeight origin or has to aim at the chosen level directly.
		public static int ApplyTerrainSnappedHeight(ITerrainService terrainService, Vector2Int xy, int currentZ,
			int heightStep, out bool isTopLevel)
		{
			var levels = new List<int>();
			var nearestIndex = 0;
			var nearestDistance = int.MaxValue;
			foreach (var cell in terrainService.GetAllHeightsInCell(xy))
			{
				var distance = Mathf.Abs(cell.z - currentZ);
				if (distance < nearestDistance)
				{
					nearestDistance = distance;
					nearestIndex = levels.Count;
				}

				levels.Add(cell.z);
			}

			if (levels.Count == 0)
			{
				isTopLevel = true;
				return currentZ;
			}

			var targetIndex = Mathf.Clamp(nearestIndex + heightStep, 0, levels.Count - 1);
			isTopLevel = targetIndex == levels.Count - 1;
			return levels[targetIndex];
		}
	}
}
