using System.Collections.Generic;
using System.Numerics;
using Sledge.DataStructures.Geometric;

namespace Sledge.BspEditor.Editing.CompileErrors
{
    public enum CompileErrorsSeverity { Warning, Error }

    /// <summary>One message from a compile log, after parsing.</summary>
    public class CompileErrorsEntry
    {
        public string Tool { get; set; }
        public CompileErrorsSeverity Severity { get; set; }
        public int LineNumber { get; set; }
        public string Text { get; set; }

        /// <summary>Stable key of the message type, e.g. "AmbiguousLeafnode".</summary>
        public string TypeKey { get; set; }
        public string TypeName { get; set; }
        public string Category { get; set; }

        /// <summary>Key of the show/hide checkbox this entry belongs to.</summary>
        public string FilterKey { get; set; }
        public string FilterLabel { get; set; }

        public int? Hull { get; set; }

        /// <summary>Compile-order entity index (0 = worldspawn), as printed by the compiler.</summary>
        public int? Entity { get; set; }

        /// <summary>Compile-order brush index inside the entity.</summary>
        public int? Brush { get; set; }

        public string EntityClassname { get; set; }
        public string EntityTargetname { get; set; }

        public Box Box { get; set; }
        public List<Vector3> Points { get; } = new List<Vector3>();
    }
}
