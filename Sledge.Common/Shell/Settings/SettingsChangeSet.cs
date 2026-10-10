using System;
using System.Collections.Generic;
using System.Linq;

namespace Sledge.Common.Shell.Settings
{
    /// <summary>
    /// Describes which settings containers actually changed when settings were applied.
    /// Published as the payload of the "SettingPreChanged" and "SettingsChanged" messages
    /// so that subscribers can rebuild only the parts they actually own.
    /// </summary>
    public class SettingsChangeSet
    {
        /// <summary>
        /// Used when a publisher doesn't describe what changed. Every query returns true,
        /// so subscribers fall back to rebuilding everything (the old behaviour).
        /// </summary>
        public static readonly SettingsChangeSet Unknown = new SettingsChangeSet(null);

        private readonly HashSet<string> _changed;

        /// <summary>
        /// Create a change set for the given containers.
        /// </summary>
        /// <param name="changedContainers">The names of the containers that changed,
        /// an empty collection when nothing changed, or null when the change is unknown</param>
        public SettingsChangeSet(IEnumerable<string> changedContainers)
        {
            _changed = changedContainers == null ? null : new HashSet<string>(changedContainers, StringComparer.Ordinal);
        }

        /// <summary>
        /// True when the publisher didn't describe what changed.
        /// </summary>
        public bool IsUnknown => _changed == null;

        /// <summary>
        /// The containers that are known to have changed. Empty when nothing changed,
        /// and empty when the change is unknown (use <see cref="IsUnknown"/> to tell those apart).
        /// </summary>
        public IEnumerable<string> ChangedContainers => _changed ?? Enumerable.Empty<string>();

        /// <summary>
        /// True when the given container may have changed and therefore needs to be re-applied.
        /// </summary>
        public bool MayHaveChanged(string containerName)
        {
            return _changed == null || _changed.Contains(containerName);
        }

        /// <summary>
        /// True when any of the given containers may have changed.
        /// </summary>
        public bool MayHaveChanged(params string[] containerNames)
        {
            if (_changed == null) return true;
            return containerNames.Any(_changed.Contains);
        }

        /// <summary>
        /// Interpret a message payload as a change set. Anything that isn't a change set
        /// (including the plain objects published by older callers) means "unknown".
        /// </summary>
        public static SettingsChangeSet FromPayload(object payload)
        {
            return payload as SettingsChangeSet ?? Unknown;
        }
    }
}