using Inedo.Extensibility;
using Inedo.Web;

namespace Inedo.Extensions.DotNet.SuggestionProviders
{
    public sealed class BuildConfigurationSuggestionProvider : ISuggestionProvider
    {
        public Task<IEnumerable<string>> GetSuggestionsAsync(IComponentConfiguration config)
        {
            var values = (IEnumerable<string>)["Release", "Debug"];
            return Task.FromResult(values);
        }
    }
}
