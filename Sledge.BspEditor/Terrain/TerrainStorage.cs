using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Text;

namespace Sledge.BspEditor.Terrain
{
    /// <summary>Vectors and vector lists as short text.</summary>
    public static class TerrainText
    {
        public static string Write(Vector3 v)
        {
            return TerrainTextureInfo.F(v.X) + "," + TerrainTextureInfo.F(v.Y) + "," + TerrainTextureInfo.F(v.Z);
        }

        public static Vector3 ReadVector(string s)
        {
            var p = (s ?? "").Split(',');
            return p.Length == 3 ? new Vector3(TerrainTextureInfo.P(p[0]), TerrainTextureInfo.P(p[1]), TerrainTextureInfo.P(p[2])) : Vector3.Zero;
        }

        public static string Write(Vector3[] list)
        {
            return list == null ? "" : string.Join(";", list.Select(Write));
        }

        /// <summary>Reads a list back; null when it is empty or isn't the expected length.</summary>
        public static Vector3[] ReadList(string s, int expected)
        {
            if (string.IsNullOrEmpty(s)) return null;
            var parts = s.Split(';');
            if (parts.Length != expected) return null;
            return parts.Select(ReadVector).ToArray();
        }
    }

    /// <summary>The texture settings of one cell's top face. The axes are not kept: they are world aligned when the terrain is baked.</summary>
    public sealed class TerrainTextureInfo
    {
        public string Name = "";
        public float XShift;
        public float YShift;
        public float XScale = 1;
        public float YScale = 1;
        public float Rotation;

        /// <summary>The texture axes as they were, when they were captured from a face (zero when unknown).</summary>
        public Vector3 UAxis;
        public Vector3 VAxis;

        public bool HasAxes => UAxis != Vector3.Zero && VAxis != Vector3.Zero;

        public TerrainTextureInfo Clone()
        {
            return (TerrainTextureInfo) MemberwiseClone();
        }

        public bool SameAs(TerrainTextureInfo o)
        {
            return o != null && Name == o.Name && XShift == o.XShift && YShift == o.YShift
                   && XScale == o.XScale && YScale == o.YScale && Rotation == o.Rotation
                   && UAxis == o.UAxis && VAxis == o.VAxis;
        }

        internal string Key()
        {
            return string.Join("|", Name, F(XShift), F(YShift), F(XScale), F(YScale), F(Rotation),
                F(UAxis.X), F(UAxis.Y), F(UAxis.Z), F(VAxis.X), F(VAxis.Y), F(VAxis.Z));
        }

        internal static TerrainTextureInfo FromKey(string key)
        {
            var p = key.Split('|');
            if (p.Length != 12) return null;
            return new TerrainTextureInfo
            {
                Name = p[0],
                XShift = P(p[1]),
                YShift = P(p[2]),
                XScale = P(p[3]),
                YScale = P(p[4]),
                Rotation = P(p[5]),
                UAxis = new Vector3(P(p[6]), P(p[7]), P(p[8])),
                VAxis = new Vector3(P(p[9]), P(p[10]), P(p[11]))
            };
        }

        internal static string F(float v) => v.ToString("R", CultureInfo.InvariantCulture);

        internal static float P(string s)
        {
            return float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0;
        }
    }

    /// <summary>
    /// Per-cell textures as one short text: the distinct textures first, then the index of each cell's texture (-1 for none).
    /// "name|xshift|yshift|xscale|yscale|rotation;...#0,0,1,-1,..."
    /// </summary>
    public static class TerrainCellTextures
    {
        public static string Write(TerrainTextureInfo[] cells)
        {
            if (cells == null || cells.All(x => x == null)) return "";

            var table = new List<string>();
            var lookup = new Dictionary<string, int>();
            var indices = new List<int>();
            foreach (var c in cells)
            {
                if (c == null || string.IsNullOrEmpty(c.Name))
                {
                    indices.Add(-1);
                    continue;
                }

                var key = c.Key();
                if (!lookup.TryGetValue(key, out var i))
                {
                    i = table.Count;
                    table.Add(key);
                    lookup[key] = i;
                }
                indices.Add(i);
            }

            return string.Join(";", table) + "#" + string.Join(",", indices);
        }

        /// <summary>Reads cell textures back. Returns an array of the expected length (entries are null where nothing is set).</summary>
        public static TerrainTextureInfo[] Read(string text, int expected)
        {
            var result = new TerrainTextureInfo[expected];
            if (string.IsNullOrEmpty(text)) return result;

            var hash = text.IndexOf('#');
            if (hash < 0) return result;

            var table = text.Substring(0, hash).Split(';').Select(TerrainTextureInfo.FromKey).ToList();
            var indices = text.Substring(hash + 1).Split(',');
            for (var i = 0; i < expected && i < indices.Length; i++)
            {
                if (int.TryParse(indices[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out var t) && t >= 0 && t < table.Count)
                {
                    result[i] = table[t]?.Clone();
                }
            }
            return result;
        }

        public static TerrainTextureInfo[] Clone(TerrainTextureInfo[] cells)
        {
            return cells?.Select(x => x?.Clone()).ToArray();
        }

        /// <summary>The same textures for a different grid: each new cell takes the texture of the old cell at its centre.</summary>
        public static TerrainTextureInfo[] Remap(TerrainTextureInfo[] cells, int oldX, int oldY, int newX, int newY)
        {
            var result = new TerrainTextureInfo[newX * newY];
            if (cells == null || cells.Length != oldX * oldY) return result;

            for (var y = 0; y < newY; y++)
            {
                for (var x = 0; x < newX; x++)
                {
                    var ox = Math.Min(oldX - 1, (int) ((x + 0.5f) * oldX / newX));
                    var oy = Math.Min(oldY - 1, (int) ((y + 0.5f) * oldY / newY));
                    result[y * newX + x] = cells[oy * oldX + ox]?.Clone();
                }
            }
            return result;
        }
    }

    public sealed class OriginalFace
    {
        public string Texture = "";
        public Vector3 UAxis, VAxis;
        public float XShift, YShift, XScale = 1, YScale = 1, Rotation;
        public List<Vector3> Vertices = new List<Vector3>();
    }

    public sealed class OriginalSolid
    {
        public int Colour = -1;
        public List<OriginalFace> Faces = new List<OriginalFace>();
    }

    /// <summary>The brushes a terrain was made from, kept as text so the terrain can be turned back into them.</summary>
    public static class TerrainOriginalBrushes
    {
        private static string F(float v) => TerrainTextureInfo.F(v);
        private static float P(string s) => TerrainTextureInfo.P(s);
        private static string V(Vector3 v) => F(v.X) + "," + F(v.Y) + "," + F(v.Z);

        private static Vector3 ParseV(string s)
        {
            var p = s.Split(',');
            return p.Length == 3 ? new Vector3(P(p[0]), P(p[1]), P(p[2])) : Vector3.Zero;
        }

        public static string Write(IEnumerable<OriginalSolid> solids)
        {
            var sb = new StringBuilder();
            var first = true;
            foreach (var s in solids)
            {
                if (!first) sb.Append('~');
                first = false;
                sb.Append(s.Colour.ToString(CultureInfo.InvariantCulture));
                foreach (var f in s.Faces)
                {
                    sb.Append('^');
                    sb.Append(string.Join("|", f.Texture, V(f.UAxis), V(f.VAxis), F(f.XShift), F(f.YShift), F(f.XScale), F(f.YScale), F(f.Rotation),
                        string.Join(";", f.Vertices.Select(V))));
                }
            }
            return sb.ToString();
        }

        public static List<OriginalSolid> Read(string text)
        {
            var result = new List<OriginalSolid>();
            if (string.IsNullOrEmpty(text)) return result;

            foreach (var solidText in text.Split('~'))
            {
                var parts = solidText.Split('^');
                var solid = new OriginalSolid();
                if (int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var colour)) solid.Colour = colour;

                foreach (var faceText in parts.Skip(1))
                {
                    var p = faceText.Split('|');
                    if (p.Length != 9) continue;
                    solid.Faces.Add(new OriginalFace
                    {
                        Texture = p[0],
                        UAxis = ParseV(p[1]),
                        VAxis = ParseV(p[2]),
                        XShift = P(p[3]),
                        YShift = P(p[4]),
                        XScale = P(p[5]),
                        YScale = P(p[6]),
                        Rotation = P(p[7]),
                        Vertices = p[8].Split(';').Where(x => x.Length > 0).Select(ParseV).ToList()
                    });
                }

                if (solid.Faces.Count >= 4) result.Add(solid);
            }
            return result;
        }
    }

    /// <summary>World-aligned texture axes: the same for every face that faces the same way, so a texture runs on across brushes.</summary>
    public static class TerrainTextureAxes
    {
        /// <summary>0, 1 or 2 for the world axis (x, y, z) the normal is closest to.</summary>
        public static int ClosestAxis(Vector3 normal)
        {
            var ax = Math.Abs(normal.X);
            var ay = Math.Abs(normal.Y);
            var az = Math.Abs(normal.Z);
            if (az >= ax && az >= ay) return 2;
            return ax >= ay ? 0 : 1;
        }

        /// <summary>
        /// The axes for a face: the ones captured from the face before, if they project the same way as the face does now
        /// (so a rotation the user set is kept); otherwise the world-aligned ones.
        /// </summary>
        public static void Resolve(Vector3 normal, TerrainTextureInfo info, out Vector3 uAxis, out Vector3 vAxis)
        {
            if (info != null && info.HasAxes)
            {
                var projection = Vector3.Cross(info.UAxis, info.VAxis);
                if (projection.LengthSquared() > 1e-8f && ClosestAxis(projection) == ClosestAxis(normal))
                {
                    uAxis = info.UAxis;
                    vAxis = info.VAxis;
                    return;
                }
            }
            ForNormal(normal, out uAxis, out vAxis);
        }

        /// <summary>Axes for a face by the world axis its normal is closest to (the standard world alignment).</summary>
        public static void ForNormal(Vector3 normal, out Vector3 uAxis, out Vector3 vAxis)
        {
            var ax = Math.Abs(normal.X);
            var ay = Math.Abs(normal.Y);
            var az = Math.Abs(normal.Z);

            if (az >= ax && az >= ay)
            {
                uAxis = new Vector3(1, 0, 0);
                vAxis = new Vector3(0, -1, 0);
            }
            else if (ax >= ay)
            {
                uAxis = new Vector3(0, 1, 0);
                vAxis = new Vector3(0, 0, -1);
            }
            else
            {
                uAxis = new Vector3(1, 0, 0);
                vAxis = new Vector3(0, 0, -1);
            }
        }
    }
}
