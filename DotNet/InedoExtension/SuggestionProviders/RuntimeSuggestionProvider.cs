using System.Collections.Generic;
using System.Threading.Tasks;
using Inedo.Extensibility;
using Inedo.Web;

namespace Inedo.Extensions.DotNet.SuggestionProviders
{
    internal sealed class RuntimeSuggestionProvider : ISuggestionProvider
    {
        #region Common runtimes taken from https://docs.microsoft.com/en-us/dotnet/core/rid-catalog
        private readonly static string[] Runtimes = new[]
        {
            "win-x64",
            "win-x86",
            "win-arm",
            "win-arm64",
            "linux-x64",
            "linux-musl-x64",
            "linux-arm",
            "linux-arm64",
            "osx-x64"
        };
        #endregion

        public Task<IEnumerable<string>> GetSuggestionsAsync(IComponentConfiguration config) => Task.FromResult<IEnumerable<string>>(Runtimes);
    }
}
