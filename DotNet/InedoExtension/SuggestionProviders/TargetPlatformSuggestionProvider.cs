using Inedo.Extensibility;
using Inedo.Web;

namespace Inedo.Extensions.DotNet.SuggestionProviders
{
    public sealed class TargetPlatformSuggestionProvider : ISuggestionProvider
    {
        public Task<IEnumerable<string>> GetSuggestionsAsync(IComponentConfiguration config)
        {
            var values = (IEnumerable<string>)["AnyCPU", "x86", "x64", "Win32"];
            return Task.FromResult(values);
        }
    }
}
