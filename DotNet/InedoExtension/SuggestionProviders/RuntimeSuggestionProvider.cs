using Inedo.Extensibility;
using Inedo.Web;

namespace Inedo.Extensions.DotNet.SuggestionProviders;

internal sealed class RuntimeSuggestionProvider : ISuggestionProvider
{
    #region Common runtimes taken from https://docs.microsoft.com/en-us/dotnet/core/rid-catalog
    private static readonly string[] Runtimes =
    [
        "win-x64",
        "win-x86",
        "win-arm64",
        "linux-x64",
        "linux-musl-x64",
        "linux-arm",
        "linux-arm64",
        "linux-bionic-arm64",
        "linux-loongarch64",
        "osx-x64",
        "osx-arm64",
        "ios-arm64",
        "android-arm64",
        "android-arm",
        "android-x64",
        "android-x86"
    ];
    #endregion

    public IAsyncEnumerable<string> GetSuggestionsAsync(IComponentConfiguration config, CancellationToken cancellationToken) => Runtimes.ToAsyncEnumerable();
}
