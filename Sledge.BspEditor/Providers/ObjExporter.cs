using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using Sledge.BspEditor.Primitives.MapObjects;

namespace Sledge.BspEditor.Providers
{
    /// <summary>
    /// Axis conventions the exporter can convert to.
    /// </summary>
    public enum ObjAxisPreset
    {
        /// <summary>No conversion: Z-up, right-handed (the editor's own space).</summary>
        Source,

        /// <summary>Y-up, -Z forward. Imports upright with Blender's default OBJ importer settings.</summary>
        Blender,

        /// <summary>Z-up, right-handed. Same as the editor's space, so no rotation is needed.</summary>
        Max
    }

    public class ObjExportOptions
    {
        public bool SelectedOnly { get; set; }
        public bool ZeroOrigin { get; set; }
        public ObjAxisPreset Axis { get; set; } = ObjAxisPreset.Source;
    }

    public static class ObjExporter
    {
        public static string GetAxisLabel(ObjAxisPreset axis)
        {
            switch (axis)
            {
                case ObjAxisPreset.Blender: return "Blender (Y-up, -Z forward)";
                case ObjAxisPreset.Max: return "3ds Max (Z-up)";
                default: return "Source (Z-up, as-is)";
            }
        }

        public static void Write(IReadOnlyCollection<Solid> solids, ObjExportOptions options, TextWriter writer)
        {
            // "Zero out origin": move the centre of the exported geometry's bounding box to (0,0,0)
            var offset = Vector3.Zero;
            if (options.ZeroOrigin)
            {
                var all = solids.SelectMany(s => s.Faces).SelectMany(f => f.Vertices).ToList();
                if (all.Count > 0)
                {
                    var min = new Vector3(all.Min(v => v.X), all.Min(v => v.Y), all.Min(v => v.Z));
                    var max = new Vector3(all.Max(v => v.X), all.Max(v => v.Y), all.Max(v => v.Z));
                    offset = (min + max) / 2;
                }
            }

            writer.WriteLine("# Hammertime OBJ export");
            writer.WriteLine("# Axis: " + GetAxisLabel(options.Axis));
            writer.WriteLine("# Origin zeroed: " + (options.ZeroOrigin ? "yes" : "no"));
            writer.WriteLine("# Scale: 1");
            writer.WriteLine();

            // OBJ indices are 1-based and absolute; keep running totals of what's been written
            var vCount = 0;
            var vtCount = 0;
            var vnCount = 0;

            foreach (var solid in solids)
            {
                writer.WriteLine("o solid_" + solid.ID.ToString(CultureInfo.InvariantCulture));

                string lastMaterial = null;

                foreach (var face in solid.Faces)
                {
                    var verts = face.Vertices.ToList();
                    if (verts.Count < 3) continue;

                    var material = SanitiseName(face.Texture?.Name);
                    if (material != lastMaterial)
                    {
                        writer.WriteLine("usemtl " + material);
                        lastMaterial = material;
                    }

                    var normal = face.Plane.Normal;

                    foreach (var v in verts)
                    {
                        var p = ConvertAxis(v - offset, options.Axis);
                        writer.WriteLine("v " + Fmt(p.X) + " " + Fmt(p.Y) + " " + Fmt(p.Z));
                    }

                    foreach (var v in verts)
                    {
                        // UVs come from the original (un-offset) position so they don't slide when the origin is zeroed
                        var uv = ProjectUv(v, normal);
                        writer.WriteLine("vt " + Fmt(uv.X) + " " + Fmt(uv.Y));
                    }

                    var n = ConvertAxis(normal, options.Axis);
                    writer.WriteLine("vn " + Fmt(n.X) + " " + Fmt(n.Y) + " " + Fmt(n.Z));

                    // Editor faces are wound the opposite way to OBJ, so write the indices in reverse
                    writer.Write("f");
                    for (var i = verts.Count - 1; i >= 0; i--)
                    {
                        writer.Write(" " + (vCount + i + 1).ToString(CultureInfo.InvariantCulture)
                                     + "/" + (vtCount + i + 1).ToString(CultureInfo.InvariantCulture)
                                     + "/" + (vnCount + 1).ToString(CultureInfo.InvariantCulture));
                    }
                    writer.WriteLine();

                    vCount += verts.Count;
                    vtCount += verts.Count;
                    vnCount += 1;
                }

                writer.WriteLine();
            }
        }

        /// <summary>
        /// Both conversions are proper rotations (determinant +1), so face winding is unaffected.
        /// </summary>
        private static Vector3 ConvertAxis(Vector3 v, ObjAxisPreset axis)
        {
            switch (axis)
            {
                case ObjAxisPreset.Blender:
                    // Z-up -> Y-up, -Z forward: (x, y, z) -> (x, z, -y)
                    return new Vector3(v.X, v.Z, -v.Y);
                default:
                    // Source and 3ds Max are both Z-up, right-handed
                    return v;
            }
        }

        private static Vector2 ProjectUv(Vector3 v, Vector3 normal)
        {
            // Simple planar projection onto the face's own plane. The basis is built from the normal
            // directly so faces pointing straight up or down don't collapse.
            var up = Math.Abs(normal.Z) > 0.999f ? Vector3.UnitY : Vector3.UnitZ;
            var u = Vector3.Normalize(Vector3.Cross(up, normal));
            var w = Vector3.Cross(normal, u);
            return new Vector2(Vector3.Dot(v, u), Vector3.Dot(v, w)) * 0.01f;
        }

        private static string SanitiseName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "none";
            return name.Trim().Replace(' ', '_');
        }

        private static string Fmt(float f)
        {
            if (Math.Abs(f) < 0.00005f) f = 0; // avoid "-0.0000"
            return f.ToString("0.0000", CultureInfo.InvariantCulture);
        }
    }
}
