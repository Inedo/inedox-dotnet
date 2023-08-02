using System.Collections.Immutable;
using Inedo.Web;

namespace Inedo.Extensions.DotNet.SuggestionProviders;

internal sealed class DotNetIBSSuggestionProvider : ImageBasedServiceSuggestionProvider
{
    protected override ImmutableArray<string> RequiredCapabilities => ImmutableArray.Create("dotnet");
}
