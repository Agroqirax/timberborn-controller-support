using UnityEngine;

namespace ControllerSupport
{
	// Free vertical movement, for the one tool that genuinely needs it: SculptingTerrainBrushTool,
	// whose whole purpose is creating terrain where none exists, so it has to be able to point at
	// empty air with nothing below it. Every other cursor tool steps through the discrete set of
	// heights its own picker can actually resolve to instead - see GamepadCursorLevels, and its class
	// comment for why free voxel-at-a-time movement was wrong for all of them.
	//
	// This class used to hold that shared logic (NaturalTop, ApplyTerrainSnappedHeight, ClampToMap);
	// all of it now lives in GamepadCursorLevels, which derives its answers from the game's own
	// enumerations rather than a hand-rolled voxel scan.
	internal static class GamepadCursorHeight
	{
		// The real bedrock isn't terrain voxel z 0 - TerrainMap.IsTerrainVoxel (what
		// ITerrainService.Underground reads) special-cases any *negative* z as unconditionally solid,
		// and z 0 itself is ordinary, diggable, data-backed terrain like every other height. So the
		// lowest a column can ever be dug down to is z 0 removed, exposing the eternal virtual bedrock
		// at z -1 - the lowest cursor CELL (the empty space resting on top of solid ground) is
		// therefore 0, not 1.
		private const int MinCursorHeight = 0;

		// Two modes. By default (`heightLocked` false) this returns `terrainTop` every call, so the
		// cursor follows the terrain surface exactly like a mouse in every column the player pans
		// across. The instant a height key is pressed it locks: `lockedHeight` is stamped with an
		// absolute z and every later call just returns/adjusts that same number, independent of
		// whatever column the cursor happens to be over.
		//
		// An earlier version tracked a signed *offset* from the terrain top and recomputed
		// z = top + offset every call - that is what produced the reported "moved into a hole dug to
		// bedrock and ended up embedded in bedrock" bug: the offset carried over unclamped whenever
		// heightStep was zero (the clamp only ran inside the `if (heightStep != 0)` branch), so panning
		// from a tall column into one with less headroom below walked z straight past MinCursorHeight
		// with nothing to catch it. An absolute height removes the whole class of bug - there is no
		// "recompute against a different column's baseline" step left to get wrong, and the one moment
		// z legitimately changes is exactly the one moment it is clamped.
		//
		// `mapCeilingExclusive` must be MapSize.TotalSize.z (the construction ceiling), not
		// MaxGameTerrainHeight/ITerrainService.MaxTerrainHeight (the terrain-only ceiling, always
		// lower, and for MaxTerrainHeight also a moving target that grows as the player builds up).
		public static int ApplyFreeHeight(int terrainTop, ref bool heightLocked, ref int lockedHeight, int heightStep,
			int mapCeilingExclusive)
		{
			if (!heightLocked)
			{
				if (heightStep == 0)
				{
					return terrainTop;
				}

				heightLocked = true;
				lockedHeight = terrainTop;
			}

			if (heightStep != 0)
			{
				lockedHeight = Mathf.Clamp(lockedHeight + heightStep, MinCursorHeight, mapCeilingExclusive - 1);
			}

			return lockedHeight;
		}
	}
}
