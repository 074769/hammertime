namespace Sledge.BspEditor.Modification.ChangeHandling
{
    /// <summary>
    /// Fast path for changes where nothing happened except objects being selected or deselected.
    /// Handlers whose result depends on object data, hierarchy or class (entity models and sprites, decals,
    /// beams, visgroup membership, link sync) can't be affected by selection and can return early.
    /// Handlers that do depend on selection must not use this.
    /// </summary>
    public static class SelectionOnlyFastPath
    {
        public static bool IsActive(Change change)
        {
            return change != null && change.IsSelectionOnly;
        }
    }
}
