using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace Sledge.BspEditor.Terrain
{
    public enum SculptMode
    {
        Raise,
        Lower,
        Flatten,
        Smooth,
        Set
    }

    public enum BrushFalloff
    {
        Smooth,
        Linear,
        Constant
    }

    /// <summary>How the coarse collision grid takes its heights from the terrain.</summary>
    public enum CollisionSampling
    {
        /// <summary>The height of the terrain at the block corner.</summary>
        Corner,

        /// <summary>The lowest height around the block corner, so the collision never rises above the surface.</summary>
        Lowest,

        /// <summary>The highest height around the block corner, so the collision never sinks below the surface.</summary>
        Highest
    }

    public enum TerrainFaceKind
    {
        Top,
        Bottom,
        Side
    }

    /// <summary>One face of a generated brush. Vertices are wound the way Sledge's Plane expects (clockwise seen from outside).</summary>
    public sealed class TerrainFace
    {
        public Vector3[] Vertices;
        public TerrainFaceKind Kind;
    }

    /// <summary>A convex brush: a prism with a sloped top and a flat bottom, and vertical sides.</summary>
    public sealed class TerrainBrush
    {
        public List<TerrainFace> Faces = new List<TerrainFace>();
    }

    /// <summary>
    /// A height grid. Heights are whole levels, so everything stays on the grid: a vertex is at
    /// Origin + (x * CellSize, y * CellSize, Level * LevelHeight). The brushes are generated from it.
    /// This file has no editor dependencies, so it can be tested on its own.
    /// </summary>
    public sealed class Heightfield
    {
        public int CellsX { get; set; }
        public int CellsY { get; set; }

        /// <summary>Width and depth of a cell in world units (a multiple of the grid).</summary>
        public float CellSize { get; set; } = 16f;

        /// <summary>World units per height level (a multiple of the grid).</summary>
        public float LevelHeight { get; set; } = 8f;

        /// <summary>World position of vertex (0, 0) at level 0.</summary>
        public Vector3 Origin { get; set; }

        /// <summary>How many levels thick the lowest point of the terrain is, down to the flat bottom of the brushes.</summary>
        public int ThicknessLevels { get; set; } = 2;

        /// <summary>(CellsX + 1) * (CellsY + 1) levels, row by row.</summary>
        public int[] Levels { get; set; }

        /// <summary>Goes up on every change, so a renderer can tell when its cached mesh is out of date.</summary>
        public int Version { get; private set; }

        public void Touch()
        {
            Version++;
        }

        public Heightfield()
        {
            Levels = new int[0];
        }

        public Heightfield(int cellsX, int cellsY, float cellSize, float levelHeight, Vector3 origin, int level = 0)
        {
            CellsX = cellsX;
            CellsY = cellsY;
            CellSize = cellSize;
            LevelHeight = levelHeight;
            Origin = origin;
            Levels = new int[(cellsX + 1) * (cellsY + 1)];
            if (level != 0) for (var i = 0; i < Levels.Length; i++) Levels[i] = level;
        }

        public int VertsX => CellsX + 1;
        public int VertsY => CellsY + 1;

        public int Index(int x, int y) => y * VertsX + x;
        public int GetLevel(int x, int y) => Levels[Index(x, y)];
        public void SetLevel(int x, int y, int level) { Levels[Index(x, y)] = level; Version++; }

        public Vector3 Vertex(int x, int y)
        {
            return new Vector3(Origin.X + x * CellSize, Origin.Y + y * CellSize, Origin.Z + GetLevel(x, y) * LevelHeight);
        }

        public Heightfield Clone()
        {
            return new Heightfield
            {
                CellsX = CellsX,
                CellsY = CellsY,
                CellSize = CellSize,
                LevelHeight = LevelHeight,
                Origin = Origin,
                ThicknessLevels = ThicknessLevels,
                Levels = (int[]) Levels.Clone()
            };
        }

        public int MinLevel => Levels.Length == 0 ? 0 : Levels.Min();
        public int MaxLevel => Levels.Length == 0 ? 0 : Levels.Max();

        /// <summary>World height of the flat bottom shared by every brush.</summary>
        public float BottomZ => Origin.Z + (MinLevel - Math.Max(1, ThicknessLevels)) * LevelHeight;

        public (Vector3 Min, Vector3 Max) Bounds()
        {
            return (
                new Vector3(Origin.X, Origin.Y, BottomZ),
                new Vector3(Origin.X + CellsX * CellSize, Origin.Y + CellsY * CellSize, Origin.Z + MaxLevel * LevelHeight)
            );
        }

        public bool SameShapeAs(Heightfield other)
        {
            return other != null && other.CellsX == CellsX && other.CellsY == CellsY && other.Levels.SequenceEqual(Levels);
        }

        #region Triangulation

        /// <summary>
        /// Which way a cell is split. True: the diagonal runs from the (0, 0) corner to the (1, 1) corner.
        /// The diagonal with the smaller height difference is used, so ridges are not cut across; the choice only depends on the
        /// four corner levels, so the preview and the brushes always agree.
        /// </summary>
        public bool DiagonalA(int cx, int cy)
        {
            var h00 = GetLevel(cx, cy);
            var h10 = GetLevel(cx + 1, cy);
            var h01 = GetLevel(cx, cy + 1);
            var h11 = GetLevel(cx + 1, cy + 1);
            var a = Math.Abs(h00 - h11);
            var b = Math.Abs(h10 - h01);
            if (a != b) return a < b;
            return (cx + cy) % 2 == 0;
        }

        /// <summary>The two triangles of a cell, as vertex coordinates (x, y) of the grid.</summary>
        public IEnumerable<(int X, int Y)[]> CellTriangles(int cx, int cy)
        {
            if (DiagonalA(cx, cy))
            {
                yield return new[] { (cx, cy), (cx + 1, cy), (cx + 1, cy + 1) };
                yield return new[] { (cx, cy), (cx + 1, cy + 1), (cx, cy + 1) };
            }
            else
            {
                yield return new[] { (cx, cy), (cx + 1, cy), (cx, cy + 1) };
                yield return new[] { (cx + 1, cy), (cx + 1, cy + 1), (cx, cy + 1) };
            }
        }

        /// <summary>All surface triangles in world space, for the preview and for ray casts.</summary>
        public IEnumerable<Vector3[]> SurfaceTriangles()
        {
            for (var cy = 0; cy < CellsY; cy++)
            {
                for (var cx = 0; cx < CellsX; cx++)
                {
                    foreach (var t in CellTriangles(cx, cy))
                    {
                        yield return new[] { Vertex(t[0].X, t[0].Y), Vertex(t[1].X, t[1].Y), Vertex(t[2].X, t[2].Y) };
                    }
                }
            }
        }

        #endregion

        #region Ray casting

        /// <summary>The nearest point where the ray crosses the surface.</summary>
        public bool RayCast(Vector3 start, Vector3 end, out Vector3 hit)
        {
            hit = default;
            var dir = end - start;
            if (dir.LengthSquared() < 1e-12f) return false;
            dir = Vector3.Normalize(dir);
            var length = (end - start).Length();

            var best = float.MaxValue;
            foreach (var t in SurfaceTriangles())
            {
                var d = RayTriangle(start, dir, t[0], t[1], t[2]);
                if (d >= 0 && d <= length && d < best) best = d;
            }

            if (best == float.MaxValue) return false;
            hit = start + dir * best;
            return true;
        }

        private static float RayTriangle(Vector3 o, Vector3 d, Vector3 a, Vector3 b, Vector3 c)
        {
            const float eps = 1e-6f;
            var e1 = b - a;
            var e2 = c - a;
            var p = Vector3.Cross(d, e2);
            var det = Vector3.Dot(e1, p);
            if (Math.Abs(det) < eps) return -1;
            var inv = 1f / det;
            var tv = o - a;
            var u = Vector3.Dot(tv, p) * inv;
            if (u < 0 || u > 1) return -1;
            var q = Vector3.Cross(tv, e1);
            var v = Vector3.Dot(d, q) * inv;
            if (v < 0 || u + v > 1) return -1;
            return Vector3.Dot(e2, q) * inv;
        }

        /// <summary>The surface height at a world position, or null outside the grid.</summary>
        public float? HeightAt(float x, float y)
        {
            var fx = (x - Origin.X) / CellSize;
            var fy = (y - Origin.Y) / CellSize;
            if (fx < 0 || fy < 0 || fx > CellsX || fy > CellsY) return null;
            var cx = Math.Min((int) Math.Floor(fx), CellsX - 1);
            var cy = Math.Min((int) Math.Floor(fy), CellsY - 1);
            foreach (var t in CellTriangles(cx, cy))
            {
                var a = Vertex(t[0].X, t[0].Y);
                var b = Vertex(t[1].X, t[1].Y);
                var c = Vertex(t[2].X, t[2].Y);
                var z = TriangleHeight(a, b, c, x, y);
                if (z.HasValue) return z;
            }
            return null;
        }

        private static float? TriangleHeight(Vector3 a, Vector3 b, Vector3 c, float x, float y)
        {
            var d = (b.Y - c.Y) * (a.X - c.X) + (c.X - b.X) * (a.Y - c.Y);
            if (Math.Abs(d) < 1e-9f) return null;
            var l1 = ((b.Y - c.Y) * (x - c.X) + (c.X - b.X) * (y - c.Y)) / d;
            var l2 = ((c.Y - a.Y) * (x - c.X) + (a.X - c.X) * (y - c.Y)) / d;
            var l3 = 1 - l1 - l2;
            const float e = -1e-4f;
            if (l1 < e || l2 < e || l3 < e) return null;
            return l1 * a.Z + l2 * b.Z + l3 * c.Z;
        }

        #endregion

        #region Sculpting

        private static float Weight(float distance, float radius, BrushFalloff falloff)
        {
            if (distance > radius) return 0;
            var t = radius <= 0 ? 0 : distance / radius;
            switch (falloff)
            {
                case BrushFalloff.Constant: return 1;
                case BrushFalloff.Linear: return 1 - t;
                default:
                    // Smoothstep, so the edge of the brush blends in
                    var s = 1 - t;
                    return s * s * (3 - 2 * s);
            }
        }

        /// <summary>
        /// One brush stamp at a world position (x, y). Strength is in levels: a vertex under the full weight of the brush moves
        /// by that many levels, and the rest are rounded to whole levels, so the terrain never leaves the grid.
        /// Flatten and Set move towards (or to) the target level.
        /// </summary>
        public bool ApplyBrush(float x, float y, float radius, SculptMode mode, float strength, BrushFalloff falloff, int targetLevel = 0)
        {
            var changed = false;
            var minX = Math.Max(0, (int) Math.Floor((x - radius - Origin.X) / CellSize));
            var maxX = Math.Min(CellsX, (int) Math.Ceiling((x + radius - Origin.X) / CellSize));
            var minY = Math.Max(0, (int) Math.Floor((y - radius - Origin.Y) / CellSize));
            var maxY = Math.Min(CellsY, (int) Math.Ceiling((y + radius - Origin.Y) / CellSize));

            var before = (int[]) Levels.Clone();

            for (var vy = minY; vy <= maxY; vy++)
            {
                for (var vx = minX; vx <= maxX; vx++)
                {
                    var px = Origin.X + vx * CellSize - x;
                    var py = Origin.Y + vy * CellSize - y;
                    var w = Weight((float) Math.Sqrt(px * px + py * py), radius, falloff);
                    if (w <= 0) continue;

                    var current = before[Index(vx, vy)];
                    var next = current;
                    switch (mode)
                    {
                        case SculptMode.Raise:
                            next = current + (int) Math.Round(strength * w, MidpointRounding.AwayFromZero);
                            break;
                        case SculptMode.Lower:
                            next = current - (int) Math.Round(strength * w, MidpointRounding.AwayFromZero);
                            break;
                        case SculptMode.Flatten:
                            next = current + (int) Math.Round((targetLevel - current) * Math.Min(1f, w * Math.Max(0.1f, strength)), MidpointRounding.AwayFromZero);
                            break;
                        case SculptMode.Set:
                            next = targetLevel;
                            break;
                        case SculptMode.Smooth:
                            var avg = NeighbourAverage(before, vx, vy);
                            next = current + (int) Math.Round((avg - current) * Math.Min(1f, w * Math.Max(0.25f, strength)), MidpointRounding.AwayFromZero);
                            break;
                    }

                    if (next != current)
                    {
                        Levels[Index(vx, vy)] = next;
                        changed = true;
                    }
                }
            }

            if (changed) Version++;
            return changed;
        }

        private float NeighbourAverage(int[] levels, int vx, int vy)
        {
            var sum = 0f;
            var count = 0;
            for (var dy = -1; dy <= 1; dy++)
            {
                for (var dx = -1; dx <= 1; dx++)
                {
                    if (dx == 0 && dy == 0) continue;
                    var nx = vx + dx;
                    var ny = vy + dy;
                    if (nx < 0 || ny < 0 || nx > CellsX || ny > CellsY) continue;
                    sum += levels[Index(nx, ny)];
                    count++;
                }
            }
            return count == 0 ? levels[Index(vx, vy)] : sum / count;
        }

        /// <summary>Smooths the whole terrain by averaging each vertex with its neighbours, rounded to whole levels.</summary>
        public bool Smooth(int iterations, float amount = 1f)
        {
            var any = false;
            for (var i = 0; i < Math.Max(1, iterations); i++)
            {
                var before = (int[]) Levels.Clone();
                for (var vy = 0; vy <= CellsY; vy++)
                {
                    for (var vx = 0; vx <= CellsX; vx++)
                    {
                        var current = before[Index(vx, vy)];
                        var avg = NeighbourAverage(before, vx, vy);
                        var next = current + (int) Math.Round((avg - current) * amount, MidpointRounding.AwayFromZero);
                        if (next != current)
                        {
                            Levels[Index(vx, vy)] = next;
                            any = true;
                        }
                    }
                }
            }
            if (any) Version++;
            return any;
        }

        #endregion

        #region Weld

        /// <summary>
        /// Makes the vertices that two terrains share (the same world x and y) the same height: this terrain's height wins.
        /// Returns how many vertices of the other terrain changed. Terrains with different cell or level sizes are left alone.
        /// </summary>
        public int WeldTo(Heightfield other)
        {
            if (other == null || other == this) return 0;
            if (Math.Abs(other.LevelHeight - LevelHeight) > 1e-4f) return 0;

            var changed = 0;
            const float tol = 0.01f;
            for (var vy = 0; vy <= CellsY; vy++)
            {
                for (var vx = 0; vx <= CellsX; vx++)
                {
                    var wx = Origin.X + vx * CellSize;
                    var wy = Origin.Y + vy * CellSize;

                    var fx = (wx - other.Origin.X) / other.CellSize;
                    var fy = (wy - other.Origin.Y) / other.CellSize;
                    var ox = (int) Math.Round(fx);
                    var oy = (int) Math.Round(fy);
                    if (Math.Abs(fx - ox) * other.CellSize > tol || Math.Abs(fy - oy) * other.CellSize > tol) continue;
                    if (ox < 0 || oy < 0 || ox > other.CellsX || oy > other.CellsY) continue;

                    var worldZ = Origin.Z + GetLevel(vx, vy) * LevelHeight;
                    var level = (int) Math.Round((worldZ - other.Origin.Z) / other.LevelHeight);
                    if (other.GetLevel(ox, oy) != level)
                    {
                        other.SetLevel(ox, oy, level);
                        changed++;
                    }
                }
            }
            return changed;
        }

        #endregion

        #region Brush generation

        /// <summary>
        /// The brushes of the terrain. With mergeCoplanar, rectangles of cells that lie in one plane become one brush
        /// (a flat area is a single block); the remaining cells are split into two triangles each.
        /// </summary>
        public List<TerrainBrush> BuildBrushes(bool mergeCoplanar = true)
        {
            var result = new List<TerrainBrush>();
            var bottom = BottomZ;
            var used = new bool[CellsX * CellsY];

            for (var cy = 0; cy < CellsY; cy++)
            {
                for (var cx = 0; cx < CellsX; cx++)
                {
                    if (used[cy * CellsX + cx]) continue;

                    if (!mergeCoplanar || !RectPlanar(cx, cy, cx, cy))
                    {
                        used[cy * CellsX + cx] = true;
                        if (RectPlanar(cx, cy, cx, cy))
                        {
                            // A planar cell is one brush even without merging
                            result.Add(Prism(new[] { Vertex(cx, cy), Vertex(cx + 1, cy), Vertex(cx + 1, cy + 1), Vertex(cx, cy + 1) }, bottom));
                        }
                        else
                        {
                            foreach (var t in CellTriangles(cx, cy))
                            {
                                result.Add(Prism(new[] { Vertex(t[0].X, t[0].Y), Vertex(t[1].X, t[1].Y), Vertex(t[2].X, t[2].Y) }, bottom));
                            }
                        }
                        continue;
                    }

                    // Grow right, then down, while the rectangle stays in one plane
                    var x1 = cx;
                    while (x1 + 1 < CellsX && !used[cy * CellsX + x1 + 1] && RectPlanar(cx, cy, x1 + 1, cy)) x1++;

                    var y1 = cy;
                    while (y1 + 1 < CellsY && RowFree(used, cx, x1, y1 + 1) && RectPlanar(cx, cy, x1, y1 + 1)) y1++;

                    for (var yy = cy; yy <= y1; yy++)
                        for (var xx = cx; xx <= x1; xx++)
                            used[yy * CellsX + xx] = true;

                    result.Add(Prism(new[] { Vertex(cx, cy), Vertex(x1 + 1, cy), Vertex(x1 + 1, y1 + 1), Vertex(cx, y1 + 1) }, bottom));
                }
            }

            return result;
        }

        private bool RowFree(bool[] used, int x0, int x1, int y)
        {
            for (var x = x0; x <= x1; x++) if (used[y * CellsX + x]) return false;
            return true;
        }

        /// <summary>True if every vertex of the cells (x0..x1, y0..y1) lies in the plane through the rectangle's corners.</summary>
        private bool RectPlanar(int x0, int y0, int x1, int y1)
        {
            var w = x1 + 1 - x0;
            var h = y1 + 1 - y0;
            var a = GetLevel(x0, y0);
            var b = GetLevel(x0 + w, y0);
            var c = GetLevel(x0, y0 + h);

            // The fourth corner decides whether the rectangle is a plane at all
            if (GetLevel(x0 + w, y0 + h) * 1L != a + (b - a) + (c - a)) return false;

            for (var y = y0; y <= y0 + h; y++)
            {
                for (var x = x0; x <= x0 + w; x++)
                {
                    // (level - a) = (b - a) * (x - x0) / w + (c - a) * (y - y0) / h, in integers
                    long lhs = (long) (GetLevel(x, y) - a) * w * h;
                    long rhs = (long) (b - a) * (x - x0) * h + (long) (c - a) * (y - y0) * w;
                    if (lhs != rhs) return false;
                }
            }
            return true;
        }

        /// <summary>
        /// A convex brush from a top polygon (3 or 4 points, in x/y order around the footprint) down to a flat bottom.
        /// </summary>
        public static TerrainBrush Prism(Vector3[] top, float bottomZ)
        {
            // Make the footprint counter-clockwise seen from above, so the faces can be wound consistently
            var area = 0f;
            for (var i = 0; i < top.Length; i++)
            {
                var p = top[i];
                var q = top[(i + 1) % top.Length];
                area += p.X * q.Y - q.X * p.Y;
            }
            var t = (Vector3[]) top.Clone();
            if (area < 0) Array.Reverse(t);

            var n = t.Length;
            var bot = t.Select(v => new Vector3(v.X, v.Y, bottomZ)).ToArray();
            var brush = new TerrainBrush();

            // Counter-clockwise from outside is (a, b, c); Sledge wants clockwise from outside, so every face is reversed below
            brush.Faces.Add(new TerrainFace { Kind = TerrainFaceKind.Top, Vertices = Clockwise(t) });
            brush.Faces.Add(new TerrainFace { Kind = TerrainFaceKind.Bottom, Vertices = Clockwise(bot.Reverse().ToArray()) });
            for (var i = 0; i < n; i++)
            {
                var j = (i + 1) % n;
                // Outward-facing side, counter-clockwise seen from outside: top i, bottom i, bottom j, top j
                brush.Faces.Add(new TerrainFace
                {
                    Kind = TerrainFaceKind.Side,
                    Vertices = Clockwise(new[] { t[i], bot[i], bot[j], t[j] })
                });
            }
            return brush;
        }

        private static Vector3[] Clockwise(Vector3[] counterClockwise)
        {
            return counterClockwise.Reverse().ToArray();
        }

        #endregion

        #region Collision

        /// <summary>
        /// Coarse collision brushes: the terrain sampled every blockSize cells (the last block is whatever is left over),
        /// built into brushes the same way. The result never has more brushes than the terrain itself.
        /// </summary>
        public List<TerrainBrush> BuildCollision(int blockSize, CollisionSampling sampling, bool mergeCoplanar = true)
        {
            blockSize = Math.Max(1, blockSize);
            var xs = Stops(CellsX, blockSize);
            var ys = Stops(CellsY, blockSize);

            // The coarse grid, as a heightfield of its own with uneven cells
            var levels = new int[xs.Count, ys.Count];
            for (var j = 0; j < ys.Count; j++)
                for (var i = 0; i < xs.Count; i++)
                    levels[i, j] = Sample(xs[i], ys[j], blockSize, sampling);

            var bottom = BottomZ;
            var result = new List<TerrainBrush>();

            for (var j = 0; j + 1 < ys.Count; j++)
            {
                for (var i = 0; i + 1 < xs.Count; i++)
                {
                    Vector3 V(int a, int b) => new Vector3(Origin.X + xs[a] * CellSize, Origin.Y + ys[b] * CellSize, Origin.Z + levels[a, b] * LevelHeight);

                    var p00 = V(i, j);
                    var p10 = V(i + 1, j);
                    var p01 = V(i, j + 1);
                    var p11 = V(i + 1, j + 1);

                    // The same rule as the terrain: planar blocks are one brush, the others two triangles
                    var planar = Math.Abs((p00.Z + p11.Z) - (p10.Z + p01.Z)) < 1e-3f
                                 && PlanarQuad(p00, p10, p11, p01);
                    if (mergeCoplanar && planar)
                    {
                        result.Add(Prism(new[] { p00, p10, p11, p01 }, bottom));
                    }
                    else
                    {
                        var a = Math.Abs(p00.Z - p11.Z);
                        var b = Math.Abs(p10.Z - p01.Z);
                        var diagonalA = a != b ? a < b : (i + j) % 2 == 0;
                        if (diagonalA)
                        {
                            result.Add(Prism(new[] { p00, p10, p11 }, bottom));
                            result.Add(Prism(new[] { p00, p11, p01 }, bottom));
                        }
                        else
                        {
                            result.Add(Prism(new[] { p00, p10, p01 }, bottom));
                            result.Add(Prism(new[] { p10, p11, p01 }, bottom));
                        }
                    }
                }
            }

            return result;
        }

        private static bool PlanarQuad(Vector3 p00, Vector3 p10, Vector3 p11, Vector3 p01)
        {
            // Horizontal positions are a rectangle, so the corners are in one plane when z00 + z11 == z10 + z01
            return Math.Abs((p00.Z + p11.Z) - (p10.Z + p01.Z)) < 1e-3f;
        }

        private static List<int> Stops(int cells, int block)
        {
            var list = new List<int>();
            for (var i = 0; i < cells; i += block) list.Add(i);
            list.Add(cells);
            return list;
        }

        private int Sample(int vx, int vy, int block, CollisionSampling sampling)
        {
            if (sampling == CollisionSampling.Corner) return GetLevel(vx, vy);

            var lo = int.MaxValue;
            var hi = int.MinValue;
            var r = Math.Max(0, block / 2);
            for (var dy = -r; dy <= r; dy++)
            {
                for (var dx = -r; dx <= r; dx++)
                {
                    var x = vx + dx;
                    var y = vy + dy;
                    if (x < 0 || y < 0 || x > CellsX || y > CellsY) continue;
                    var l = GetLevel(x, y);
                    lo = Math.Min(lo, l);
                    hi = Math.Max(hi, l);
                }
            }
            return sampling == CollisionSampling.Lowest ? lo : hi;
        }

        #endregion
    }
}
