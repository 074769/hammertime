namespace Sledge.Rendering.Resources
{
    /// <summary>
    /// Which selection state a draw belongs to. Objects that can be selected without rebuilding their
    /// geometry are converted twice, once as they look unselected and once as they look selected, and each
    /// copy is only drawn while the object is in the matching state.
    /// </summary>
    public enum SelectionFilter
    {
        /// <summary>Drawn regardless of selection.</summary>
        Always = 0,

        /// <summary>Drawn only while the object is selected.</summary>
        WhenSelected = 1,

        /// <summary>Drawn only while the object is not selected.</summary>
        WhenUnselected = 2
    }
}
