using System;
using System.Collections.Generic;
using System.ComponentModel.Composition.Hosting;
using System.ComponentModel.Composition.Primitives;
using System.IO;
using System.Linq;
using System.Reflection;
using Sledge.Common.Logging;

namespace Sledge.Common
{
    public class ValidAssembliesInDirectoryContainer : ComposablePartCatalog
    {
        private readonly string _directory;
        private readonly Lazy<AggregateCatalog> _innerCatalog;

        public ValidAssembliesInDirectoryContainer(string directory)
        {
            _directory = directory;
            _innerCatalog = new Lazy<AggregateCatalog>(CreateInnerCatalog);
        }

        private AggregateCatalog CreateInnerCatalog()
        {
            var parts = new List<ComposablePartCatalog>();

            var dir = _directory;
            var total = StartupLog.Now;
            var skipped = 0;
            foreach (var dll in Directory.GetFiles(dir, "*.dll").Union(Directory.GetFiles(dir, "*.exe")))
            {
                var start = StartupLog.Now;
                try
                {
                    // See if the assembly is managed
                    AssemblyName.GetAssemblyName(dll);
                }
                catch
                {
                    // Invalid assembly; Skip
                    skipped++;
                    continue;
                }

                var catalog = new AssemblyCatalog(dll);

                // Force the part discovery (reflection over every type) now so it is timed per assembly.
                // MEF would do the same work later anyway, so this doesn't add to the total.
                var count = catalog.Parts.Count();
                parts.Add(catalog);

                StartupLog.Done("catalog: " + Path.GetFileName(dll) + " (" + count + " parts)", start);
            }

            StartupLog.Done("catalog: scanned " + parts.Count + " managed assemblies, skipped " + skipped + " native/invalid", total);
            return new AggregateCatalog(parts);
        }
        
        public override IEnumerator<ComposablePartDefinition> GetEnumerator()
        {
            return _innerCatalog.Value.GetEnumerator();
        }
    }
}