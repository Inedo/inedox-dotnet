using System.Collections.Generic;
using System.Threading.Tasks;
using Inedo.Extensibility;
using Inedo.Web;

namespace Inedo.Extensions.DotNet.SuggestionProviders
{
    internal sealed class TargetFrameworkSuggestionProvider : ISuggestionProvider
    {
        #region Frameworks listed at https://docs.microsoft.com/en-us/dotnet/standard/frameworks
        private readonly static string[] Frameworks = new[]
        {
            "netstandard1.0",
            "netstandard1.1",
            "netstandard1.2",
            "netstandard1.3",
            "netstandard1.4",
            "netstandard1.5",
            "netstandard1.6",
            "netstandard2.0",
            "netcoreapp1.0",
            "netcoreapp1.1",
            "netcoreapp2.0",
            "netcoreapp2.1",
            "netcoreapp2.2",
            "netcoreapp3.0",
            "netcoreapp3.1",
            "net5.0",
            "net11",
            "net20",
            "net35",
            "net40",
            "net403",
            "net45",
            "net451",
            "net452",
            "net46",
            "net461",
            "net462",
            "net47",
            "net471",
            "net472",
            "net48",
            "netcore",
            "netcore45",
            "netcore451",
            "netmf",
            "sl4",
            "sl5",
            "wp",
            "wp7",
            "wp75",
            "wp8",
            "wp81",
            "wpa81",
            "uap",
            "uap10.0"
        };
        #endregion

        public Task<IEnumerable<string>> GetSuggestionsAsync(IComponentConfiguration config) => Task.FromResult<IEnumerable<string>>(Frameworks);
    }
}
