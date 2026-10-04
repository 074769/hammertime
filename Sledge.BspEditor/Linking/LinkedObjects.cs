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

        private static readonly HashSet<string> IgnoredProperties = new HashSet<string> { "ID", "IsSelected", "ParentID", "angles", "angle", "pitch", "roll" };

        // Entity keys that say how the entity is turned. Like position, they belong to each object and aren't shared.
        private static readonly string[] OrientationKeys = { "angles", "angle", "pitch", "roll" };
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

        public static long GetTopId(IMapObject obj)
        {
            var d = obj?.Data.GetOne<LinkGroupID>();
            if (d == null) return 0;
            return d.TopID != 0 ? d.TopID : d.ID;
        }

        /// <summary>
        /// True if this object isn't inside another linked object (eg a brush of a linked brush entity).
        /// </summary>
        public static bool IsTopLevel(IMapObject obj)
        {
            return FindLinkedOwner(obj.Hierarchy.Parent) == null;
        }

        /// <summary>
        /// The brushes inside an object (a brush entity or group), at any depth.
        /// </summary>
        public static List<Solid> ChildSolids(IMapObject obj)
        {
            return obj.FindAll().Where(x => !ReferenceEquals(x, obj)).OfType<Solid>().ToList();
        }

        /// <summary>
        /// Match the brushes inside <paramref name="obj"/> to the brushes inside <paramref name="counterpart"/>
        /// (which is already linked): the same shape, and the closest to the same place inside its entity.
        /// Returns the slot of the counterpart brush each brush matches. Brushes that match nothing aren't in the result.
        /// </summary>
        public static Dictionary<Solid, long> MatchChildren(IMapObject obj, IMapObject counterpart)
        {
            var result = new Dictionary<Solid, long>();
            var mine = ChildSolids(obj);
            var theirs = ChildSolids(counterpart).Where(x => GetLinkId(x) != null).ToList();
            if (mine.Count == 0 || theirs.Count == 0) return result;

            var myCentre = obj.BoundingBox.Center;
            var theirCentre = counterpart.BoundingBox.Center;

            var pairs = new List<Tuple<float, Solid, Solid>>();
            foreach (var m in mine)
            {
                foreach (var t in theirs)
                {
                    if (!Congruent(m, t)) continue;
                    var distance = ((m.BoundingBox.Center - myCentre) - (t.BoundingBox.Center - theirCentre)).Length();
                    pairs.Add(Tuple.Create(distance, m, t));
                }
            }

            var used = new HashSet<Solid>();
            foreach (var p in pairs.OrderBy(x => x.Item1))
            {
                if (result.ContainsKey(p.Item2) || used.Contains(p.Item3)) continue;
                result[p.Item2] = GetLinkId(p.Item3).Value;
                used.Add(p.Item3);
            }
            return result;
        }

        public static long GetInstance(IMapObject obj)
        {
            return obj?.Data.GetOne<LinkGroupID>()?.Instance ?? 0;
        }

        /// <summary>
        /// A look at every link in a document at once: its slots, instances, and origin instance.
        /// </summary>
        public class LinkIndex
        {
            public Dictionary<long, LinkGroup> Groups = new Dictionary<long, LinkGroup>();

            /// <summary>Slot ID to the objects in that slot (one per instance).</summary>
            public Dictionary<long, List<IMapObject>> Slots = new Dictionary<long, List<IMapObject>>();

            /// <summary>Link ID to instance number to the objects of that instance.</summary>
            public Dictionary<long, SortedDictionary<long, List<IMapObject>>> Links = new Dictionary<long, SortedDictionary<long, List<IMapObject>>>();

            public string NameOf(long id)
            {
                return Groups.TryGetValue(id, out var g) ? g.Name : "Link " + id;
            }

            public Color ColourOf(long id)
            {
                return Groups.TryGetValue(id, out var g) ? g.Colour : ColourFor(id);
            }

            /// <summary>The origin instance of a link: the one it names, or else the lowest numbered.</summary>
            public long OriginInstance(long linkId)
            {
                if (!Links.TryGetValue(linkId, out var instances) || instances.Count == 0) return 0;
                if (Groups.TryGetValue(linkId, out var g) && g.OriginInstance != 0 && instances.ContainsKey(g.OriginInstance)) return g.OriginInstance;
                return instances.Keys.First();
            }

            /// <summary>The origin object of a slot: its member in the link's origin instance, or else the lowest ID member.</summary>
            public IMapObject OriginOf(long slotId)
            {
                if (!Slots.TryGetValue(slotId, out var members) || members.Count == 0) return null;
                var origin = OriginInstance(GetTopId(members[0]));
                return members.FirstOrDefault(x => GetInstance(x) == origin) ?? members.OrderBy(x => x.ID).First();
            }
        }

        public static LinkIndex BuildIndex(MapDocument document)
        {
            var index = new LinkIndex();
            foreach (var g in document.Map.Data.Get<LinkGroup>())
            {
                if (!index.Groups.ContainsKey(g.ID)) index.Groups[g.ID] = g;
            }

            foreach (var o in document.Map.Root.FindAll())
            {
                var d = o.Data.GetOne<LinkGroupID>();
                if (d == null) continue;

                if (!index.Slots.TryGetValue(d.ID, out var slot)) index.Slots[d.ID] = slot = new List<IMapObject>();
                slot.Add(o);

                // The instances list the objects of the group. The brushes inside a linked entity are parts of that
                // entity (they have slots of their own so they can be edited, but they aren't separate objects of the group).
                if (!IsTopLevel(o)) continue;

                var top = GetTopId(o);
                if (!index.Links.TryGetValue(top, out var instances)) index.Links[top] = instances = new SortedDictionary<long, List<IMapObject>>();
                if (!instances.TryGetValue(d.Instance, out var list)) instances[d.Instance] = list = new List<IMapObject>();
                list.Add(o);
            }
            return index;
        }

        /// <summary>
        /// Move an object (and everything in it), keeping its textures stuck to its faces.
        /// If the movement is a flip, the faces are turned so they keep facing outwards.
        /// </summary>
        public static void TransformObject(IMapObject obj, Matrix4x4 matrix, bool mirrored)
        {
            obj.Transform(matrix);
            foreach (var o in obj.FindAll())
            {
                foreach (var t in o.Data.OfType<ITextured>()) t.Texture?.TransformUniform(matrix);
                if (mirrored)
                {
                    foreach (var f in o.Data.OfType<Face>()) f.Vertices.Flip();
                }
            }
        }

        /// <summary>
        /// True if the two objects are the same shape (position and orientation aside), so one could be a copy of the other.
        /// </summary>
        public static bool Congruent(IMapObject a, IMapObject b)
        {
            if (a.GetType() != b.GetType()) return false;
            if (a is Solid sa && b is Solid sb)
            {
                return LinkGeometry.Rigid.Fit(LinkGeometry.Snapshot.Take(sa), LinkGeometry.Snapshot.Take(sb)) != null;
            }
            return AreEquivalent(a, b);
        }

        /// <summary>
        /// The IDs of the groups that are worth keeping: those with members, and the groups above them (sublinks hang off their parents).
        /// </summary>
        public static HashSet<long> GetKeptGroupIds(MapDocument document)
        {
            var groups = document.Map.Data.Get<LinkGroup>().GroupBy(x => x.ID).ToDictionary(x => x.Key, x => x.First());
            var kept = new HashSet<long>();
            foreach (var id in GetMembers(document).Keys)
            {
                var current = id;
                var guard = 0;
                while (current != 0 && kept.Add(current) && guard++ < 64)
                {
                    current = groups.TryGetValue(current, out var g) ? g.ParentID : 0;
                }
            }
            return kept;
        }

        /// <summary>
        /// The folder in the visgroup list that holds the instances of a link.
        /// </summary>
        public static string GetInstancesPath(LinkGroup link)
        {
            return AutoVisgroupPath + "/" + SafeName(link?.Name) + " (instances)";
        }

        /// <summary>
        /// Names become visgroup path segments, so they can't contain the separator.
        /// </summary>
        public static string SafeName(string name)
        {
            return (name ?? "").Replace('/', '-').Replace('\\', '-').Trim();
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
        public static SerialisedObject NormalisedForm(IMapObject obj)
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
            return Equivalent(NormalisedForm(a), NormalisedForm(b));
        }

        public static bool Equivalent(SerialisedObject a, SerialisedObject b)
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
        /// Give <paramref name="target"/> the entity properties of <paramref name="source"/>, except the ones that say how it is turned.
        /// Nothing else about the target changes (no objects are added or removed), so this is safe for entities with brushes.
        /// </summary>
        public static void SyncProperties(IMapObject source, IMapObject target, Change change)
        {
            var from = source.Data.GetOne<EntityData>();
            var to = target.Data.GetOne<EntityData>();
            if (from?.Properties == null || to?.Properties == null) return;

            bool Own(string key) => OrientationKeys.Contains(key.ToLowerInvariant());

            var wanted = from.Properties.Where(x => !Own(x.Key)).ToDictionary(x => x.Key, x => x.Value);
            foreach (var kv in to.Properties.Where(x => Own(x.Key))) wanted[kv.Key] = kv.Value;

            var same = wanted.Count == to.Properties.Count && wanted.All(x => to.Properties.TryGetValue(x.Key, out var v) && v == x.Value);
            if (same && to.Name == from.Name && to.Flags == from.Flags) return;

            to.Name = from.Name;
            to.Flags = from.Flags;
            to.Properties = wanted;
            change.Update(target);
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
            var ownOrientation = target.Data.GetOne<EntityData>()?.Properties
                ?.Where(x => OrientationKeys.Contains(x.Key.ToLowerInvariant()))
                .ToDictionary(x => x.Key, x => x.Value);
            var keep = target.Data.Where(IsPerObjectData).Select(x => (IMapObjectData) x.Clone()).ToList();
            var oldDescendants = target.FindAll().Where(x => !ReferenceEquals(x, target)).ToList();

            // Copy gives the copy (and its children) fresh IDs, so nothing collides with the source's objects
            var copy = (IMapObject) source.Copy(document.Map.NumberGenerator);
            Translate(copy, center - copy.BoundingBox.Center);
            copy.Data.Remove(IsPerObjectData);
            copy.Data.AddRange(keep);

            target.Unclone(copy);
            target.IsSelected = wasSelected;

            var targetData = target.Data.GetOne<EntityData>();
            if (targetData?.Properties != null)
            {
                foreach (var key in targetData.Properties.Keys.Where(x => OrientationKeys.Contains(x.ToLowerInvariant())).ToList())
                {
                    targetData.Properties.Remove(key);
                }
                if (ownOrientation != null)
                {
                    foreach (var kv in ownOrientation) targetData.Properties[kv.Key] = kv.Value;
                }
            }
            target.DescendantsChanged();

            var newDescendants = target.FindAll().Where(x => !ReferenceEquals(x, target)).ToList();
            change.RemoveRange(oldDescendants);
            change.AddRange(newDescendants);
            change.Update(target);
        }
    }
}
