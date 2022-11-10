using System.ComponentModel;
using System.Text;
using Inedo.Agents;
using Inedo.Diagnostics;
using Inedo.Documentation;
using Inedo.Extensibility;
using Inedo.Extensibility.Operations;
using Inedo.Extensions.DotNet.SuggestionProviders;
using Inedo.Web;

namespace Inedo.Extensions.DotNet.Operations.DotNet
{
    [Tag("nuget")]
    [DisplayName("dotnet pack")]
    [ScriptAlias("Pack")]
    [ScriptNamespace("DotNet")]
    [Description("Creates a NuGet package from a .net project using the dotnet pack command.")]
    [SeeAlso(typeof(NuGet.CreateNuGetPackageOperation), Comments = "The NuGet::Create-Package operation works only on Windows, but can also build a package from a .nuspec file.")]
    [Note("This operation works on Windows and Linux as long as dotnet is installed.")]
    public sealed class DotNetPackOperation : DotNetOperation
    {
        [Required]
        [ScriptAlias("Project")]
        [DisplayName("Project path")]
        [Description("This must be the path to either a project file, solution file, or a directory containing a project or solution file.")]
        public string ProjectPath { get; set; }

        [Category("Advanced")]
        [ScriptAlias("Output")]
        [Description("Specifies an output directory for the build. This is only valid if \"Framework\" is also specified.")]
        public string Output { get; set; }

        [ScriptAlias("Configuration")]
        [SuggestableValue(typeof(BuildConfigurationSuggestionProvider))]
        public string Configuration { get; set; }

        [ScriptAlias("IncludeSymbols")]
        [DisplayName("Include symbols")]
        public bool IncludeSymbols { get; set; }
        [ScriptAlias("IncludeSource")]
        [DisplayName("Include source")]
        public bool IncludeSource { get; set; }

        [Category("Advanced")]
        [ScriptAlias("ForceDependencyResolution")]
        [DisplayName("Force dependency resolution")]
        [PlaceholderText("false")]
        public bool Force { get; set; }
        [Category("Advanced")]
        [ScriptAlias("Verbosity")]
        [DefaultValue(DotNetVerbosityLevel.Minimal)]
        public DotNetVerbosityLevel Verbosity { get; set; } = DotNetVerbosityLevel.Minimal;

        public override async Task ExecuteAsync(IOperationExecutionContext context)
        {
            var dotNetPath = await this.GetDotNetExePath(context);
            if (string.IsNullOrEmpty(dotNetPath))
                return;

            var projectPath = context.ResolvePath(this.ProjectPath);

            var args = new StringBuilder("pack ");
            args.AppendArgument(projectPath);

            if (!string.IsNullOrWhiteSpace(this.Configuration))
            {
                args.Append("--configuration ");
                args.AppendArgument(this.Configuration);
            }

            if (this.Force)
                args.Append("--force ");

            if (!string.IsNullOrWhiteSpace(this.Output))
            {
                args.Append("--output ");
                args.AppendArgument(context.ResolvePath(this.Output));
            }

            if (this.Verbosity != DotNetVerbosityLevel.Minimal)
            {
                args.Append("--verbosity ");
                args.AppendArgument(this.Verbosity.ToString().ToLowerInvariant());
            }

            if (!string.IsNullOrWhiteSpace(this.AdditionalArguments))
                args.Append(this.AdditionalArguments);

            this.LogDebug($"Ensuring working directory {context.WorkingDirectory} exists...");
            var fileOps = await context.Agent.GetServiceAsync<IFileOperationsExecuter>();
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
            var output = config[nameof(Output)];

            if (!string.IsNullOrWhiteSpace(output))
                extended.AppendContent("Output: ", new DirectoryHilite(output));

            return new ExtendedRichDescription(
                new RichDescription(
                    "dotnet pack ",
                    new DirectoryHilite(config[nameof(ProjectPath)])
                ),
                extended
            );
        }
    }
}
