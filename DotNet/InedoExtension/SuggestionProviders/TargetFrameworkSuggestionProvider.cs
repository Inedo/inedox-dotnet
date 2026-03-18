using Inedo.Extensibility;
using Inedo.Web;

namespace Inedo.Extensions.DotNet.SuggestionProviders;

internal sealed class TargetFrameworkSuggestionProvider : ISuggestionProvider
{
    #region Frameworks listed at https://docs.microsoft.com/en-us/dotnet/standard/frameworks
    private static readonly string[] Frameworks =
    [
        "netstandard2.0",
        "net10.0",
        "net9.0",
        "net8.0",
        "net7.0",
        "net6.0",
        "net5.0",
        "net461",
        "net462",
        "net47",
        "net471",
        "net472",
        "net48",
        "net481",
        "netcore"
    ];
    #endregion

    public IAsyncEnumerable<string> GetSuggestionsAsync(IComponentConfiguration config, CancellationToken cancellationToken) => Frameworks.ToAsyncEnumerable();
}
