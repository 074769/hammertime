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
        /// The names of texture packages the user has chosen to unload (unchecked) for the
        /// map currently open in this environment. This is the user's selection: it only
        /// takes effect on the loaded textures after <see cref="RefreshTexturePackages"/>.
        /// </summary>
        IEnumerable<string> ManuallyDisabledTexturePackages { get; }

        /// <summary>
        /// The texture packages the environment settings leave unticked (not loaded by default).
        /// A new map starts with exactly these unticked and everything else ticked.
        /// </summary>
        IEnumerable<string> EnvironmentDisabledTexturePackages { get; }

        /// <summary>
        /// Records which texture packages the user wants unloaded WITHOUT reloading anything.
        /// The choice is applied the next time <see cref="RefreshTexturePackages"/> is called.
        /// </summary>
        /// <param name="packageNames">The names of the packages to unload</param>
        void SetPendingDisabledTexturePackages(IEnumerable<string> packageNames);

        /// <summary>
        /// Sets which texture packages should be manually unloaded for the current map and
        /// applies the change immediately (rebuilds the texture collection).
        /// </summary>
        /// <param name="packageNames">The names of the packages to unload</param>
        void SetManuallyDisabledTexturePackages(IEnumerable<string> packageNames);

        /// <summary>
        /// Forces texture packages to be re-read from disk, discarding any cached texture
        /// metadata. Used so that a WAD file edited on disk (outside the editor) can be picked
        /// up without restarting the editor. Also applies any pending package selection made with
        /// <see cref="SetPendingDisabledTexturePackages"/>.
        /// </summary>
        void RefreshTexturePackages();
    }
}
