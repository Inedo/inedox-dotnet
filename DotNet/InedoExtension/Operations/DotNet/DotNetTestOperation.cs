using System;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Inedo.Agents;
using Inedo.Diagnostics;
using Inedo.Documentation;
using Inedo.Extensibility;
using Inedo.Extensibility.Operations;
using Inedo.Extensions.DotNet.SuggestionProviders;
using Inedo.Web;

namespace Inedo.Extensions.DotNet.Operations.DotNet
{
    [ScriptAlias("Test")]
    [DisplayName("dotnet test")]
    [Description("Runs unit tests on a specified test project using the dotnet test command.")]
    public sealed class DotNetTestOperation : DotNetOperation
    {
        [Required]
        [ScriptAlias("Project")]
        [DisplayName("Project path")]
        [Description("This must be the path to either a project file, solution file, or a directory containing a project or solution file.")]
        public string ProjectPath { get; set; }

        [ScriptAlias("Configuration")]
        [SuggestableValue(typeof(BuildConfigurationSuggestionProvider))]
        public string Configuration { get; set; }
        [ScriptAlias("PackageSource")]
        [DisplayName("Package source")]
        [SuggestableValue(typeof(NuGetPackageSourceSuggestionProvider))]
        [Description("If specified, this NuGet package source will be used to restore packages when building.")]
        public string PackageSource { get; set; }

        [ScriptAlias("Group")]
        [DisplayName("Test group")]
        [PlaceholderText("Unit Tests")]
        public string TestGroup { get; set; }

        [Category("Advanced")]
        [ScriptAlias("Framework")]
        [SuggestableValue(typeof(TargetFrameworkSuggestionProvider))]
        public string Framework { get; set; }

        public override async Task ExecuteAsync(IOperationExecutionContext context)
        {
            var dotNetPath = await this.GetDotNetExePath(context);
            if (string.IsNullOrEmpty(dotNetPath))
                return;

            var fileOps = await context.Agent.GetServiceAsync<IFileOperationsExecuter>();
            var trxFileName = fileOps.CombinePath(await fileOps.GetBaseWorkingDirectoryAsync(), "Temp", $"{Guid.NewGuid():N}.trx");

            var projectPath = context.ResolvePath(this.ProjectPath);

            var args = new StringBuilder("test ");
            args.AppendArgument(projectPath);

            if (!string.IsNullOrWhiteSpace(this.Configuration))
            {
                args.Append("--configuration ");
                args.AppendArgument(this.Configuration);
            }

            if (!string.IsNullOrWhiteSpace(this.Framework))
            {
                args.Append("--framework ");
                args.AppendArgument(this.Framework);
            }

            args.Append("--logger ");
            args.AppendArgument($"trx;LogFileName={trxFileName}");

            if (!string.IsNullOrWhiteSpace(this.PackageSource))
            {
                var source = SDK.GetPackageSources()
                    .FirstOrDefault(s => string.Equals(s.ResourceInfo.Name, this.PackageSource, StringComparison.OrdinalIgnoreCase));

                if (source == null)
                {
                    this.LogError($"Package source \"{this.PackageSource}\" not found.");
                    return;
                }

                if (source.PackageType != AttachedPackageType.NuGet)
                {
                    this.LogError($"Package source \"{this.PackageSource}\" is a {source.PackageType} source; it must be a NuGet source for use with this operation.");
                    return;
                }

                args.Append("--source ");
                args.AppendArgument(source.FeedUrl);
            }

            if (!string.IsNullOrWhiteSpace(this.AdditionalArguments))
                args.Append(this.AdditionalArguments);

            this.LogDebug($"Ensuring working directory {context.WorkingDirectory} exists...");
            await fileOps.CreateDirectoryAsync(context.WorkingDirectory);

            var fullArgs = args.ToString();
            this.LogDebug($"Executing dotnet {fullArgs}...");

            int res = await this.ExecuteCommandLineAsync(
                context,
                new RemoteProcessStartInfo
                {
                    FileName = dotNetPath,
                    Arguments = fullArgs,
                    WorkingDirectory = context.WorkingDirectory
                }
            );

            this.Log(res == 0 ? MessageLevel.Debug : MessageLevel.Error, "dotnet exit code: " + res);
        }

        protected override ExtendedRichDescription GetDescription(IOperationConfiguration config)
        {
            var extended = new RichDescription();
            var framework = config[nameof(Framework)];

            if (!string.IsNullOrWhiteSpace(framework))
                extended.AppendContent("Framework: ", new Hilite(framework));

            return new ExtendedRichDescription(
                new RichDescription(
                    $"dotnet test ",
                    new DirectoryHilite(config[nameof(ProjectPath)])
                ),
                extended
            );
        }
    }
}
