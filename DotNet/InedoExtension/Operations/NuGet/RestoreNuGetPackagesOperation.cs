using System.ComponentModel;
using System.Text;
using System.Threading.Tasks;
using Inedo.Diagnostics;
using Inedo.Documentation;
using Inedo.Extensibility;
using Inedo.Extensibility.Operations;
using Inedo.Extensibility.SecureResources;
using Inedo.Extensions.SecureResources;

namespace Inedo.Extensions.DotNet.Operations.NuGet
{
    [ScriptNamespace("NuGet")]
    [ScriptAlias("Restore-Packages")]
    [DisplayName("Restore NuGet Packages")]
    [Description("Restores all packages in a specified solution, project, or packages.config file.")]
    public sealed class RestoreNuGetPackagesOperation : NuGetOperation
    {
        [ScriptAlias("Target")]
        [DisplayName("Target")]
        [Description("The target solution, project, or packages.config file used to restore packages, or directory containing a solution.")]
        [PlaceholderText("$WorkingDirectory")]
        public string Target { get; set; }
        [Category("Advanced")]
        [ScriptAlias("PackagesDirectory")]
        [DisplayName("Packages directory")]
        [PlaceholderText("default")]
        public string PackagesDirectory { get; set; }
        [ScriptAlias("Source")]
        [DisplayName("Source URL")]
        [PlaceholderText("Use default URL specified in nuget.config")]
        public string ServerUrl { get; set; }
        [Category("Advanced")]
        [ScriptAlias("SourceName")]
        [DisplayName("Package source")]
        public string PackageSource { get; set; }

        public override async Task ExecuteAsync(IOperationExecutionContext context)
        {
            if (!string.IsNullOrEmpty(this.PackageSource))
            {
                if (!string.IsNullOrEmpty(this.ServerUrl))
                {
                    this.LogWarning("SourceName will be ignored because Source (url) is specified.");
                }
                else
                {
                    this.LogDebug($"Using package source {this.PackageSource}.");
                    var packageSource = (NuGetPackageSource)SecureResource.Create(this.PackageSource, (IResourceResolutionContext)context);
                    this.ServerUrl = packageSource.ApiEndpointUrl;
                }
            }

            var nugetInfo = await this.GetNuGetInfoAsync(context).ConfigureAwait(false);

            var target = context.ResolvePath(this.Target);
            var buffer = new StringBuilder();

            this.LogInformation($"Restoring packages for {target}...");
            if (nugetInfo.IsNuGetExe)
            {
                buffer.Append("restore");
                if (!string.IsNullOrEmpty(target))
                    buffer.Append($" \"{TrimDirectorySeparator(target)}\"");

                if (!string.IsNullOrEmpty(this.PackagesDirectory))
                    buffer.Append($" -PackagesDirectory \"{TrimDirectorySeparator(context.ResolvePath(this.PackagesDirectory))}\"");

                if (!string.IsNullOrWhiteSpace(this.ServerUrl))
                    buffer.Append($" -Source \"{this.ServerUrl}\"");
            }
            else
            {
                buffer.Append("restore");
                if (!string.IsNullOrEmpty(target))
                    buffer.Append($" \"{TrimDirectorySeparator(target)}\"");

                if (!string.IsNullOrEmpty(this.PackagesDirectory))
                    buffer.Append($" --packages \"{TrimDirectorySeparator(context.ResolvePath(this.PackagesDirectory))}\"");

                if (!string.IsNullOrWhiteSpace(this.ServerUrl))
                    buffer.Append($" --source \"{this.ServerUrl}\"");
            }

            await this.ExecuteNuGetAsync(context, nugetInfo, buffer.ToString(), null);
            this.LogInformation("Done restoring packages.");
        }

        protected override ExtendedRichDescription GetDescription(IOperationConfiguration config)
        {
            return new ExtendedRichDescription(
                new RichDescription(
                    "Restore NuGet packages for ",
                    new DirectoryHilite(config[nameof(this.Target)])
                )
            );
        }
    }
}
