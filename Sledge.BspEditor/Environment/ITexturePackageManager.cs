using System.Collections.Generic;

namespace Sledge.BspEditor.Environment
{
    /// <summary>
    /// Optional capability for environments that load texture packages individually from
    /// disk (e.g. GoldSource WAD files) and support the user manually unloading packages
    /// that aren't needed, plus reloading package contents from disk on demand.
    /// Environments that don't work this way (e.g. Source/VMF material system) simply don't
    /// implement this interface.
    /// </summary>
    public interface ITexturePackageManager
    {
        /// <summary>
        /// The names of every texture package found on disk for this environment, regardless
        /// of whether it is currently loaded or has been manually/globally excluded.
        /// </summary>
        /// <returns>All available texture package names</returns>
        IEnumerable<string> GetAllTexturePackageNames();

        /// <summary>
        /// The names of texture packages that have been manually unloaded (unchecked) for the
        /// map currently open in this environment.
        /// </summary>
        IEnumerable<string> ManuallyDisabledTexturePackages { get; }

        /// <summary>
        /// Sets which texture packages should be manually unloaded for the current map, and
        /// rebuilds the texture collection so the change takes effect immediately.
        /// </summary>
        /// <param name="packageNames">The names of the packages to unload</param>
        void SetManuallyDisabledTexturePackages(IEnumerable<string> packageNames);

        /// <summary>
        /// Forces texture packages to be re-read from disk, discarding any cached texture
        /// metadata. Used so that a WAD file edited on disk (outside the editor) can be picked
        /// up without restarting the editor. Does not affect which packages are enabled/disabled.
        /// </summary>
        void RefreshTexturePackages();
    }
}
