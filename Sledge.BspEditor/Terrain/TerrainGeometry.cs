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

    /// <summary>A convex brush: a prism with a (sloped) top, a flat bottom and sides that run along the surface normal.</summary>
    public sealed class TerrainBrush
    {
        public List<TerrainFace> Faces = new List<TerrainFace>();

        /// <summary>The cells (inclusive) the top face covers; -1 when the brush isn't made from whole cells.</summary>
        public int CellX0 = -1, CellY0 = -1, CellX1 = -1, CellY1 = -1;

        public TerrainBrush WithCells(int x0, int y0, int x1, int y1)
        {
            CellX0 = x0;
            CellY0 = y0;
            CellX1 = x1;
            CellY1 = y1;
            return this;
        }
    }

    /// <summary>
    /// A displacement surface, like a Source displacement. Four corner points (on a face of a brush, or on a rectangle) are
    /// divided into cells, and every grid vertex can be moved along a direction (the face normal) in whole levels:
    /// vertex = bilinear point + direction * level * LevelHeight. A flat floor is the same thing with the normal pointing up.
    /// The brushes are generated from it: each cell (or merged group of cells) is extruded back along the normal to a flat bottom.
    /// This file has no editor dependencies, so it can be tested on its own.
    /// </summary>
    public sealed class Heightfield
    {
        /// <summary>The corners at level 0: (0, 0), (CellsX, 0), (0, CellsY) and (CellsX, CellsY) of the grid.</summary>
        public Vector3 P00 { get; set; }
        public Vector3 P10 { get; set; }
        public Vector3 P01 { get; set; }
        public Vector3 P11 { get; set; }

        /// <summary>The direction the levels move the surface in (a unit vector; the normal of the face the surface came from).</summary>
        public Vector3 Normal { get; set; } = Vector3.UnitZ;

        /// <summary>
        /// The direction each vertex moves in, or null when they all use <see cref="Normal"/>. Neighbouring surfaces that
        /// meet at an angle share an averaged direction along their joint, so the same level means the same point (no crack).
        /// </summary>
        public Vector3[] Dirs { get; set; }

        public int CellsX { get; set; }
        public int CellsY { get; set; }

        /// <summary>The cell size asked for, in world units (the real cells follow the corners and may differ a little).</summary>
        public float CellSize { get; set; } = 16f;

        /// <summary>World units per height level.</summary>
        public float LevelHeight { get; set; } = 8f;

        /// <summary>How many levels deep the brushes go below the lowest point of the surface.</summary>
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

        /// <summary>A flat rectangle facing up: the corner, the number of cells and their size.</summary>
        public Heightfield(int cellsX, int cellsY, float cellSize, float levelHeight, Vector3 origin, int level = 0)
        {
            CellsX = cellsX;
            CellsY = cellsY;
            CellSize = cellSize;
            LevelHeight = levelHeight;
            P00 = origin;
            P10 = origin + new Vector3(cellsX * cellSize, 0, 0);
            P01 = origin + new Vector3(0, cellsY * cellSize, 0);
            P11 = origin + new Vector3(cellsX * cellSize, cellsY * cellSize, 0);
            Normal = Vector3.UnitZ;
            Levels = new int[(cellsX + 1) * (cellsY + 1)];
            if (level != 0) for (var i = 0; i < Levels.Length; i++) Levels[i] = level;
        }

        /// <summary>
        /// A surface over a four sided face (corners in order around the face). The grid runs along the longer edges first.
        /// Depth is how far back the brushes go (the thickness of the brush the face came from).
        /// </summary>
        public static Heightfield FromQuad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 normal, float cellSize, float levelHeight, float depth, int maxCells = 256)
        {
            cellSize = Math.Max(1f, cellSize);
            levelHeight = Math.Max(1f, levelHeight);

            // Start at the corner that makes the first edge the longer one
            if ((b - a).Length() + (c - d).Length() < (d - a).Length() + (c - b).Length())
            {
                var t = a;
                a = b;
                b = c;
                c = d;
                d = t;
            }

            var lengthX = ((b - a).Length() + (c - d).Length()) / 2;
            var lengthY = ((d - a).Length() + (c - b).Length()) / 2;
            var cellsX = Math.Max(1, Math.Min(maxCells, (int) Math.Round(lengthX / cellSize)));
            var cellsY = Math.Max(1, Math.Min(maxCells, (int) Math.Round(lengthY / cellSize)));

            return new Heightfield
            {
                P00 = a,
                P10 = b,
                P11 = c,
                P01 = d,
                Normal = Vector3.Normalize(normal),
                CellsX = cellsX,
                CellsY = cellsY,
                CellSize = cellSize,
                LevelHeight = levelHeight,
                ThicknessLevels = Math.Max(1, (int) Math.Ceiling(depth / levelHeight - 1e-4f)),
                Levels = new int[(cellsX + 1) * (cellsY + 1)]
            };
        }

        /// <summary>The corner of the grid. Setting it moves the whole surface.</summary>
        public Vector3 Origin
        {
            get => P00;
            set => Translate(value - P00);
        }

        public void Translate(Vector3 delta)
        {
            if (delta == Vector3.Zero) return;
            P00 += delta;
            P10 += delta;
            P01 += delta;
            P11 += delta;
            Version++;
        }

        public int VertsX => CellsX + 1;
        public int VertsY => CellsY + 1;
        public int CellCount => CellsX * CellsY;

        public int Index(int x, int y) => y * VertsX + x;
        public int GetLevel(int x, int y) => Levels[Index(x, y)];
        public void SetLevel(int x, int y, int level) { Levels[Index(x, y)] = level; Version++; }

        /// <summary>The point at a (fractional) grid position at level 0.</summary>
        public Vector3 Base(float gx, float gy)
        {
            var u = gx / CellsX;
            var v = gy / CellsY;
            return P00 * ((1 - u) * (1 - v)) + P10 * (u * (1 - v)) + P01 * ((1 - u) * v) + P11 * (u * v);
        }

        public Vector3 DirAt(int x, int y) => Dirs != null ? Dirs[Index(x, y)] : Normal;

        public Vector3 Vertex(int x, int y)
        {
            return Base(x, y) + DirAt(x, y) * (GetLevel(x, y) * LevelHeight);
        }

        public Vector3 VertexAtLevel(int x, int y, int level)
        {
            return Base(x, y) + DirAt(x, y) * (level * LevelHeight);
        }

        public Heightfield Clone()
        {
            return new Heightfield
            {
                P00 = P00,
                P10 = P10,
                P01 = P01,
                P11 = P11,
                Normal = Normal,
                Dirs = Dirs == null ? null : (Vector3[]) Dirs.Clone(),
                CellsX = CellsX,
                CellsY = CellsY,
                CellSize = CellSize,
                LevelHeight = LevelHeight,
                ThicknessLevels = ThicknessLevels,
                Levels = (int[]) Levels.Clone()
            };
        }

        public int MinLevel => Levels.Length == 0 ? 0 : Levels.Min();
        public int MaxLevel => Levels.Length == 0 ? 0 : Levels.Max();

        /// <summary>Height of a point above the base surface's plane, along <see cref="Normal"/>.</summary>
        public float HeightOf(Vector3 p) => Vector3.Dot(p - P00, Normal);

        /// <summary>The height (along the normal) of the flat bottom shared by every brush.</summary>
        public float BottomHeight
        {
            get
            {
                var min = float.MaxValue;
                for (var y = 0; y < VertsY; y++)
                    for (var x = 0; x < VertsX; x++)
                        min = Math.Min(min, HeightOf(Vertex(x, y)));
                return min - Math.Max(1, ThicknessLevels) * LevelHeight;
            }
        }

        /// <summary>A surface point straight back (against the normal) to the bottom.</summary>
        public Vector3 BottomPoint(Vector3 p, float bottomHeight)
        {
            return p - Normal * (HeightOf(p) - bottomHeight);
        }

        public (Vector3 Min, Vector3 Max) Bounds()
        {
            var bottom = BottomHeight;
            var min = new Vector3(float.MaxValue);
            var max = new Vector3(float.MinValue);
            for (var y = 0; y < VertsY; y++)
            {
                for (var x = 0; x < VertsX; x++)
                {
                    var v = Vertex(x, y);
                    var b = BottomPoint(v, bottom);
                    min = Vector3.Min(min, Vector3.Min(v, b));
                    max = Vector3.Max(max, Vector3.Max(v, b));
                }
            }
            return (min, max);
        }

        public bool SameShapeAs(Heightfield other)
        {
            return other != null && other.CellsX == CellsX && other.CellsY == CellsY && other.Levels.SequenceEqual(Levels)
                   && other.P00 == P00 && other.P10 == P10 && other.P01 == P01 && other.P11 == P11
                   && (Dirs == null) == (other.Dirs == null) && (Dirs == null || other.Dirs.SequenceEqual(Dirs));
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

        /// <summary>All surface triangles in world space, for ray casts.</summary>
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

        /// <summary>The level at a (fractional) grid position, following the same triangles as the surface.</summary>
        public float LevelAt(float gx, float gy)
        {
            gx = Math.Max(0, Math.Min(CellsX, gx));
            gy = Math.Max(0, Math.Min(CellsY, gy));
            var cx = Math.Min((int) Math.Floor(gx), CellsX - 1);
            var cy = Math.Min((int) Math.Floor(gy), CellsY - 1);
            var fx = gx - cx;
            var fy = gy - cy;
            float h00 = GetLevel(cx, cy), h10 = GetLevel(cx + 1, cy), h01 = GetLevel(cx, cy + 1), h11 = GetLevel(cx + 1, cy + 1);

            if (DiagonalA(cx, cy))
            {
                return fx >= fy
                    ? h00 + (h10 - h00) * (fx - fy) + (h11 - h00) * fy
                    : h00 + (h11 - h00) * fx + (h01 - h00) * (fy - fx);
            }

            return fx + fy <= 1
                ? h00 + (h10 - h00) * fx + (h01 - h00) * fy
                : h11 + (h01 - h11) * (1 - fx) + (h10 - h11) * (1 - fy);
        }

        #endregion

        #region Ray casting

        /// <summary>The nearest point where the ray crosses the surface, and where on the grid that is.</summary>
        public bool RayCast(Vector3 start, Vector3 end, out Vector3 hit, out float gx, out float gy)
        {
            hit = default;
            gx = gy = 0;
            var dir = end - start;
            if (dir.LengthSquared() < 1e-12f) return false;
            var length = dir.Length();
            dir /= length;

            // Positions once, then every triangle
            var verts = new Vector3[VertsX * VertsY];
            for (var y = 0; y < VertsY; y++)
                for (var x = 0; x < VertsX; x++)
                    verts[Index(x, y)] = Vertex(x, y);

            var best = float.MaxValue;
            for (var cy = 0; cy < CellsY; cy++)
            {
                for (var cx = 0; cx < CellsX; cx++)
                {
                    foreach (var t in CellTriangles(cx, cy))
                    {
                        var a = verts[Index(t[0].X, t[0].Y)];
                        var b = verts[Index(t[1].X, t[1].Y)];
                        var c = verts[Index(t[2].X, t[2].Y)];
                        if (!RayTriangle(start, dir, a, b, c, out var d, out var u, out var v)) continue;
                        if (d < 0 || d > length || d >= best) continue;
                        best = d;
                        var w = 1 - u - v;
                        gx = w * t[0].X + u * t[1].X + v * t[2].X;
                        gy = w * t[0].Y + u * t[1].Y + v * t[2].Y;
                    }
                }
            }

            if (best == float.MaxValue) return false;
            hit = start + dir * best;
            return true;
        }

        public bool RayCast(Vector3 start, Vector3 end, out Vector3 hit)
        {
            return RayCast(start, end, out hit, out _, out _);
        }

        private static bool RayTriangle(Vector3 o, Vector3 d, Vector3 a, Vector3 b, Vector3 c, out float distance, out float u, out float v)
        {
            const float eps = 1e-6f;
            distance = u = v = 0;
            var e1 = b - a;
            var e2 = c - a;
            var p = Vector3.Cross(d, e2);
            var det = Vector3.Dot(e1, p);
            if (Math.Abs(det) < eps) return false;
            var inv = 1f / det;
            var tv = o - a;
            u = Vector3.Dot(tv, p) * inv;
            if (u < 0 || u > 1) return false;
            var q = Vector3.Cross(tv, e1);
            v = Vector3.Dot(d, q) * inv;
            if (v < 0 || u + v > 1) return false;
            distance = Vector3.Dot(e2, q) * inv;
            return true;
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

        /// <summary>
        /// One brush stamp: a sphere of the given radius at a world point (on the surface). Strength is in world units per stamp
        /// (at the full weight of the brush). The levels are whole numbers, so what is too small to move a level is kept in
        /// <paramref name="residual"/> (one value per vertex, kept for the whole stroke) and adds up over the next stamps:
        /// a small strength is slower, not nothing. Raise and Lower move along each vertex's direction (the face normal);
        /// Flatten and Smooth move towards their target by at most the amount; Set jumps to the level.
        /// </summary>
        public bool ApplyBrushUnits(Vector3 hit, float radius, SculptMode mode, float units, BrushFalloff falloff, int targetLevel, float[] residual)
        {
            var perStamp = Math.Max(0f, units) / Math.Max(0.0001f, LevelHeight);
            var changed = false;

            // The neighbours of a vertex are read from the heights before this stamp, so the result doesn't depend on the order
            var before = mode == SculptMode.Smooth ? (int[]) Levels.Clone() : Levels;

            for (var vy = 0; vy <= CellsY; vy++)
            {
                for (var vx = 0; vx <= CellsX; vx++)
                {
                    var idx = Index(vx, vy);
                    var w = Weight((Vertex(vx, vy) - hit).Length(), radius, falloff);
                    if (w <= 0) continue;

                    var current = Levels[idx];
                    var cap = perStamp * w;
                    float move;

                    switch (mode)
                    {
                        case SculptMode.Raise:
                            move = cap;
                            break;
                        case SculptMode.Lower:
                            move = -cap;
                            break;
                        case SculptMode.Flatten:
                            move = Math.Max(-cap, Math.Min(cap, targetLevel - current));
                            break;
                        case SculptMode.Smooth:
                            move = Math.Max(-cap, Math.Min(cap, NeighbourAverage(before, vx, vy) - current));
                            break;
                        default: // Set
                            if (current != targetLevel)
                            {
                                Levels[idx] = targetLevel;
                                changed = true;
                            }
                            if (residual != null) residual[idx] = 0;
                            continue;
                    }

                    var r = (residual != null ? residual[idx] : 0f) + move;
                    var whole = (int) Math.Truncate(r);
                    if (residual != null) residual[idx] = r - whole;
                    if (whole != 0)
                    {
                        Levels[idx] = current + whole;
                        changed = true;
                    }
                }
            }

            if (changed) Version++;
            return changed;
        }

        /// <summary>Smooths the whole surface by averaging each vertex with its neighbours, rounded to whole levels.</summary>
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

        #region Resizing

        private float AverageLengthX() => ((P10 - P00).Length() + (P11 - P01).Length()) / 2;
        private float AverageLengthY() => ((P01 - P00).Length() + (P11 - P10).Length()) / 2;

        private float MinHeight()
        {
            var min = float.MaxValue;
            for (var y = 0; y < VertsY; y++)
                for (var x = 0; x < VertsX; x++)
                    min = Math.Min(min, HeightOf(Vertex(x, y)));
            return min;
        }

        private Vector3 DirAtGrid(float gx, float gy)
        {
            if (Dirs == null) return Normal;
            gx = Math.Max(0, Math.Min(CellsX, gx));
            gy = Math.Max(0, Math.Min(CellsY, gy));
            var x0 = Math.Min((int) Math.Floor(gx), CellsX - 1);
            var y0 = Math.Min((int) Math.Floor(gy), CellsY - 1);
            var fx = gx - x0;
            var fy = gy - y0;
            var d = Dirs[Index(x0, y0)] * ((1 - fx) * (1 - fy)) + Dirs[Index(x0 + 1, y0)] * (fx * (1 - fy))
                    + Dirs[Index(x0, y0 + 1)] * ((1 - fx) * fy) + Dirs[Index(x0 + 1, y0 + 1)] * (fx * fy);
            return d.LengthSquared() < 1e-8f ? Normal : Vector3.Normalize(d);
        }

        /// <summary>The same surface with another cell size: the corners stay, heights are taken from the old surface.</summary>
        public Heightfield Resample(float newCellSize, int maxCells = 256)
        {
            newCellSize = Math.Max(1f, newCellSize);
            var cx = Math.Max(1, Math.Min(maxCells, (int) Math.Round(AverageLengthX() / newCellSize)));
            var cy = Math.Max(1, Math.Min(maxCells, (int) Math.Round(AverageLengthY() / newCellSize)));

            var result = new Heightfield
            {
                P00 = P00,
                P10 = P10,
                P01 = P01,
                P11 = P11,
                Normal = Normal,
                CellsX = cx,
                CellsY = cy,
                CellSize = newCellSize,
                LevelHeight = LevelHeight,
                Levels = new int[(cx + 1) * (cy + 1)]
            };

            if (Dirs != null) result.Dirs = new Vector3[(cx + 1) * (cy + 1)];
            for (var y = 0; y <= cy; y++)
            {
                for (var x = 0; x <= cx; x++)
                {
                    var gx = x * (float) CellsX / cx;
                    var gy = y * (float) CellsY / cy;
                    result.Levels[result.Index(x, y)] = (int) Math.Round(LevelAt(gx, gy));
                    if (Dirs != null) result.Dirs[result.Index(x, y)] = DirAtGrid(gx, gy);
                }
            }

            // Keep the bottom where it was
            result.ThicknessLevels = Math.Max(1, (int) Math.Round((result.MinHeight() - BottomHeight) / LevelHeight));
            result.Touch();
            return result;
        }

        /// <summary>The same surface with another level height: the shape is kept, rounded to the new levels.</summary>
        public Heightfield WithLevelHeight(float newLevelHeight)
        {
            newLevelHeight = Math.Max(1f, newLevelHeight);
            var result = Clone();
            result.LevelHeight = newLevelHeight;
            for (var i = 0; i < Levels.Length; i++)
            {
                result.Levels[i] = (int) Math.Round(Levels[i] * LevelHeight / newLevelHeight);
            }

            result.ThicknessLevels = Math.Max(1, (int) Math.Round((result.MinHeight() - BottomHeight) / newLevelHeight));
            result.Touch();
            return result;
        }

        #endregion

        #region Weld

        private const float WeldTolerance = 0.05f;

        private static (long, long, long) Key(Vector3 p)
        {
            return ((long) Math.Round(p.X * 8), (long) Math.Round(p.Y * 8), (long) Math.Round(p.Z * 8));
        }

        /// <summary>
        /// Stitches surfaces that meet along an edge: where their corner and edge vertices are in the same place they get the
        /// same height as the first one and the same (averaged) direction, so the surfaces meet with no crack however they are sculpted.
        /// Returns how many vertices were changed.
        /// </summary>
        public static int Weld(IList<Heightfield> fields)
        {
            var groups = new Dictionary<(long, long, long), List<(Heightfield F, int X, int Y)>>();
            foreach (var f in fields)
            {
                for (var y = 0; y <= f.CellsY; y++)
                {
                    for (var x = 0; x <= f.CellsX; x++)
                    {
                        if (x != 0 && y != 0 && x != f.CellsX && y != f.CellsY) continue; // only the border can touch another surface
                        var key = Key(f.Base(x, y));
                        if (!groups.TryGetValue(key, out var list)) groups[key] = list = new List<(Heightfield, int, int)>();
                        list.Add((f, x, y));
                    }
                }
            }

            var changed = 0;
            foreach (var list in groups.Values)
            {
                // The same surface can only be in a group once; two vertices of one surface are not a seam
                if (list.Count < 2 || list.Select(x => x.F).Distinct().Count() < 2) continue;

                var sum = Vector3.Zero;
                foreach (var e in list) sum += e.F.DirAt(e.X, e.Y);
                var dir = sum.LengthSquared() < 1e-8f ? list[0].F.Normal : Vector3.Normalize(sum);

                var first = list[0];
                var firstHeight = first.F.GetLevel(first.X, first.Y) * first.F.LevelHeight;

                foreach (var e in list)
                {
                    var f = e.F;
                    var idx = f.Index(e.X, e.Y);
                    var touched = false;

                    if (f.DirAt(e.X, e.Y) != dir)
                    {
                        if (f.Dirs == null)
                        {
                            f.Dirs = new Vector3[f.Levels.Length];
                            for (var i = 0; i < f.Dirs.Length; i++) f.Dirs[i] = f.Normal;
                        }
                        f.Dirs[idx] = dir;
                        touched = true;
                    }

                    var level = (int) Math.Round(firstHeight / f.LevelHeight);
                    if (f.Levels[idx] != level)
                    {
                        f.Levels[idx] = level;
                        touched = true;
                    }

                    if (touched)
                    {
                        changed++;
                        f.Version++;
                    }
                }
            }

            return changed;
        }

        /// <summary>Welds this surface to another one (this surface's heights win). Returns how many vertices changed.</summary>
        public int WeldTo(Heightfield other)
        {
            if (other == null || other == this) return 0;
            return Weld(new[] { this, other });
        }

        #endregion

        #region Brush generation

        private static bool Coplanar(Vector3 a, Vector3 b, Vector3 c, Vector3 d)
        {
            var n = Vector3.Cross(b - a, c - a);
            var len = n.Length();
            if (len < 1e-6f) n = Vector3.Cross(b - a, d - a);
            len = n.Length();
            if (len < 1e-6f) return true;
            return Math.Abs(Vector3.Dot(n / len, d - a)) < 0.01f;
        }

        /// <summary>A plane through the first three corners of the rectangle of cells, as (normal, distance).</summary>
        private (Vector3 N, float D) CellPlane(int cx, int cy)
        {
            var a = Vertex(cx, cy);
            var n = Vector3.Cross(Vertex(cx + 1, cy) - a, Vertex(cx, cy + 1) - a);
            if (n.LengthSquared() < 1e-12f) n = Normal;
            n = Vector3.Normalize(n);
            return (n, Vector3.Dot(n, a));
        }

        private bool CellIsPlanar(int cx, int cy)
        {
            return Coplanar(Vertex(cx, cy), Vertex(cx + 1, cy), Vertex(cx, cy + 1), Vertex(cx + 1, cy + 1));
        }

        private bool OnPlane((Vector3 N, float D) plane, int x, int y)
        {
            return Math.Abs(Vector3.Dot(plane.N, Vertex(x, y)) - plane.D) < 0.01f;
        }

        private bool ColumnOnPlane((Vector3 N, float D) plane, int x, int y0, int y1)
        {
            for (var y = y0; y <= y1; y++) if (!OnPlane(plane, x, y)) return false;
            return true;
        }

        private bool RowOnPlane((Vector3 N, float D) plane, int y, int x0, int x1)
        {
            for (var x = x0; x <= x1; x++) if (!OnPlane(plane, x, y)) return false;
            return true;
        }

        /// <summary>
        /// The brushes of the surface. With mergeCoplanar, rectangles of cells that lie in one plane become one brush
        /// (a flat area is a single block); the remaining cells are split into two triangles each.
        /// Cells with different keys (textures) are never merged.
        /// </summary>
        public List<TerrainBrush> BuildBrushes(bool mergeCoplanar = true, int[] cellKeys = null)
        {
            var result = new List<TerrainBrush>();
            var bottom = BottomHeight;
            var used = new bool[CellsX * CellsY];

            int Key(int x, int y) => cellKeys == null || cellKeys.Length != used.Length ? 0 : cellKeys[y * CellsX + x];
            TerrainBrush Make(Vector3[] top) => Prism(top, Normal, p => BottomPoint(p, bottom));

            for (var cy = 0; cy < CellsY; cy++)
            {
                for (var cx = 0; cx < CellsX; cx++)
                {
                    if (used[cy * CellsX + cx]) continue;

                    var planar = CellIsPlanar(cx, cy);
                    if (!mergeCoplanar || !planar)
                    {
                        used[cy * CellsX + cx] = true;
                        if (planar)
                        {
                            // A planar cell is one brush even without merging
                            result.Add(Make(new[] { Vertex(cx, cy), Vertex(cx + 1, cy), Vertex(cx + 1, cy + 1), Vertex(cx, cy + 1) }).WithCells(cx, cy, cx, cy));
                        }
                        else
                        {
                            foreach (var t in CellTriangles(cx, cy))
                            {
                                result.Add(Make(new[] { Vertex(t[0].X, t[0].Y), Vertex(t[1].X, t[1].Y), Vertex(t[2].X, t[2].Y) }).WithCells(cx, cy, cx, cy));
                            }
                        }
                        continue;
                    }

                    var key = Key(cx, cy);
                    var plane = CellPlane(cx, cy);

                    // Grow right, then down, while the rectangle stays in one plane and one texture
                    var x1 = cx;
                    while (x1 + 1 < CellsX && !used[cy * CellsX + x1 + 1] && Key(x1 + 1, cy) == key
                           && ColumnOnPlane(plane, x1 + 2, cy, cy + 1)) x1++;

                    var y1 = cy;
                    while (y1 + 1 < CellsY && RowFree(used, cx, x1, y1 + 1) && RowKey(Key, cx, x1, y1 + 1, key)
                           && RowOnPlane(plane, y1 + 2, cx, x1 + 1)) y1++;

                    for (var yy = cy; yy <= y1; yy++)
                        for (var xx = cx; xx <= x1; xx++)
                            used[yy * CellsX + xx] = true;

                    result.Add(Make(new[] { Vertex(cx, cy), Vertex(x1 + 1, cy), Vertex(x1 + 1, y1 + 1), Vertex(cx, y1 + 1) }).WithCells(cx, cy, x1, y1));
                }
            }

            return result;
        }

        private static bool RowKey(Func<int, int, int> key, int x0, int x1, int y, int expected)
        {
            for (var x = x0; x <= x1; x++) if (key(x, y) != expected) return false;
            return true;
        }

        private bool RowFree(bool[] used, int x0, int x1, int y)
        {
            for (var x = x0; x <= x1; x++) if (used[y * CellsX + x]) return false;
            return true;
        }

        /// <summary>
        /// A convex brush from a top polygon (3 or 4 points around the footprint), extruded back against the normal to a flat bottom.
        /// The sides all run along the normal, so they are flat, and the bottom is flat because every bottom point is on one plane.
        /// </summary>
        public static TerrainBrush Prism(Vector3[] top, Vector3 normal, Func<Vector3, Vector3> bottomOf)
        {
            // Make the footprint counter-clockwise seen from the front, so the faces can be wound consistently
            var area = 0f;
            for (var i = 0; i < top.Length; i++)
            {
                var p = top[i];
                var q = top[(i + 1) % top.Length];
                area += Vector3.Dot(Vector3.Cross(p, q), normal);
            }
            var t = (Vector3[]) top.Clone();
            if (area < 0) Array.Reverse(t);

            var n = t.Length;
            var bot = t.Select(bottomOf).ToArray();
            var brush = new TerrainBrush();

            // Counter-clockwise from outside is (a, b, c); Sledge wants clockwise from outside, so every face is reversed
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
        /// Coarse collision brushes: the surface sampled every blockSize cells (the last block is whatever is left over),
        /// built into brushes the same way. The result never has more brushes than the surface itself.
        /// </summary>
        public List<TerrainBrush> BuildCollision(int blockSize, CollisionSampling sampling, bool mergeCoplanar = true)
        {
            blockSize = Math.Max(1, blockSize);
            var xs = Stops(CellsX, blockSize);
            var ys = Stops(CellsY, blockSize);

            var levels = new int[xs.Count, ys.Count];
            for (var j = 0; j < ys.Count; j++)
                for (var i = 0; i < xs.Count; i++)
                    levels[i, j] = Sample(xs[i], ys[j], blockSize, sampling);

            var bottom = BottomHeight;
            var result = new List<TerrainBrush>();
            TerrainBrush Make(Vector3[] top) => Prism(top, Normal, p => BottomPoint(p, bottom));

            for (var j = 0; j + 1 < ys.Count; j++)
            {
                for (var i = 0; i + 1 < xs.Count; i++)
                {
                    Vector3 V(int a, int b) => VertexAtLevel(xs[a], ys[b], levels[a, b]);

                    var p00 = V(i, j);
                    var p10 = V(i + 1, j);
                    var p01 = V(i, j + 1);
                    var p11 = V(i + 1, j + 1);

                    if (mergeCoplanar && Coplanar(p00, p10, p01, p11))
                    {
                        result.Add(Make(new[] { p00, p10, p11, p01 }));
                    }
                    else
                    {
                        var a = Math.Abs(levels[i, j] - levels[i + 1, j + 1]);
                        var b = Math.Abs(levels[i + 1, j] - levels[i, j + 1]);
                        var diagonalA = a != b ? a < b : (i + j) % 2 == 0;
                        if (diagonalA)
                        {
                            result.Add(Make(new[] { p00, p10, p11 }));
                            result.Add(Make(new[] { p00, p11, p01 }));
                        }
                        else
                        {
                            result.Add(Make(new[] { p00, p10, p01 }));
                            result.Add(Make(new[] { p10, p11, p01 }));
                        }
                    }
                }
            }

            return result;
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
