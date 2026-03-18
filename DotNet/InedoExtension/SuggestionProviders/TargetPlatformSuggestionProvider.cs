using Inedo.Extensibility;
using Inedo.Web;

namespace Inedo.Extensions.DotNet.SuggestionProviders;

internal sealed class TargetPlatformSuggestionProvider : ISuggestionProvider
{
    public IAsyncEnumerable<string> GetSuggestionsAsync(IComponentConfiguration config, CancellationToken cancellationToken)
    {
        IEnumerable<string> values = ["AnyCPU", "x86", "x64", "Win32"];
        return values.ToAsyncEnumerable();
    }
}
