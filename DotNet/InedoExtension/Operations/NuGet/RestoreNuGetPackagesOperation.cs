using System.ComponentModel;
using System.Text;
using System.Threading.Tasks;
using Inedo.Diagnostics;
using Inedo.Documentation;
using Inedo.Extensibility;
using Inedo.Extensibility.Operations;
using Inedo.Extensibility.SecureResources;
using Inedo.Extensions.PackageSources;
using Inedo.Extensions.SecureResources;
using Inedo.Web;

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
        [ScriptAlias("SourceName", Obsolete = true)]
        [DisplayName("Package source")]
        [Description("If specified, this NuGet package source will be used to restore packages.")]
        [SuggestableValue(typeof(NuGetPackageSourceSuggestionProvider))]
        public string PackageSource { get; set; }

        public override async Task ExecuteAsync(IOperationExecutionContext context)
        {
            if (!string.IsNullOrEmpty(this.PackageSource))
            {
                var sourceId = new PackageSourceId(this.PackageSource);
                if (sourceId.Format != PackageSourceIdFormat.Url)
                {
                    this.LogDebug($"Resolving package source \"{this.PackageSource}\"...");
                    var source = await AhPackages.GetPackageSourceAsync(sourceId, context, context.CancellationToken);
                    if (source == null)
                    {
                        this.LogError($"Package source \"{this.PackageSource}\" not found.");
                        return;
                    }
                    if (source is not INuGetPackageSource nuuget)
                    {
                        this.LogError($"Package source \"{this.PackageSource}\" is a {source.GetType().Name} source; it must be a NuGet source for use with this operation.");
                        return;
                    }

                    this.PackageSource = nuuget.SourceUrl;
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

                if (!string.IsNullOrWhiteSpace(this.PackageSource))
                    buffer.Append($" -Source \"{this.PackageSource}\"");
            }
            else
            {
                buffer.Append("restore");
                if (!string.IsNullOrEmpty(target))
                    buffer.Append($" \"{TrimDirectorySeparator(target)}\"");

                if (!string.IsNullOrEmpty(this.PackagesDirectory))
                    buffer.Append($" --packages \"{TrimDirectorySeparator(context.ResolvePath(this.PackagesDirectory))}\"");

                if (!string.IsNullOrWhiteSpace(this.PackageSource))
                    buffer.Append($" --source \"{this.PackageSource}\"");
            }

            var exitCode = await this.ExecuteNuGetAsync(context, nugetInfo, buffer.ToString(), null); 
            if (exitCode != 0)
                this.LogError($"NuGet exited with code {exitCode}");
            
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
