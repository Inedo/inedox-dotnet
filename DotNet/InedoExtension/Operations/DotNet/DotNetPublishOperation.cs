using System.ComponentModel;
using System.Text;
using Inedo.Documentation;
using Inedo.Extensibility;

namespace Inedo.Extensions.DotNet.Operations.DotNet
{
    [DisplayName("dotnet publish")]
    [ScriptAlias("Publish")]
    [ScriptNamespace("DotNet")]
    [Description("Publishes a .NET Core/Framework/Standard project using dotnet publish.")]
    [Example(@"# Publish ~\src\MyProject.csproj with Release configuration for .net core 3.1, restoring NuGet packages from the InternalNuGet source
DotNet::Publish ~\src\MyProject.csproj
(
    Configuration: Release,
    Framework: netcoreapp3.1,
    Runtime: win-x64,
    PackageSource: InternalNuGet
);")]
    [SeeAlso(typeof(DotNetBuildOperation))]
    public sealed class DotNetPublishOperation : DotNetBuildOrPublishOperation
    {
        [ScriptAlias("SelfContained")]
        [DisplayName("Self-contained")]
        [Category("Advanced")]
        public bool SelfContained { get; set; }

        protected override string CommandName => "publish";

        protected override void AppendAdditionalArguments(StringBuilder args)
        {
            args.AppendArgument(this.SelfContained ? "--self-contained" : "--no-self-contained");
        }
    }
}
