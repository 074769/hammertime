using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Sledge.BspEditor.Documents;
using Sledge.BspEditor.Modification;
using Sledge.BspEditor.Modification.Operations;
using Sledge.BspEditor.Modification.Operations.Data;
using Sledge.BspEditor.Primitives.MapObjects;
using Sledge.Common.Threading;

using Sledge.BspEditor.Rendering.Debugging;

namespace Sledge.BspEditor.Tools.Vertex.Selection
{
    public class VertexSelection : IEnumerable<VertexSolid>
    {
        private readonly object _lock = new object();
        private readonly ISet<VertexSolid> _selectedSolids;

        public VertexSelection()
        {
            _selectedSolids = new ThreadSafeSet<VertexSolid>();
        }

        public async Task Clear(MapDocument document)
        {
            var tran = new Transaction();
            Task pending = null;

            lock (_lock)
            {
                if (_selectedSolids.Any())
                {
                    var toDeselect = _selectedSolids.ToList();
                    tran.Add(new TrivialOperation(
                        d => toDeselect.ForEach(x => x.Real.Data.Remove(o => o is VertexHidden)),
                        c => c.UpdateRange(toDeselect.Select(x => x.Real))
                    ));

                    _selectedSolids.Clear();
                }

                // Queued while the lock is held, so the map sees the changes in the same order the selection changed
                if (!tran.IsEmpty) pending = MapDocumentOperation.Perform(document, tran);
            }

            if (pending != null) await pending;
        }

        public async Task Update(MapDocument document)
        {
            if (document == null) return;
            
            var tran = new Transaction();
            Task pending = null;

            lock (_lock)
            {
                var selection = new HashSet<Solid>(document.Selection.OfType<Solid>());
                var toSelect = selection.Except(_selectedSolids.Select(x => x.Real)).ToList();
                var toDeselect = _selectedSolids.Select(x => x.Real).Except(selection).ToList();

                if (toSelect.Any())
                {
                    tran.Add(new TrivialOperation(
                        d => toSelect.ForEach(x => x.Data.Add(new VertexHidden())),
                        c => c.UpdateRange(toSelect)
                    ));
                }

                if (toDeselect.Any())
                {
                    tran.Add(new TrivialOperation(
                        d => toDeselect.ForEach(x => x.Data.Remove(o => o is VertexHidden)),
                        c => c.UpdateRange(toDeselect)
                    ));
                }

                var rem = _selectedSolids.Where(s => toDeselect.Contains(s.Real));
                _selectedSolids.ExceptWith(rem);
                _selectedSolids.UnionWith(toSelect.Select(s => new VertexSolid(s)));
                if (RenderDebug.Enabled) RenderDebug.Write("selection", $"update: added {toSelect.Count}, removed {toDeselect.Count}");

                // Queued while the lock is held, so concurrent updates reach the map in the order they changed the selection
                if (!tran.IsEmpty) pending = MapDocumentOperation.Perform(document, tran);
            }

            if (pending != null) await pending;
        }

        public async Task Commit(MapDocument document)
        {
            var tran = new Transaction();
            Task pending = null;

            lock (_lock)
            {
                foreach (var solid in _selectedSolids.Where(x => x.IsDirty))
                {
                    // An edit that left NaN / infinity in a solid would wreck it (and every copy of it): drop that edit instead
                    if (solid.Copy.Faces.Any(f => f.Vertices.Any(v => !IsFinite(v.Position))))
                    {
                        solid.Reset();
                        continue;
                    }

                    tran.Add(new RemoveMapObjectData(solid.Real.ID, solid.Real.Faces));
                    //TODO:should validate solid with polyhedron??
                    tran.Add(new AddMapObjectData(solid.Real.ID, solid.Copy.Faces.Select(x => x.ToFace(document.Map.NumberGenerator))));
                    solid.Reset();
                }

                // Queued while the lock is held, so the write-back is ordered with any selection change that follows it
                if (!tran.IsEmpty) pending = MapDocumentOperation.Perform(document, tran);
            }

            if (pending != null) await pending;
        }

        private static bool IsFinite(System.Numerics.Vector3 v)
        {
            return !(float.IsNaN(v.X) || float.IsNaN(v.Y) || float.IsNaN(v.Z) || float.IsInfinity(v.X) || float.IsInfinity(v.Y) || float.IsInfinity(v.Z));
        }

        /// <summary>
        /// Refreshes the working copy of every unedited selected solid that the map has changed underneath us
        /// (undo, redo, or an edit made outside the vertex tool). Returns true if any copy was refreshed.
        /// </summary>
        public bool ResetUpdated(IEnumerable<IMapObject> updated)
        {
            var set = new HashSet<IMapObject>(updated);
            if (set.Count == 0) return false;

            var any = false;
            lock (_lock)
            {
                foreach (var solid in _selectedSolids.Where(x => !x.IsDirty && set.Contains(x.Real)).ToList())
                {
                    // Our own selection writes (the VertexHidden marker) also raise a change, but leave the geometry alone.
                    // Only refresh the working copy when the solid's geometry really changed underneath it.
                    if (GeometrySignature.Of(solid.Real) == solid.SourceSignature) continue;

                    if (RenderDebug.Enabled) RenderDebug.Write("selection", $"reset copy of solid {solid.Real.ID} (map changed)");
                    solid.Reset();
                    any = true;
                }
            }
            return any;
        }

        public async Task Reset(MapDocument document)
        {
            lock (_lock)
            {
                foreach (var ss in _selectedSolids)
                {
                    ss.Reset();
                }
            }
        }
        public IEnumerable<VertexSolid> GetSolids() => _selectedSolids;

        public IEnumerator<VertexSolid> GetEnumerator()
        {
            lock (_lock)
            {
                return _selectedSolids.GetEnumerator();
            }
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }
}
