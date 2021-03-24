using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Inedo.Extensibility;
using Inedo.Extensibility.Operations;
using Inedo.Web;

namespace Inedo.Extensions.DotNet.SuggestionProviders
{
    internal sealed class NuGetPackageSourceSuggestionProvider : ISuggestionProvider
    {
        public Task<IEnumerable<string>> GetSuggestionsAsync(IComponentConfiguration config)
        {
            var result = SDK.GetPackageSources()
                .Where(s => s.PackageType == AttachedPackageType.NuGet)
                .OrderBy(s => s.ResourceInfo.Name)
                .Select(s => s.ResourceInfo.Name);

            return Task.FromResult(result);
        }
    }
}
