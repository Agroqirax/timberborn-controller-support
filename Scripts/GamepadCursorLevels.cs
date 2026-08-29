using System.Collections.Generic;
using Timberborn.BlockSystem;
using Timberborn.Brushes;
using Timberborn.LevelVisibilitySystem;
using Timberborn.MapStateSystem;
using Timberborn.TerrainSystem;
using UnityEngine;

namespace ControllerSupport
{
	// Height rules for the gamepad's world cursor, and the column/footprint scans they need.
	//
	// Two regimes, split by whether the tool can act on empty space at all:
	//
	//  - **Free** (GamepadCursorHeight.ApplyFreeHeight) for everything that isn't tied to terrain:
	//    building placement, plain select, builder priority, demolish, deletion, the blueprint tools,
	//    and the sculpting brush. The cursor goes wherever the player puts it, clamped only to the map.
	//  - **Terrain levels** (this class + GamepadCursorLevelTracker) for the tools that can only ever
	//    act on a real terrain surface - planting, un-planting, tree-cutting marking/unmarking, and the
	//    MapEditor height and natural-resource brushes. Those bottom out in TerrainPicker/
	//    TerrainAreaService, which cannot see block objects and refuse anything that isn't ground, so a
	//    cursor floating between layers would just draw somewhere the tool silently declines to act.
	//
	// The thing that makes either work is the ray origin, not the height itself. No Timberborn tool
	// takes a coordinate; they all take a Ray and resolve it by walking DOWN to the first thing they
	// accept. Publishing an origin of exactly `cursor.z + 1` - an integer voxel boundary - makes
	// GridTraversal's first step land on the cursor's own cell and the second on whatever is under it,
	// so the pick is precise rather than "whatever happens to be nearest below". A cursor still sitting
	// at its column's natural top publishes RayHeight instead, which resolves to the same place and
	// keeps the pre-3D behaviour exactly.
	internal class GamepadCursorLevels
	{
		// Ray origin for a cursor that hasn't been moved off its column's natural top: well above any
		// real map height, so the pick travels down from clear air onto whatever is actually there.
		public const float RayHeight = 1000f;

		// How far below the top of the cursor's own cell a non-RayHeight ray starts. It has to be inside
		// that cell rather than exactly on its ceiling, and the reason is a Physics.Raycast detail:
		// starting a ray exactly on a collider's face registers a hit for it at distance zero. With the
		// origin at a flat `cursor.z + 1`, a cursor lowered *into* an object's own cell still hit that
		// object's top face - so two, sometimes three, consecutive cursor heights all selected the same
		// thing (reported against a stack of levees, but true of anything). Starting a whisker inside the
		// cell instead means the ray is already past that face, misses the object it is inside, and
		// finds the next one down - one cursor height, one target.
		//
		// It costs nothing anywhere else. GridTraversal floors the origin, so it still starts in the
		// cursor's own cell; the first voxel it yields is the one below, which is what every consumer
		// wanted from `cursor.z + 1` anyway (that origin just spent its first step on the cursor's own
		// empty cell). And the two patches that recover the cell from a ray origin use RoundToInt, which
		// still lands on `cursor.z + 1` exactly.
		private const float RayOriginInset = 0.001f;

		private readonly ITerrainService _terrainService;
		private readonly IBlockService _blockService;
		private readonly ILevelVisibilityService _levelVisibilityService;
		private readonly BrushShapeIterator _brushShapeIterator;
		private readonly MapSize _mapSize;

		// Reused across frames - these run once per frame per controller and allocating fresh
		// collections every time would be pure garbage.
		private readonly List<int> _levels = new List<int>();
		private readonly HashSet<int> _levelSet = new HashSet<int>();

		public GamepadCursorLevels(ITerrainService terrainService, IBlockService blockService,
			ILevelVisibilityService levelVisibilityService, BrushShapeIterator brushShapeIterator, MapSize mapSize)
		{
			_terrainService = terrainService;
			_blockService = blockService;
			_levelVisibilityService = levelVisibilityService;
			_brushShapeIterator = brushShapeIterator;
			_mapSize = mapSize;
		}

		// Every height in one column where terrain can be acted on: ITerrainService.GetAllHeightsInCell
		// is "z is not a terrain voxel and z-1 is", scanned over the whole column, so an overhang or a
		// cave contributes each of its floors and not just the topmost.
		//
		// The visibility filter mirrors TerrainPicker.IsTerrainVoxel's own `solid.z < MaxVisibleLevel`:
		// with the level slider pulled down, a level above the slice is one the picker would refuse to
		// resolve to, so the cursor must not offer it either.
		//
		// Ascending, and may be empty (a column dug right out with the slider below its floor). The
		// returned list is this instance's own buffer - read it before the next call.
		public List<int> TerrainLevels(Vector2Int xy)
		{
			_levels.Clear();
			AddTerrainLevels(xy);
			return _levels;
		}

		// Same, but for a brush tool: the union over every cell the brush would actually touch, not just
		// the one under the cursor. Without this a small overhang is unreachable whenever the brush's
		// centre column happens not to be under it, even though most of the brush is - the level simply
		// wasn't in the set, so the height key stepped straight past it.
		//
		// Footprint comes from the game's own BrushShapeIterator, driven by the tool's own BrushSize/
		// BrushShape, so it is exactly the set of cells the tool will iterate a moment later - including
		// the round brush's `distance + 0.7 <= size` rule and its own map-bounds filter. Iterated at
		// z 0 purely because the iterator carries the centre's z through untouched and range-checks it;
		// only x/y are wanted here, and z 0 is always in bounds.
		//
		// A level that exists somewhere in the footprint but not in the centre column is still offered,
		// which is the whole point - and is also why GamepadPlacementState.ExactTerrainPick exists. The
		// brush tools derive their own origin from PickTerrainCoordinates on the centre column, which
		// would quietly fall back to the ground under the cursor and undo the choice; that flag makes
		// the picker hand back the cursor's actual cell instead. See TerrainPickerExactCellPatch.
		public List<int> TerrainLevels(Vector2Int xy, int brushSize, BrushShape brushShape)
		{
			_levels.Clear();
			if (brushSize <= 1)
			{
				AddTerrainLevels(xy);
				return _levels;
			}

			_levelSet.Clear();
			foreach (var cell in _brushShapeIterator.IterateShape(new Vector3Int(xy.x, xy.y, 0), brushSize, brushShape))
			{
				foreach (var level in TerrainLevelsIn(new Vector2Int(cell.x, cell.y)))
				{
					_levelSet.Add(level);
				}
			}

			_levels.AddRange(_levelSet);
			_levels.Sort();
			return _levels;
		}

		// Where a free-moving cursor sits when the player hasn't touched the height keys: the empty cell
		// on top of whatever the column's topmost solid thing is, terrain or block object, whichever is
		// higher. That is the cell a mouse would be pointing at, so leaving the height alone behaves
		// exactly as it did before 3D cursor movement existed.
		//
		// A straight integer voxel scan, not a raycast - IBlockService.AnyObjectAt and
		// ITerrainService.Underground are plain lookups on the same discrete grid everything else places
		// things on, with no floating-point hit-point rounding to get subtly wrong and no risk of a ray
		// starting inside a collider and missing it.
		//
		// The loop never has to check negative z and still cannot fail to find a floor: a fully dug-out
		// column falls out of the bottom, and the virtual bedrock at z -1 (TerrainMap.IsTerrainVoxel
		// treats every negative z as solid) means the right answer there is exactly 0.
		public int SurfaceTop(Vector2Int xy)
		{
			for (var z = CeilingExclusive - 1; z >= 0; z--)
			{
				if (IsSolid(new Vector3Int(xy.x, xy.y, z)))
				{
					return z + 1;
				}
			}

			return 0;
		}

		// Where a free-moving cursor sits, for the tools that act on a cell's *contents* rather than on
		// the empty space above a surface - the plain select tool, builder priority, demolish, deletion,
		// the blueprint tools. `cursor.z` for those means "the cell being acted on", which is also the
		// cell the selection box is drawn around (RectangleBoundsDrawer renders a box's floor at world
		// y == its cell's z, so the box occupies the cell, and the thing inside it is what the base game
		// acts on - see SelectionStart, whose Coordinates for a block-object hit is the object's own
		// base cell, not the empty cell above it).
		//
		// So this is the topmost *occupied* cell when the column has an object in it, and the empty cell
		// resting on the terrain when it does not - which is exactly the pair of answers
		// AreaSelector.GetSelectionStart produces from its own two branches (a block-object raycast, and
		// a terrain pick plus face offset). SurfaceTop, one higher, is the right default for building
		// placement instead: a building goes in the empty space *above* what supports it.
		public int HoverCell(Vector2Int xy)
		{
			for (var z = CeilingExclusive - 1; z >= 0; z--)
			{
				var coordinates = new Vector3Int(xy.x, xy.y, z);
				if (_blockService.Contains(coordinates) && _blockService.AnyObjectAt(coordinates))
				{
					return z;
				}

				if (_terrainService.Underground(coordinates))
				{
					return z + 1;
				}
			}

			return 0;
		}

		// The sculpting brush's own free-height seed. Terrain-only rather than SurfaceTop, because that
		// is the surface it actually sculpts against - starting the cursor on a building's roof would
		// put the first Add press somewhere the tool refuses to act.
		public int TerrainTop(Vector2Int xy)
		{
			var levels = TerrainLevels(xy);
			return levels.Count > 0 ? levels[levels.Count - 1] : 0;
		}

		// Ceiling for a free-moving cursor. MapSize.TotalSize.z is the construction ceiling (terrain
		// height plus MaxHeightAboveTerrain), not MaxGameTerrainHeight/ITerrainService.MaxTerrainHeight -
		// the terrain-only ceiling, always lower, and for MaxTerrainHeight also a moving target that
		// grows as the player builds terrain up. Also capped by the level-visibility slice, since a
		// cursor above it is pointing at something the player cannot see.
		public int CeilingExclusive =>
			Mathf.Min(_mapSize.TotalSize.z, _levelVisibilityService.MaxVisibleLevel + 2);

		// SculptingTerrainBrushTool.IsValidCandidateBlock refuses any block at `z >= MaxVisibleLevel + 1`
		// or at `z >= MapSize.MaxMapEditorTerrainHeight` - terrain, unlike buildings, cannot go into the
		// build-above-terrain headroom TotalSize.z includes. Letting the cursor climb past either just
		// parks it somewhere the tool silently declines to act, which reads as the height key having
		// stopped working.
		public int SculptCeilingExclusive =>
			Mathf.Min(_levelVisibilityService.MaxVisibleLevel + 1, _mapSize.MaxMapEditorTerrainHeight);

		// The ray origin for a cursor that has been moved off its column's natural top, i.e. one whose
		// exact cell has to be respected rather than re-derived. See RayOriginInset.
		public static float RayOriginFor(int cursorZ)
		{
			return cursorZ + 1f - RayOriginInset;
		}

		// Keeps the cursor from ever stepping past the edge of the map, on any side.
		public Vector2Int ClampToMap(Vector2Int xy)
		{
			var size = _mapSize.TerrainSize2D;
			return new Vector2Int(Mathf.Clamp(xy.x, 0, size.x - 1), Mathf.Clamp(xy.y, 0, size.y - 1));
		}

		private void AddTerrainLevels(Vector2Int xy)
		{
			foreach (var level in TerrainLevelsIn(xy))
			{
				_levels.Add(level);
			}
		}

		private IEnumerable<int> TerrainLevelsIn(Vector2Int xy)
		{
			var maxVisible = _levelVisibilityService.MaxVisibleLevel;
			foreach (var cell in _terrainService.GetAllHeightsInCell(xy))
			{
				if (cell.z <= maxVisible)
				{
					yield return cell.z;
				}
			}
		}

		private bool IsSolid(Vector3Int coordinates)
		{
			return _terrainService.Underground(coordinates)
				|| (_blockService.Contains(coordinates) && _blockService.AnyObjectAt(coordinates));
		}
	}

	// Which terrain level the cursor is on, for the terrain-only tools. Deliberately NOT "an index into
	// the current column's list" and NOT "a held absolute z" - both were tried and both are wrong in an
	// obvious way. An index means panning sideways silently teleports the cursor to some unrelated
	// height whenever two columns have different numbers of levels; a held absolute z means panning off
	// an overhang strands the cursor at a height with no terrain at it, where the tool does nothing.
	//
	// What is held instead is a *preference*: the z the player last explicitly asked for. Every frame
	// the cursor snaps to whichever level of the current column (or brush footprint) is nearest that
	// preference, so crossing a gap drops to the ground and crossing back onto the overhang returns to
	// it. Until the first height press there is no preference and the cursor takes the topmost level,
	// which is byte-for-byte the pre-3D behaviour.
	internal struct GamepadCursorLevelTracker
	{
		private bool _hasPreference;
		private int _preferred;

		public void Reset()
		{
			_hasPreference = false;
			_preferred = 0;
		}

		// Seeds the preference from somewhere other than a height press - the mouse's own cell during a
		// mouse->gamepad handoff, so picking the stick back up resumes at the height the mouse was last
		// pointing at rather than jumping to the top of the column.
		public void Prefer(int z)
		{
			_hasPreference = true;
			_preferred = z;
		}

		// `levels` must be ascending. `fallback` is returned only for a column with no levels at all.
		// `isTopLevel` says whether the result is the highest available, which is what lets the caller
		// keep using the safe RayHeight ray origin for the overwhelmingly common default case.
		public int Apply(List<int> levels, int step, int fallback, out bool isTopLevel)
		{
			if (levels.Count == 0)
			{
				isTopLevel = true;
				return fallback;
			}

			var index = _hasPreference ? NearestIndex(levels, _preferred) : levels.Count - 1;
			if (step != 0)
			{
				index = Mathf.Clamp(index + step, 0, levels.Count - 1);
				_hasPreference = true;
				_preferred = levels[index];
			}

			isTopLevel = index == levels.Count - 1;
			return levels[index];
		}

		// Ties go to the higher level: <= rather than < while scanning ascending. A cursor sitting
		// exactly between two levels after crossing into a new column should surface, not sink - sinking
		// reads as the cursor falling through the floor the player was just standing on.
		private static int NearestIndex(List<int> levels, int target)
		{
			var best = 0;
			var bestDistance = int.MaxValue;
			for (var i = 0; i < levels.Count; i++)
			{
				var distance = Mathf.Abs(levels[i] - target);
				if (distance <= bestDistance)
				{
					bestDistance = distance;
					best = i;
				}
			}

			return best;
		}
	}
}
