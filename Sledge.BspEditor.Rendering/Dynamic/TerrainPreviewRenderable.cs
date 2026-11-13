using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Linq;
using System.Numerics;
using Sledge.BspEditor.Documents;
using Sledge.BspEditor.Rendering.Resources;
using Sledge.BspEditor.Terrain;
using Sledge.Rendering.Cameras;
using Sledge.Rendering.Pipelines;
using Sledge.Rendering.Primitives;
using Sledge.Rendering.Resources;

namespace Sledge.BspEditor.Rendering.Dynamic
{
    /// <summary>
    /// Draws every terrain that is still a height grid (it has no brushes yet): a height-coloured translucent surface
    /// in the 3D view and the grid as lines in all views. It is rebuilt from the terrain's data every frame, so sculpting
    /// shows at once and doesn't rebuild the scene. The mesh is cached until the terrain changes.
    /// </summary>
    [Export(typeof(IMapObjectDynamicRenderable))]
    public class TerrainPreviewRenderable : IMapObjectDynamicRenderable
    {
        private const float SurfaceAlpha = 0.55f;

        private sealed class Cache
        {
            public Heightfield Field;
            public int Version;
            public VertexStandard[] Vertices;
            public uint[] Indices;
            public uint SurfaceIndexCount;
            public uint LineIndexCount;
            public Vector3 Centre;
        }

        private readonly Dictionary<long, Cache> _cache = new Dictionary<long, Cache>();

        public void Render(MapDocument document, BufferBuilder builder, ResourceCollector resourceCollector)
        {
            var terrains = document.Map.Data.Get<TerrainData>().Where(x => x.State == TerrainState.Heightfield).ToList();
            if (terrains.Count == 0)
            {
                if (_cache.Count > 0) _cache.Clear();
                return;
            }

            // Forget terrains that are gone
            foreach (var id in _cache.Keys.Where(k => terrains.All(t => t.ID != k)).ToList()) _cache.Remove(id);

            foreach (var terrain in terrains)
            {
                var field = terrain.Field;
                if (field == null || field.CellsX < 1 || field.CellsY < 1 || field.Levels.Length != field.VertsX * field.VertsY) continue;

                if (!_cache.TryGetValue(terrain.ID, out var cache) || cache.Field != field || cache.Version != field.Version)
                {
                    cache = Build(field);
                    _cache[terrain.ID] = cache;
                }

                var groups = new List<BufferGroup>();
                if (cache.SurfaceIndexCount > 0)
                {
                    groups.Add(new BufferGroup(PipelineType.TexturedAlpha, CameraType.Perspective, cache.Centre, 0, cache.SurfaceIndexCount));
                }
                if (cache.LineIndexCount > 0)
                {
                    groups.Add(new BufferGroup(PipelineType.Wireframe, CameraType.Both, cache.SurfaceIndexCount, cache.LineIndexCount));
                }

                builder.Append(cache.Vertices, cache.Indices, groups);
            }
        }

        private static Cache Build(Heightfield field)
        {
            var vx = field.VertsX;
            var vy = field.VertsY;
            var count = vx * vy;

            var min = field.MinLevel;
            var max = field.MaxLevel;
            var span = System.Math.Max(1, max - min);

            // Vertices [0, count): the surface. [count, 2 * count): the lines.
            var vertices = new VertexStandard[count * 2];
            for (var y = 0; y < vy; y++)
            {
                for (var x = 0; x < vx; x++)
                {
                    var i = field.Index(x, y);
                    var p = field.Vertex(x, y);
                    var colour = Ramp((field.GetLevel(x, y) - min) / (float) span);
                    var normal = Normal(field, x, y);

                    vertices[i] = new VertexStandard
                    {
                        Position = p,
                        Colour = Vector4.One,
                        Normal = normal,
                        Texture = Vector2.Zero,
                        Tint = new Vector4(colour.X, colour.Y, colour.Z, SurfaceAlpha),
                        Flags = VertexFlags.FlatColour
                    };
                    vertices[count + i] = new VertexStandard
                    {
                        Position = p,
                        Colour = new Vector4(colour.X, colour.Y, colour.Z, 1),
                        Normal = normal,
                        Texture = Vector2.Zero,
                        Tint = Vector4.One
                    };
                }
            }

            var indices = new List<uint>();

            // Surface triangles, the same split as the brushes
            for (var cy = 0; cy < field.CellsY; cy++)
            {
                for (var cx = 0; cx < field.CellsX; cx++)
                {
                    foreach (var t in field.CellTriangles(cx, cy))
                    {
                        indices.Add((uint) field.Index(t[0].X, t[0].Y));
                        indices.Add((uint) field.Index(t[1].X, t[1].Y));
                        indices.Add((uint) field.Index(t[2].X, t[2].Y));
                    }
                }
            }
            var surfaceCount = (uint) indices.Count;

            // Lines: the cell edges and the diagonals actually used
            uint L(int x, int y) => (uint) (count + field.Index(x, y));
            for (var y = 0; y < vy; y++)
            {
                for (var x = 0; x < vx; x++)
                {
                    if (x + 1 < vx) { indices.Add(L(x, y)); indices.Add(L(x + 1, y)); }
                    if (y + 1 < vy) { indices.Add(L(x, y)); indices.Add(L(x, y + 1)); }
                    if (x + 1 < vx && y + 1 < vy)
                    {
                        if (field.DiagonalA(x, y)) { indices.Add(L(x, y)); indices.Add(L(x + 1, y + 1)); }
                        else { indices.Add(L(x + 1, y)); indices.Add(L(x, y + 1)); }
                    }
                }
            }

            var (bmin, bmax) = field.Bounds();
            return new Cache
            {
                Field = field,
                Version = field.Version,
                Vertices = vertices,
                Indices = indices.ToArray(),
                SurfaceIndexCount = surfaceCount,
                LineIndexCount = (uint) indices.Count - surfaceCount,
                Centre = (bmin + bmax) / 2
            };
        }

        /// <summary>Smooth surface normal from the neighbouring heights (central differences).</summary>
        private static Vector3 Normal(Heightfield f, int x, int y)
        {
            var x0 = System.Math.Max(0, x - 1);
            var x1 = System.Math.Min(f.CellsX, x + 1);
            var y0 = System.Math.Max(0, y - 1);
            var y1 = System.Math.Min(f.CellsY, y + 1);

            var dzdx = (f.GetLevel(x1, y) - f.GetLevel(x0, y)) * f.LevelHeight / ((x1 - x0) * f.CellSize);
            var dzdy = (f.GetLevel(x, y1) - f.GetLevel(x, y0)) * f.LevelHeight / ((y1 - y0) * f.CellSize);
            return Vector3.Normalize(new Vector3(-dzdx, -dzdy, 1));
        }

        /// <summary>Blue (low) through green and yellow to red (high).</summary>
        private static Vector3 Ramp(float t)
        {
            var stops = new[]
            {
                new Vector3(0.25f, 0.45f, 0.95f),
                new Vector3(0.30f, 0.80f, 0.35f),
                new Vector3(0.95f, 0.85f, 0.25f),
                new Vector3(0.90f, 0.30f, 0.25f)
            };
            t = System.Math.Max(0, System.Math.Min(1, t)) * (stops.Length - 1);
            var i = System.Math.Min(stops.Length - 2, (int) t);
            return Vector3.Lerp(stops[i], stops[i + 1], t - i);
        }
    }
}
