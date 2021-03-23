using System.ComponentModel;
using Inedo.Documentation;
using Inedo.Extensibility;

namespace Inedo.Extensions.DotNet.Operations.DotNet
{
    [DisplayName("dotnet build")]
    [ScriptAlias("Build")]
    [ScriptNamespace("DotNet")]
    [Description("Builds a .NET Core/Framework/Standard project using dotnet build.")]
    [Example(@"# Build ~\src\MyProject.csproj with Release configuration, restoring NuGet packages from the InternalNuGet source
DotNet::Build ~\src\MyProject.csproj
(
    Configuration: Release,
    PackageSource: InternalNuGet
);")]
    [SeeAlso(typeof(DotNetPublishOperation))]
    public sealed class DotNetBuildOperation : DotNetOperation
    {
        protected override string CommandName => "build";
    }
}
