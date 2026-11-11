using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.RegularExpressions;
using Sledge.DataStructures.Geometric;

namespace Sledge.BspEditor.Editing.CompileErrors
{
    /// <summary>
    /// Reads an SDHLT compile log (hlcsg, hlbsp, hlvis, hlrad) into a list of entries.
    /// Message formats come from the SDHLT source (sdHLCSG, sdHLBSP, sdHLVIS, common/log.cpp).
    /// </summary>
    public static class CompileErrorsParser
    {
        private static readonly Regex MessageRx = new Regex(@"(Warning|Error): (.*)$");
        private static readonly Regex ToolRx = new Regex(@"BEGIN\s+sd(HL\w+)");
        private static readonly Regex HullRx = new Regex(@"hull \[?(\d+)\]?");
        private static readonly Regex EntityBrushRx = new Regex(@"Entity (\d+), Brush (\d+)");
        private static readonly Regex EntityNumberRx = new Regex(@"Entity (\d+) \(([^)]*)\)");
        private static readonly Regex ClassnameRx = new Regex(@"classname ""([^""]*)""");
        private static readonly Regex TargetnameRx = new Regex(@"targetname ""([^""]*)""");
        private static readonly Regex VecRx = new Regex(@"\(\s*(-?[\d.]+)\s+(-?[\d.]+)\s+(-?[\d.]+)\s*\)");
        private static readonly Regex PointLineRx = new Regex(@"^\s*\(\s*(-?[\d.]+)\s+(-?[\d.]+)\s+(-?[\d.]+)\s*\)\s*$");
        private static readonly Regex PortalHeaderRx = new Regex(@"Problem at portal between leaves \d+ and \d+");
        private static readonly Regex LeakLineRx = new Regex(@"^\s*Entity .+ at \(");

        // Matched in order; the first entry whose text appears in the message wins.
        private static readonly (string Match, string Key, string Name, string Category)[] Types =
        {
            ("Illegal Brush (edge without opposite face)", "IllegalBrushEdge", "Illegal brush: edge without opposite face", "Geometry"),
            ("Illegal Brush (clip hull", "IllegalBrushBackFace", "Illegal brush: clip hull backward face", "Geometry"),
            ("outside world", "BrushOutsideWorld", "Brush outside world", "Coordinates"),
            ("origin outside", "OriginOutsideRange", "Entity origin outside range", "Coordinates"),
            ("Ambiguous leafnode content", "AmbiguousLeafnode", "Ambiguous leafnode content", "Coordinates"),
            ("No visible brushes in solid entity", "NoVisibleBrushes", "Solid entity without visible brushes", "Geometry"),
            ("off-grid", "OffGridVertex", "Hull shape vertex off-grid", "Coordinates"),
            ("Degenerate QuArK-style", "DegenerateTexture", "Degenerate QuArK-style texture", "Texture"),
            ("LEAK", "Leak", "Leak", "Coordinates"),
            ("Leaf portals saw into leaf", "LeafPortal", "Leaf portals saw into leaf", "Vis"),
            ("bbox overlaps", "BBoxOverlap", "Entity bbox overlaps too many leaves", "Vis"),
            ("ChopWinding", "ChopWinding", "ChopWinding rejected (too many points)", "Vis"),
            ("intersects with brush", "BrushIntersect", "Brushes intersect", "Geometry"),
            ("coplanar", "CoplanarFaces", "Brush with coplanar faces", "Geometry"),
            ("plane with no normal", "PlaneNoNormal", "Plane with no normal", "Geometry"),
            ("mixed face contents", "MixedContents", "Mixed face contents", "Geometry"),
            ("Malformed texture alignment", "TextureAlignment", "Malformed texture alignment", "Texture"),
            ("Bad surface extents", "BadSurfaceExtents", "Bad surface extents", "Texture"),
            ("Exceeded", "Limit", "Compiler limit exceeded", "Limits"),
        };

        public static List<CompileErrorsEntry> Parse(string path)
        {
            var text = File.ReadAllText(path);

            // SDHLT writes CRLF, bare CR and doubled CR ("\r\r\n") line endings in the same file.
            // Each run of CRs (plus an optional LF) becomes exactly one line break.
            var lines = Regex.Replace(text, @"\r+\n?", "\n").Split('\n');

            var result = new List<CompileErrorsEntry>();
            var tool = "";
            CompileErrorsEntry continuation = null;

            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i];

                var tm = ToolRx.Match(line);
                if (tm.Success)
                {
                    tool = tm.Groups[1].Value.ToLowerInvariant();
                    continuation = null;
                    continue;
                }

                // Lines that belong to the previous message (portal corners, leak location)
                if (continuation != null)
                {
                    if (continuation.TypeKey == "LeafPortal" && TryPoint(line, out var p))
                    {
                        continuation.Points.Add(p);
                        continue;
                    }
                    if (continuation.TypeKey == "Leak" && LeakLineRx.IsMatch(line))
                    {
                        var v = VecRx.Match(line);
                        if (v.Success) continuation.Points.Add(ToVec(v));
                        continuation.Text += "\n" + line.Trim();
                        continuation = null;
                        continue;
                    }
                    continuation = null;
                }

                if (PortalHeaderRx.IsMatch(line))
                {
                    var last = result.LastOrDefault();
                    if (last != null && last.TypeKey == "LeafPortal")
                    {
                        last.Text += "\n" + line.Trim();
                        continuation = last;
                    }
                    continue;
                }

                // Messages can follow progress dots on the same line, so search rather than anchor
                var mm = MessageRx.Match(line);
                if (!mm.Success) continue;

                var entry = Build(mm.Groups[1].Value, mm.Groups[2].Value.Trim(), tool, i + 1);
                result.Add(entry);

                if (entry.TypeKey == "LeafPortal" || entry.TypeKey == "Leak") continuation = entry;
            }

            return result;
        }

        private static CompileErrorsEntry Build(string kind, string msg, string tool, int lineNumber)
        {
            var e = new CompileErrorsEntry
            {
                Tool = tool,
                Severity = kind == "Error" ? CompileErrorsSeverity.Error : CompileErrorsSeverity.Warning,
                LineNumber = lineNumber,
                Text = msg,
            };

            var hm = HullRx.Match(msg);
            if (hm.Success) e.Hull = int.Parse(hm.Groups[1].Value, CultureInfo.InvariantCulture);

            var eb = EntityBrushRx.Match(msg);
            if (eb.Success)
            {
                e.Entity = int.Parse(eb.Groups[1].Value, CultureInfo.InvariantCulture);
                e.Brush = int.Parse(eb.Groups[2].Value, CultureInfo.InvariantCulture);
            }
            else
            {
                var en = EntityNumberRx.Match(msg);
                if (en.Success)
                {
                    e.Entity = int.Parse(en.Groups[1].Value, CultureInfo.InvariantCulture);
                    var inner = en.Groups[2].Value;
                    var cm = ClassnameRx.Match(inner);
                    e.EntityClassname = cm.Success ? cm.Groups[1].Value : inner;
                }
            }

            if (e.EntityClassname == null)
            {
                var cm = ClassnameRx.Match(msg);
                if (cm.Success) e.EntityClassname = cm.Groups[1].Value;
            }

            var tn = TargetnameRx.Match(msg);
            if (tn.Success) e.EntityTargetname = tn.Groups[1].Value;

            var vecs = VecRx.Matches(msg).Cast<Match>().Select(ToVec).ToList();
            if (vecs.Count >= 2 && msg.Contains(")-("))
            {
                e.Box = new Box(Vector3.Min(vecs[0], vecs[1]), Vector3.Max(vecs[0], vecs[1]));
            }
            else
            {
                e.Points.AddRange(vecs);
            }

            Classify(e, msg);
            return e;
        }

        private static void Classify(CompileErrorsEntry e, string msg)
        {
            var match = Types.FirstOrDefault(t => msg.Contains(t.Match));
            if (match.Match == null)
            {
                e.TypeKey = "Other";
                e.TypeName = "Other";
                e.Category = "Other";
            }
            else
            {
                e.TypeKey = match.Key;
                e.TypeName = match.Name;
                e.Category = match.Category;
            }

            if (e.TypeKey == "AmbiguousLeafnode")
            {
                var upperHull = (e.Hull ?? 0) > 0;
                e.FilterKey = upperHull ? "AmbiguousLeafnode.Hull1Plus" : "AmbiguousLeafnode.Hull0";
                e.FilterLabel = upperHull ? "Ambiguous leafnode (hulls 1+)" : "Ambiguous leafnode (hull 0)";
            }
            else
            {
                e.FilterKey = e.TypeKey;
                e.FilterLabel = e.TypeName;
            }
        }

        private static bool TryPoint(string line, out Vector3 p)
        {
            var m = PointLineRx.Match(line);
            if (m.Success)
            {
                p = ToVec(m);
                return true;
            }
            p = default(Vector3);
            return false;
        }

        private static Vector3 ToVec(Match m)
        {
            return new Vector3(
                float.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture),
                float.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture),
                float.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture));
        }
    }
}
