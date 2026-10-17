using System;
using System.Collections.Generic;
using System.ComponentModel.Composition.Hosting;
using System.ComponentModel.Composition.Primitives;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
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
            var noMef = 0;
            foreach (var dll in Directory.GetFiles(dir, "*.dll").Union(Directory.GetFiles(dir, "*.exe")))
            {
                var start = StartupLog.Now;

                // Read the assembly's metadata without loading it. Only managed assemblies that reference MEF
                // can contain exports, so everything else (native DLLs, third party libraries) is skipped.
                // Loading and reflecting over every library in the folder was the biggest cost at start up.
                var kind = Inspect(dll);
                if (kind == AssemblyKind.NotManaged)
                {
                    skipped++;
                    continue;
                }
                if (kind == AssemblyKind.NoMef)
                {
                    noMef++;
                    continue;
                }

                var catalog = new AssemblyCatalog(dll);

                // Force the part discovery (reflection over every type) now so it is timed per assembly.
                // MEF would do the same work later anyway, so this doesn't add to the total.
                var count = catalog.Parts.Count();
                parts.Add(catalog);

                StartupLog.Done("catalog: " + Path.GetFileName(dll) + " (" + count + " parts)", start);
            }

            StartupLog.Done("catalog: scanned " + parts.Count + " assemblies that reference MEF, skipped " + noMef + " managed without MEF and " + skipped + " native/invalid", total);
            return new AggregateCatalog(parts);
        }

        private enum AssemblyKind
        {
            NotManaged,
            NoMef,
            ReferencesMef
        }

        private static AssemblyKind Inspect(string path)
        {
            try
            {
                using (var stream = File.OpenRead(path))
                using (var pe = new PEReader(stream))
                {
                    if (!pe.HasMetadata) return AssemblyKind.NotManaged;

                    var reader = pe.GetMetadataReader();
                    if (!reader.IsAssembly) return AssemblyKind.NotManaged;

                    foreach (var handle in reader.AssemblyReferences)
                    {
                        var name = reader.GetString(reader.GetAssemblyReference(handle).Name);
                        if (name == "System.ComponentModel.Composition") return AssemblyKind.ReferencesMef;
                    }
                    return AssemblyKind.NoMef;
                }
            }
            catch (BadImageFormatException)
            {
                return AssemblyKind.NotManaged;
            }
            catch
            {
                // Couldn't read it for some other reason: behave as before and let MEF try it
                return AssemblyKind.ReferencesMef;
            }
        }
        
        public override IEnumerator<ComposablePartDefinition> GetEnumerator()
        {
            return _innerCatalog.Value.GetEnumerator();
        }
    }
}