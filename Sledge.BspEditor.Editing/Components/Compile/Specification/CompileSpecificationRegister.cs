using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Linq;
using System.Threading.Tasks;
using Sledge.Common.Shell.Hooks;

namespace Sledge.BspEditor.Editing.Components.Compile.Specification
{
    [Export]
    [Export(typeof(IInitialiseHook))]
    public class CompileSpecificationRegister : IInitialiseHook
    {
        #pragma warning disable CS0649 // assigned by MEF
        [ImportMany] private IEnumerable<Lazy<ICompileSpecificationProvider>> _specProviders;
        #pragma warning restore CS0649

        public async Task OnInitialise()
        {
            _providers = new List<CompileSpecification>();
            foreach (var sp in _specProviders)
            {
                _providers.AddRange(await sp.Value.GetSpecifications());
            }
        }

        private List<CompileSpecification> _providers;

        public CompileSpecificationRegister()
        {
            _providers = new List<CompileSpecification>();
        }

        public IEnumerable<CompileSpecification> GetCompileSpecificationsForEngine(string engine)
        {
            return _providers.Where(x => x.Engine == engine);
        }
    }
}