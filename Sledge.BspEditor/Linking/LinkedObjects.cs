using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Text.RegularExpressions;
using Sledge.BspEditor.Documents;
using Sledge.BspEditor.Modification;
using Sledge.BspEditor.Primitives;
using Sledge.BspEditor.Primitives.MapData;
using Sledge.BspEditor.Primitives.MapObjectData;
using Sledge.BspEditor.Primitives.MapObjects;
using Sledge.Common.Transport;

namespace Sledge.BspEditor.Linking
{
    /// <summary>
    /// Helpers for the "linked objects" feature.
    ///
    /// A link group is a <see cref="LinkGroup"/> in the map data plus a <see cref="LinkGroupID"/> on each member object.
    /// Members share their shape, textures, and properties - each member keeps its own position.
    /// </summary>
    public static class LinkedObjects
    {
        /// <summary>The (translation key) path of the "Linked Objects" folder in the visgroup panel.</summary>
        public const string AutoVisgroupPath = "Sledge.BspEditor.AutomaticVisgroups.LinkedObjects";

        // Numbers closer than this are considered the same when comparing objects (absorbs float error from translating)
        private const double Epsilon = 0.02;

        private static readonly HashSet<string> IgnoredProperties = new HashSet<string> { "ID", "IsSelected", "ParentID" };
        private static readonly HashSet<string> IgnoredChildren = new HashSet<string> { "VisgroupID", "VisgroupHidden", "LinkGroupID", "QuickHidden" };
        private static readonly Regex Number = new Regex(@"-?\d+(?:\.\d+)?(?:[eE][-+]?\d+)?", RegexOptions.Compiled);

        public static long? GetLinkId(IMapObject obj)
        {
            return obj?.Data.GetOne<LinkGroupID>()?.ID;
        }

        /// <summary>
        /// Find the object that carries the link for this object: itself, or its closest linked ancestor.
        /// </summary>
        public static IMapObject FindLinkedOwner(IMapObject obj)
        {
            var o = obj;
            while (o != null)
            {
                if (GetLinkId(o) != null) return o;
                o = o.Hierarchy.Parent;
            }
            return null;
        }

        /// <summary>
        /// Get all the linked objects in the document, grouped by link group ID.
        /// </summary>
        public static Dictionary<long, List<IMapObject>> GetMembers(MapDocument document)
        {
            var result = new Dictionary<long, List<IMapObject>>();
            foreach (var o in document.Map.Root.FindAll())
            {
                var id = GetLinkId(o);
                if (id == null) continue;
                if (!result.TryGetValue(id.Value, out var list)) result[id.Value] = list = new List<IMapObject>();
                list.Add(o);
            }
            return result;
        }

        /// <summary>
        /// Get an unused link group ID for the document.
        /// </summary>
        public static long NextGroupId(MapDocument document)
        {
            var max = 0L;
            foreach (var g in document.Map.Data.Get<LinkGroup>()) max = Math.Max(max, g.ID);
            foreach (var o in document.Map.Root.FindAll())
            {
                var id = GetLinkId(o);
                if (id != null) max = Math.Max(max, id.Value);
            }
            return max + 1;
        }

        /// <summary>
        /// Get a stable, well-spread colour for a link group ID.
        /// </summary>
        public static Color ColourFor(long id)
        {
            var hue = (id * 137.508) % 360.0;
            return FromHsv(hue, 0.75, 1.0);
        }

        private static Color FromHsv(double hue, double saturation, double value)
        {
            var hi = (int) Math.Floor(hue / 60) % 6;
            var f = hue / 60 - Math.Floor(hue / 60);
            var v = (int) (value * 255);
            var p = (int) (value * (1 - saturation) * 255);
            var q = (int) (value * (1 - f * saturation) * 255);
            var t = (int) (value * (1 - (1 - f) * saturation) * 255);
            switch (hi)
            {
                case 0: return Color.FromArgb(v, t, p);
                case 1: return Color.FromArgb(q, v, p);
                case 2: return Color.FromArgb(p, v, t);
                case 3: return Color.FromArgb(p, q, v);
                case 4: return Color.FromArgb(t, p, v);
                default: return Color.FromArgb(v, p, q);
            }
        }

        // Data that belongs to the object itself rather than to its shape, and so isn't shared with the rest of the group
        private static bool IsPerObjectData(IMapObjectData data)
        {
            return data is LinkGroupID || data is VisgroupID || data is IObjectVisibility;
        }

        // Move an object, keeping its textures stuck to its faces
        private static void Translate(IMapObject obj, Vector3 offset)
        {
            var matrix = Matrix4x4.CreateTranslation(offset);
            obj.Transform(matrix);
            foreach (var o in obj.FindAll())
            {
                foreach (var t in o.Data.OfType<ITextured>())
                {
                    t.Texture?.TransformUniform(matrix);
                }
            }
        }

        /// <summary>
        /// Get a copy of the object's serialised form, moved to the origin, with the per-object data left out.
        /// Two objects with equivalent shapes will have equivalent serialised forms wherever they are in the map.
        /// </summary>
        private static SerialisedObject Normalise(IMapObject obj)
        {
            var clone = (IMapObject) obj.Clone();
            Translate(clone, -clone.BoundingBox.Center);
            return clone.ToSerialisedObject();
        }

        /// <summary>
        /// True if the two objects have the same shape, textures, and properties. Position and selection are ignored.
        /// </summary>
        public static bool AreEquivalent(IMapObject a, IMapObject b)
        {
            if (ReferenceEquals(a, b)) return true;
            if (a.GetType() != b.GetType()) return false;
            return Equivalent(Normalise(a), Normalise(b));
        }

        private static bool Equivalent(SerialisedObject a, SerialisedObject b)
        {
            if (a.Name != b.Name) return false;

            var pa = a.Properties.Where(x => !IgnoredProperties.Contains(x.Key)).ToList();
            var pb = b.Properties.Where(x => !IgnoredProperties.Contains(x.Key)).ToList();
            if (pa.Count != pb.Count) return false;

            var usedProps = new bool[pb.Count];
            foreach (var p in pa)
            {
                var found = false;
                for (var i = 0; i < pb.Count; i++)
                {
                    if (usedProps[i] || pb[i].Key != p.Key || !ValuesEqual(p.Value, pb[i].Value)) continue;
                    usedProps[i] = true;
                    found = true;
                    break;
                }
                if (!found) return false;
            }

            // Children can be in any order (hierarchy children aren't ordered)
            var ca = a.Children.Where(x => !IgnoredChildren.Contains(x.Name)).ToList();
            var cb = b.Children.Where(x => !IgnoredChildren.Contains(x.Name)).ToList();
            if (ca.Count != cb.Count) return false;

            var usedChildren = new bool[cb.Count];
            foreach (var c in ca)
            {
                var found = false;
                for (var i = 0; i < cb.Count; i++)
                {
                    if (usedChildren[i] || !Equivalent(c, cb[i])) continue;
                    usedChildren[i] = true;
                    found = true;
                    break;
                }
                if (!found) return false;
            }

            return true;
        }

        private static bool ValuesEqual(string x, string y)
        {
            if (x == y) return true;
            if (x == null || y == null) return false;

            var nx = Number.Matches(x);
            var ny = Number.Matches(y);
            if (nx.Count == 0 || nx.Count != ny.Count) return false;
            if (Number.Replace(x, "#") != Number.Replace(y, "#")) return false;

            for (var i = 0; i < nx.Count; i++)
            {
                if (!double.TryParse(nx[i].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var dx)) return false;
                if (!double.TryParse(ny[i].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var dy)) return false;
                if (Math.Abs(dx - dy) > Epsilon) return false;
            }
            return true;
        }

        /// <summary>
        /// Make <paramref name="target"/> the same as <paramref name="source"/> (shape, textures, properties, children),
        /// keeping the target's own position, selection state, visgroups, and link membership.
        /// The change is recorded in <paramref name="change"/>.
        /// </summary>
        public static void Sync(MapDocument document, IMapObject source, IMapObject target, Change change)
        {
            if (source.GetType() != target.GetType()) return;

            var center = target.BoundingBox.Center;
            var wasSelected = target.IsSelected;
            var keep = target.Data.Where(IsPerObjectData).Select(x => (IMapObjectData) x.Clone()).ToList();
            var oldDescendants = target.FindAll().Where(x => !ReferenceEquals(x, target)).ToList();

            // Copy gives the copy (and its children) fresh IDs, so nothing collides with the source's objects
            var copy = (IMapObject) source.Copy(document.Map.NumberGenerator);
            Translate(copy, center - copy.BoundingBox.Center);
            copy.Data.Remove(IsPerObjectData);
            copy.Data.AddRange(keep);

            target.Unclone(copy);
            target.IsSelected = wasSelected;
            target.DescendantsChanged();

            var newDescendants = target.FindAll().Where(x => !ReferenceEquals(x, target)).ToList();
            change.RemoveRange(oldDescendants);
            change.AddRange(newDescendants);
            change.Update(target);
        }
    }
}
