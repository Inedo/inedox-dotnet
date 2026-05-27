using Inedo.Extensibility;
using Inedo.Web;

namespace Inedo.Extensions.DotNet.SuggestionProviders;

internal sealed class BuildConfigurationSuggestionProvider : ISuggestionProvider
{
    public IAsyncEnumerable<string> GetSuggestionsAsync(IComponentConfiguration config, CancellationToken cancellationToken)
    {
        IEnumerable<string> values = ["Release", "Debug"];
        return values.ToAsyncEnumerable();
    }
}
