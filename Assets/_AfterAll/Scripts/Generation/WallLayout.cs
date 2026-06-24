using System;
using System.Collections.Generic;
using UnityEngine;

namespace AfterAll.Generation
{
    /// <summary>
    /// Converts a BspResult into a ChunkSpec using per-room wall faces.
    ///
    /// Each room owns four wall faces. Shared edges are deduplicated so only one
    /// physical wall is spawned per boundary segment.
    ///
    /// Junction rule (dominant axis):
    ///   N/S faces (horizontal) — full room width, never trimmed.
    ///   E/W faces (vertical)   — trimmed inward by halfT at each Z end so they
    ///                            butt cleanly against the N/S faces at every corner.
    /// </summary>
    public static class WallLayout
    {
        private const float kMinStubBetweenOpenings = 0.5f;
        private const float kEps                    = 0.001f;

        private enum Face { South, North, West, East }

        // ──────────────────────────────────────────────────────────────────────────
        //  Public entry-point
        // ──────────────────────────────────────────────────────────────────────────

        public static ChunkSpec Build(BspResult bsp, MapConfig config, Rng rng, ChunkCoord? coord = null)
        {
            float halfT     = config.WallThickness * 0.5f;
            float chunkSize = config.ChunkSize;

            // Step 1 — openings on full BSP boundaries (unchanged RNG sequence).
            var boundaryOpenings = new Dictionary<BoundaryKey, IReadOnlyList<OpeningSpec>>();
            foreach (var boundary in bsp.Boundaries)
            {
                boundaryOpenings[new BoundaryKey(boundary)] =
                    GenerateOpenings(boundary, config, rng);
            }

            // Pre-compute stitched openings for each chunk border (shared with neighbours).
            IReadOnlyDictionary<EdgeStitcher.Border, IReadOnlyList<OpeningSpec>> borderOpenings = null;
            if (coord.HasValue)
                borderOpenings = BuildBorderOpenings(coord.Value, chunkSize, config);

            // Step 2 — one wall face per room edge, deduplicated by geometry.
            var wallMap = new Dictionary<WallKey, WallSpec>();

            foreach (var room in bsp.Rooms)
            {
                AddRoomFace(wallMap, room, Face.South, halfT, chunkSize,
                            bsp.Boundaries, boundaryOpenings, borderOpenings, coord);
                AddRoomFace(wallMap, room, Face.North, halfT, chunkSize,
                            bsp.Boundaries, boundaryOpenings, borderOpenings, coord);
                AddRoomFace(wallMap, room, Face.West, halfT, chunkSize,
                            bsp.Boundaries, boundaryOpenings, borderOpenings, coord);
                AddRoomFace(wallMap, room, Face.East, halfT, chunkSize,
                            bsp.Boundaries, boundaryOpenings, borderOpenings, coord);
            }

            var walls = new List<WallSpec>(wallMap.Values);

            return new ChunkSpec(
                new Rect(0f, 0f, chunkSize, chunkSize),
                config.WallHeight,
                config.SlabThickness,
                walls,
                bsp.Rooms);
        }

        // ──────────────────────────────────────────────────────────────────────────
        //  Per-room face generation
        // ──────────────────────────────────────────────────────────────────────────

        private static Dictionary<EdgeStitcher.Border, IReadOnlyList<OpeningSpec>> BuildBorderOpenings(
            ChunkCoord coord, float chunkSize, MapConfig config)
        {
            var result = new Dictionary<EdgeStitcher.Border, IReadOnlyList<OpeningSpec>>();

            foreach (EdgeStitcher.Border border in Enum.GetValues(typeof(EdgeStitcher.Border)))
            {
                int seed = EdgeStitcher.BorderSeed(config.Seed, coord.X, coord.Z, border);
                result[border] = EdgeStitcher.GenerateBorderOpenings(chunkSize, seed, config);
            }

            return result;
        }

        private static void AddRoomFace(
            Dictionary<WallKey, WallSpec> wallMap,
            RoomSpec room,
            Face face,
            float halfT,
            float chunkSize,
            IReadOnlyList<BspBoundary> bspBoundaries,
            Dictionary<BoundaryKey, IReadOnlyList<OpeningSpec>> boundaryOpenings,
            IReadOnlyDictionary<EdgeStitcher.Border, IReadOnlyList<OpeningSpec>> borderOpenings,
            ChunkCoord? coord)
        {
            var b = room.Bounds;

            BspBoundary faceBoundary;
            bool isVertical;
            float faceCoord;
            float rangeMin;
            float rangeMax;

            switch (face)
            {
                case Face.South:
                    faceBoundary = new BspBoundary(
                        new Vector2(b.xMin, b.yMin), new Vector2(b.xMax, b.yMin));
                    isVertical = false;
                    faceCoord  = b.yMin;
                    rangeMin   = b.xMin;
                    rangeMax   = b.xMax;
                    break;

                case Face.North:
                    faceBoundary = new BspBoundary(
                        new Vector2(b.xMin, b.yMax), new Vector2(b.xMax, b.yMax));
                    isVertical = false;
                    faceCoord  = b.yMax;
                    rangeMin   = b.xMin;
                    rangeMax   = b.xMax;
                    break;

                case Face.West:
                    faceBoundary = new BspBoundary(
                        new Vector2(b.xMin, b.yMin + halfT), new Vector2(b.xMin, b.yMax - halfT));
                    isVertical = true;
                    faceCoord  = b.xMin;
                    rangeMin   = b.yMin;
                    rangeMax   = b.yMax;
                    break;

                default: // East
                    faceBoundary = new BspBoundary(
                        new Vector2(b.xMax, b.yMin + halfT), new Vector2(b.xMax, b.yMax - halfT));
                    isVertical = true;
                    faceCoord  = b.xMax;
                    rangeMin   = b.yMin;
                    rangeMax   = b.yMax;
                    break;
            }

            // Skip degenerate faces (room thinner than wall thickness).
            if (faceBoundary.Length < 0.05f) return;

            var key = new WallKey(faceBoundary);
            if (wallMap.ContainsKey(key)) return;

            IReadOnlyList<OpeningSpec> openings;
            var bspBoundary = FindMatchingBoundary(bspBoundaries, faceCoord, rangeMin, rangeMax, isVertical);

            if (bspBoundary.HasValue)
            {
                var bspOpenings = boundaryOpenings[new BoundaryKey(bspBoundary.Value)];
                openings = ClipOpeningsToFace(bspOpenings, bspBoundary.Value,
                                              faceBoundary, isVertical, halfT, rangeMin, rangeMax);
            }
            else if (coord.HasValue && borderOpenings != null &&
                     TryGetChunkBorder(face, b, chunkSize, out var border))
            {
                var edgeOpenings = borderOpenings[border];
                float faceStart = isVertical ? rangeMin + halfT : rangeMin;
                float faceEnd   = isVertical ? rangeMax - halfT : rangeMax;
                openings = EdgeStitcher.ClipToFaceSegment(edgeOpenings, faceStart, faceEnd);
            }
            else
            {
                // Standalone chunk perimeter — solid wall.
                openings = Array.Empty<OpeningSpec>();
            }

            wallMap[key] = new WallSpec(faceBoundary, openings, room.RoomId);
        }

        // ──────────────────────────────────────────────────────────────────────────
        //  Boundary lookup & opening clip
        // ──────────────────────────────────────────────────────────────────────────

        private static BspBoundary? FindMatchingBoundary(
            IReadOnlyList<BspBoundary> boundaries,
            float coord, float rangeMin, float rangeMax, bool isVertical)
        {
            foreach (var b in boundaries)
            {
                if (b.IsVertical != isVertical) continue;

                float bCoord = isVertical ? b.Start.x : b.Start.y;
                if (!Mathf.Approximately(bCoord, coord)) continue;

                float bMin = isVertical
                    ? Mathf.Min(b.Start.y, b.End.y)
                    : Mathf.Min(b.Start.x, b.End.x);
                float bMax = isVertical
                    ? Mathf.Max(b.Start.y, b.End.y)
                    : Mathf.Max(b.Start.x, b.End.x);

                if (bMin <= rangeMin + kEps && bMax >= rangeMax - kEps)
                    return b;
            }

            return null;
        }

        private static bool TryGetChunkBorder(
            Face face, Rect roomBounds, float chunkSize, out EdgeStitcher.Border border)
        {
            switch (face)
            {
                case Face.South when Mathf.Approximately(roomBounds.yMin, 0f):
                    border = EdgeStitcher.Border.South;
                    return true;
                case Face.North when Mathf.Approximately(roomBounds.yMax, chunkSize):
                    border = EdgeStitcher.Border.North;
                    return true;
                case Face.West when Mathf.Approximately(roomBounds.xMin, 0f):
                    border = EdgeStitcher.Border.West;
                    return true;
                case Face.East when Mathf.Approximately(roomBounds.xMax, chunkSize):
                    border = EdgeStitcher.Border.East;
                    return true;
                default:
                    border = default;
                    return false;
            }
        }

        /// <summary>
        /// Converts boundary-space openings to face-local offsets, clipping to the
        /// portion of the boundary that this room face occupies.
        /// </summary>
        private static IReadOnlyList<OpeningSpec> ClipOpeningsToFace(
            IReadOnlyList<OpeningSpec> bspOpenings,
            BspBoundary bspBoundary,
            BspBoundary faceBoundary,
            bool isVertical,
            float halfT,
            float rangeMin,
            float rangeMax)
        {
            if (bspOpenings.Count == 0) return bspOpenings;

            float bspAxisStart = isVertical
                ? Mathf.Min(bspBoundary.Start.y, bspBoundary.End.y)
                : Mathf.Min(bspBoundary.Start.x, bspBoundary.End.x);

            // World-space extent of this face along the wall axis.
            float faceWorldStart = isVertical ? rangeMin + halfT : rangeMin;
            float faceWorldEnd   = isVertical ? rangeMax - halfT : rangeMax;

            var result = new List<OpeningSpec>(bspOpenings.Count);

            foreach (var o in bspOpenings)
            {
                float openStart = bspAxisStart + o.Offset;
                float openEnd   = openStart + o.Width;

                float clipStart = Mathf.Max(openStart, faceWorldStart);
                float clipEnd   = Mathf.Min(openEnd,   faceWorldEnd);

                if (clipEnd - clipStart >= 0.1f)
                {
                    result.Add(new OpeningSpec(
                        clipStart - faceWorldStart,
                        clipEnd   - clipStart,
                        o.Type));
                }
            }

            return result;
        }

        // ──────────────────────────────────────────────────────────────────────────
        //  Dedup keys
        // ──────────────────────────────────────────────────────────────────────────

        private readonly struct WallKey : IEquatable<WallKey>
        {
            private readonly int _ax, _ay, _bx, _by;

            public WallKey(BspBoundary b)
            {
                _ax = Q(b.Start.x); _ay = Q(b.Start.y);
                _bx = Q(b.End.x);   _by = Q(b.End.y);
            }

            private static int Q(float v) => Mathf.RoundToInt(v * 1000f);

            public bool Equals(WallKey other) =>
                _ax == other._ax && _ay == other._ay && _bx == other._bx && _by == other._by;

            public override bool Equals(object obj) => obj is WallKey k && Equals(k);
            public override int GetHashCode() => HashCode.Combine(_ax, _ay, _bx, _by);
        }

        private readonly struct BoundaryKey : IEquatable<BoundaryKey>
        {
            private readonly int _ax, _ay, _bx, _by;

            public BoundaryKey(BspBoundary b)
            {
                _ax = Q(b.Start.x); _ay = Q(b.Start.y);
                _bx = Q(b.End.x);   _by = Q(b.End.y);
            }

            private static int Q(float v) => Mathf.RoundToInt(v * 1000f);

            public bool Equals(BoundaryKey other) =>
                _ax == other._ax && _ay == other._ay && _bx == other._bx && _by == other._by;

            public override bool Equals(object obj) => obj is BoundaryKey k && Equals(k);
            public override int GetHashCode() => HashCode.Combine(_ax, _ay, _bx, _by);
        }

        // ──────────────────────────────────────────────────────────────────────────
        //  Opening generation
        // ──────────────────────────────────────────────────────────────────────────

        private static IReadOnlyList<OpeningSpec> GenerateOpenings(
            BspBoundary boundary, MapConfig config, Rng rng)
        {
            float length = boundary.Length;
            float margin = config.OpeningEdgeMargin;

            float usableStart = margin;
            float usableEnd   = length - margin;

            if (usableEnd - usableStart < config.OpeningMinWidth)
            {
                float centre = length * 0.5f;
                float halfW  = Mathf.Min(config.OpeningMinWidth * 0.5f, length * 0.35f);
                return new[] { new OpeningSpec(centre - halfW, halfW * 2f) };
            }

            int count = rng.Range(config.MinOpeningsPerBoundary, config.MaxOpeningsPerBoundary + 1);
            count = Mathf.Max(1, count);

            var openings = new List<OpeningSpec>(count);
            float cursor  = usableStart;

            for (int i = 0; i < count; i++)
            {
                float remaining = usableEnd - cursor;
                if (remaining < config.OpeningMinWidth) break;

                float maxWidth  = Mathf.Min(config.OpeningMaxWidth, remaining);
                float width     = rng.Range(config.OpeningMinWidth, maxWidth);

                float gapRoom   = remaining - width;
                float gapBefore = gapRoom > 0f ? rng.Range(0f, gapRoom) : 0f;

                openings.Add(new OpeningSpec(cursor + gapBefore, width));
                cursor = cursor + gapBefore + width + kMinStubBetweenOpenings;
            }

            return openings;
        }

        // ──────────────────────────────────────────────────────────────────────────
        //  Utility — used by ConnectivityPass
        // ──────────────────────────────────────────────────────────────────────────

        internal static IReadOnlyList<OpeningSpec> MergeOpenings(
            IReadOnlyList<OpeningSpec> existing, List<OpeningSpec> additional, float wallLength)
        {
            _ = wallLength;

            var all = new List<OpeningSpec>(existing.Count + additional.Count);
            all.AddRange(existing);
            all.AddRange(additional);

            if (all.Count == 0) return Array.Empty<OpeningSpec>();

            all.Sort(static (a, b) => a.Offset.CompareTo(b.Offset));

            var merged = new List<OpeningSpec>(all.Count);
            float start = all[0].Offset;
            float end   = all[0].EndOffset;

            for (int i = 1; i < all.Count; i++)
            {
                if (all[i].Offset <= end + 0.001f)
                    end = Mathf.Max(end, all[i].EndOffset);
                else
                {
                    merged.Add(new OpeningSpec(start, end - start));
                    start = all[i].Offset;
                    end   = all[i].EndOffset;
                }
            }
            merged.Add(new OpeningSpec(start, end - start));

            return merged;
        }

        /// <summary>
        /// Returns true when <paramref name="wall"/> lies on a BSP partition boundary
        /// (as opposed to being a chunk-perimeter segment with no BSP backing).
        /// </summary>
        internal static bool IsInteriorBspWall(
            WallSpec wall, IReadOnlyList<BspBoundary> bspBoundaries)
        {
            var b = wall.Boundary;
            float coord    = b.IsVertical ? b.Start.x : b.Start.y;
            float rangeMin = b.IsVertical
                ? Mathf.Min(b.Start.y, b.End.y)
                : Mathf.Min(b.Start.x, b.End.x);
            float rangeMax = b.IsVertical
                ? Mathf.Max(b.Start.y, b.End.y)
                : Mathf.Max(b.Start.x, b.End.x);

            return FindMatchingBoundary(bspBoundaries, coord, rangeMin, rangeMax, b.IsVertical).HasValue
                || OverlapsAnyBspBoundary(b, bspBoundaries);
        }

        /// <summary>
        /// Finds every wall segment that overlaps the same BSP boundary as <paramref name="wall"/>.
        /// Used by ConnectivityPass to patch all collinear segments at once.
        /// </summary>
        internal static List<int> FindWallIndicesOnSameBspBoundary(
            WallSpec wall, IReadOnlyList<WallSpec> walls, IReadOnlyList<BspBoundary> bspBoundaries)
        {
            var result = new List<int>();
            var b = wall.Boundary;

            foreach (var bsp in bspBoundaries)
            {
                if (bsp.IsVertical != b.IsVertical) continue;

                float bspCoord = bsp.IsVertical ? bsp.Start.x : bsp.Start.y;
                float wallCoord = b.IsVertical ? b.Start.x : b.Start.y;
                if (!Mathf.Approximately(bspCoord, wallCoord)) continue;

                float bspMin = bsp.IsVertical
                    ? Mathf.Min(bsp.Start.y, bsp.End.y)
                    : Mathf.Min(bsp.Start.x, bsp.End.x);
                float bspMax = bsp.IsVertical
                    ? Mathf.Max(bsp.Start.y, bsp.End.y)
                    : Mathf.Max(bsp.Start.x, bsp.End.x);

                float wallMin = b.IsVertical
                    ? Mathf.Min(b.Start.y, b.End.y)
                    : Mathf.Min(b.Start.x, b.End.x);
                float wallMax = b.IsVertical
                    ? Mathf.Max(b.Start.y, b.End.y)
                    : Mathf.Max(b.Start.x, b.End.x);

                if (wallMin >= bspMax - kEps || wallMax <= bspMin + kEps) continue;

                // Wall overlaps this BSP boundary — collect all segments on same line.
                for (int i = 0; i < walls.Count; i++)
                {
                    var w = walls[i].Boundary;
                    if (w.IsVertical != bsp.IsVertical) continue;
                    float wc = w.IsVertical ? w.Start.x : w.Start.y;
                    if (!Mathf.Approximately(wc, bspCoord)) continue;

                    float wMin = w.IsVertical
                        ? Mathf.Min(w.Start.y, w.End.y)
                        : Mathf.Min(w.Start.x, w.End.x);
                    float wMax = w.IsVertical
                        ? Mathf.Max(w.Start.y, w.End.y)
                        : Mathf.Max(w.Start.x, w.End.x);

                    if (wMin >= bspMax - kEps || wMax <= bspMin + kEps) continue;
                    if (!result.Contains(i)) result.Add(i);
                }

                return result;
            }

            return result;
        }

        private static bool OverlapsAnyBspBoundary(
            BspBoundary wall, IReadOnlyList<BspBoundary> bspBoundaries)
        {
            foreach (var bsp in bspBoundaries)
            {
                if (bsp.IsVertical != wall.IsVertical) continue;

                float bspCoord  = bsp.IsVertical ? bsp.Start.x : bsp.Start.y;
                float wallCoord = wall.IsVertical ? wall.Start.x : wall.Start.y;
                if (!Mathf.Approximately(bspCoord, wallCoord)) continue;

                float bspMin = bsp.IsVertical
                    ? Mathf.Min(bsp.Start.y, bsp.End.y)
                    : Mathf.Min(bsp.Start.x, bsp.End.x);
                float bspMax = bsp.IsVertical
                    ? Mathf.Max(bsp.Start.y, bsp.End.y)
                    : Mathf.Max(bsp.Start.x, bsp.End.x);
                float wallMin = wall.IsVertical
                    ? Mathf.Min(wall.Start.y, wall.End.y)
                    : Mathf.Min(wall.Start.x, wall.End.x);
                float wallMax = wall.IsVertical
                    ? Mathf.Max(wall.Start.y, wall.End.y)
                    : Mathf.Max(wall.Start.x, wall.End.x);

                if (wallMin < bspMax - kEps && wallMax > bspMin + kEps)
                    return true;
            }

            return false;
        }
    }
}
