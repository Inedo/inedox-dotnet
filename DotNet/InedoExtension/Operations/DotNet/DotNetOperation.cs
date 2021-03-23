using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
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
    [Tag(".net")]
    [DefaultProperty(nameof(ProjectPath))]
    [Note("This operation requires .NET Core build tools v2.0+ to be installed on the server.")]
    [SeeAlso(typeof(SetProjectVersionOperation))]
    public abstract class DotNetOperation : ExecuteOperation
    {
        private static readonly LazyRegex WarningRegex = new LazyRegex(@"\bwarning\b", RegexOptions.Compiled);

        protected DotNetOperation()
        {
        }

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

        [Category("Advanced")]
        [ScriptAlias("Framework")]
        [SuggestableValue(typeof(TargetFrameworkSuggestionProvider))]
        [Description("For building multiple target frameworks at once, leave this field blank and also leave \"Output\" blank.")]
        public string Framework { get; set; }
        [Category("Advanced")]
        [ScriptAlias("Runtime")]
        [SuggestableValue(typeof(RuntimeSuggestionProvider))]
        public string Runtime { get; set; }
        [Category("Advanced")]
        [ScriptAlias("Output")]
        [Description("Specifies an output directory for the build. This is only valid if \"Framework\" is also specified.")]
        public string Output { get; set; }
        [Category("Advanced")]
        [ScriptAlias("ForceDependencyResolution")]
        [DisplayName("Force dependency resolution")]
        [PlaceholderText("false")]
        public bool Force { get; set; }
        [Category("Advanced")]
        [ScriptAlias("Verbosity")]
        [DefaultValue(DotNetVerbosityLevel.Minimal)]
        public DotNetVerbosityLevel Verbosity { get; set; } = DotNetVerbosityLevel.Minimal;

        [Category("Advanced")]
        [ScriptAlias("AdditionalArguments")]
        [DisplayName("Additional arguments")]
        public string AdditionalArguments { get; set; }

        [Category("Advanced")]
        [ScriptAlias("DotNetExePath")]
        [DefaultValue("$DotNetExePath")]
        [DisplayName("dotnet.exe path")]
        [Description("Full path of dotnet.exe. This is usually C:\\Program Files\\dotnet\\dotnet.exe. If no value is supplied, the operation will default to %PROGRAMFILES%\\dotnet\\dotnet.exe.")]
        public string DotNetExePath { get; set; }

        protected abstract string CommandName { get; }

        public override async Task ExecuteAsync(IOperationExecutionContext context)
        {
            var dotNetPath = await this.GetDotNetExePath(context);
            if (string.IsNullOrEmpty(dotNetPath))
                return;

            var projectPath = context.ResolvePath(this.ProjectPath);

            var args = new StringBuilder($"{this.CommandName} ");
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

            if (!string.IsNullOrWhiteSpace(this.Runtime))
            {
                args.Append("--runtime ");
                args.AppendArgument(this.Runtime);
            }

            if (this.Force)
                args.Append("--force ");

            if (!string.IsNullOrWhiteSpace(this.Output))
            {
                if (string.IsNullOrWhiteSpace(this.Framework))
                    this.LogWarning("\"Output\" is specified; set the \"Framework\" value also to prevent unexpected results.");

                args.Append("--output ");
                args.AppendArgument(context.ResolvePath(this.Output));
            }

            if (this.Verbosity != DotNetVerbosityLevel.Minimal)
            {
                args.Append("--verbosity ");
                args.AppendArgument(this.Verbosity.ToString().ToLowerInvariant());
            }

            if (!string.IsNullOrWhiteSpace(this.PackageSource))
            {
                var source = SDK.GetPackageSources()
                    .FirstOrDefault(s => string.Equals(s.Name, this.PackageSource, StringComparison.OrdinalIgnoreCase));

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

            this.AppendAdditionalArguments(args);

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

        protected virtual void AppendAdditionalArguments(StringBuilder args)
        {
        }

        protected override void LogProcessOutput(string text) => this.Log(WarningRegex.IsMatch(text) ? MessageLevel.Warning : MessageLevel.Debug, text);

        protected override ExtendedRichDescription GetDescription(IOperationConfiguration config)
        {
            var extended = new RichDescription();
            var framework = config[nameof(Framework)];
            var output = config[nameof(Output)];

            if (!string.IsNullOrWhiteSpace(framework))
            {
                extended.AppendContent("Framework: ", new Hilite(framework));
                if (!string.IsNullOrWhiteSpace(output))
                    extended.AppendContent(", ");
            }

            if (!string.IsNullOrWhiteSpace(output))
                extended.AppendContent("Output: ", new DirectoryHilite(output));

            return new ExtendedRichDescription(
                new RichDescription(
                    $"dotnet {this.CommandName} ",
                    new DirectoryHilite(config[nameof(ProjectPath)])
                ),
                extended
            );
        }

        private async Task<string> GetDotNetExePath(IOperationExecutionContext context)
        {
            if (!string.IsNullOrWhiteSpace(this.DotNetExePath))
            {
                this.LogDebug("dotnet path: " + this.DotNetExePath);
                return this.DotNetExePath;
            }

            var remote = await context.Agent.TryGetServiceAsync<IRemoteMethodExecuter>();
            if (remote != null)
            {
                var path = await remote.InvokeFuncAsync(GetDotNetExePathRemote);
                if (!string.IsNullOrEmpty(path))
                {
                    this.LogDebug("dotnet path: " + path);
                    return path;
                }
            }

            this.LogError("Could not determine the location of dotnet.exe on this server. To resolve this error, ensure that dotnet.exe is available on this server and retry the build, or create a server-scoped variabled named $DotNetExePath set to the location of dotnet.exe.");
            return null;
        }

        private static string GetDotNetExePathRemote()
        {
            var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "dotnet", "dotnet.exe");
            if (File.Exists(path))
                return path;
            else
                return null;
        }
    }
}
