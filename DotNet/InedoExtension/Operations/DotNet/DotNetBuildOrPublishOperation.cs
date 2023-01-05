using System.ComponentModel;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Inedo.Agents;
using Inedo.Diagnostics;
using Inedo.Documentation;
using Inedo.Extensibility;
using Inedo.Extensibility.Operations;
using Inedo.Extensions.DotNet.SuggestionProviders;
using Inedo.Extensions.PackageSources;
using Inedo.IO;
using Inedo.Web;

namespace Inedo.Extensions.DotNet.Operations.DotNet
{
    [DefaultProperty(nameof(ProjectPath))]
    public abstract class DotNetBuildOrPublishOperation : DotNetOperation
    {
        protected DotNetBuildOrPublishOperation()
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

        [Category("Build Configuration")]
        [ScriptAlias("Version")]
        [PlaceholderText("not set")]
        public string Version { get; set; }
        [Category("Build Configuration")]
        [ScriptAlias("Framework")]
        [SuggestableValue(typeof(TargetFrameworkSuggestionProvider))]
        public string Framework { get; set; }
        [Category("Build Configuration")]
        [ScriptAlias("Runtime")]
        [SuggestableValue(typeof(RuntimeSuggestionProvider))]
        public string Runtime { get; set; }
        [Category("Build Configuration")]
        [ScriptAlias("Output")]
        [Description("Specifies an output directory for the build.")]
        public string Output { get; set; }
        [Category("Advanced")]
        [ScriptAlias("VSToolsPath")]
        [PlaceholderText("not set")]
        [Description(
            "Some older .NET applications (especially Framework) may require MSBuild targets included with Visual Studio. Use " +
            "\"embedded\" to try resolving these without Visual Studio, \"search\" to try to find the targets using the Registry, " +
            "or enter a path like " + @"C:\Program Files\Microsoft Visual Studio\2022\Enterprise\MSBuild\Microsoft\VisualStudio\v17.0")]
        [SuggestableValue("embedded", "search")]
        public string VSToolsPath { get; set; }
        [Category("Build Configuration")]
        [ScriptAlias("ForceDependencyResolution")]
        [DisplayName("Force dependency resolution")]
        [PlaceholderText("false")]
        public bool Force { get; set; }
        [Category("Advanced")]
        [ScriptAlias("Verbosity")]
        [DefaultValue(DotNetVerbosityLevel.Minimal)]
        public DotNetVerbosityLevel Verbosity { get; set; } = DotNetVerbosityLevel.Minimal;
        [Category("Advanced")]
        [ScriptAlias("ContinuousIntegrationBuild")]
        [DefaultValue(true)]
        [DisplayName("CI build")]
        [Description("Sets the ContinuousIntegrationBuild MSBuild flag, which is recommended for all official (non-local) builds.")]
        public bool ContinuousIntegrationBuild { get; set; } = true;

        protected abstract string CommandName { get; }

        public override async Task ExecuteAsync(IOperationExecutionContext context)
        {
            var projectPath = context.ResolvePath(this.ProjectPath);

            var dotNetPath = await this.GetDotNetExePath(context, projectPath);
            if (string.IsNullOrEmpty(dotNetPath))
                return;

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

            if (!string.IsNullOrWhiteSpace(this.Output))
            {
                args.Append("--output ");
                args.AppendArgument(context.ResolvePath(this.Output));
            }

            if (!string.IsNullOrWhiteSpace(this.Version))
                args.AppendArgument($"-p:Version={this.Version}");

            var fileOps = await context.Agent.GetServiceAsync<IFileOperationsExecuter>();

            if (!string.IsNullOrEmpty(this.VSToolsPath))
            {
                string vsToolsPathArg;
                if (this.VSToolsPath == "embedded")
                {
                    var path = fileOps.CombinePath(await fileOps.GetBaseWorkingDirectoryAsync(), ".dotnet-ext");
                    var zipPath = fileOps.CombinePath(path, "VSTargets.zip");
                    using (var src = typeof(DotNetBuildOrPublishOperation).Assembly.GetManifestResourceStream("Inedo.Extensions.DotNet.VSTargets.zip"))
                    {
                        using var dest = await fileOps.OpenFileAsync(zipPath, FileMode.Create, FileAccess.Write);
                        await src.CopyToAsync(dest, context.CancellationToken);
                    }

                    vsToolsPathArg = fileOps.CombinePath(path, "vstools");
                    await fileOps.ExtractZipFileAsync(zipPath, vsToolsPathArg, IO.FileCreationOptions.OverwriteReadOnly);
                }
                else if (this.VSToolsPath == "search")
                {
                    this.LogDebug("VSToolsPath is set to \"search\", so using vswhere.exe to search...");
                    // use vswhere to try finding this path
                    vsToolsPathArg = await FindMSBuildPathUsingVSWhereAsync(context);
                    if (vsToolsPathArg == null)
                        this.LogWarning("VSToolsPath is set to \"search\", but a location could not be found.");
                    else
                        this.LogDebug("Found path: " + vsToolsPathArg);
                }
                else
                {
                    vsToolsPathArg = this.VSToolsPath;
                }

                if (!string.IsNullOrWhiteSpace(vsToolsPathArg))
                    args.AppendArgument($"-p:VSToolsPath={vsToolsPathArg}");
            }

            if (this.Force)
                args.Append("--force ");

            if (this.Verbosity != DotNetVerbosityLevel.Minimal)
            {
                args.Append("--verbosity ");
                args.AppendArgument(this.Verbosity.ToString().ToLowerInvariant());
            }

            if (!string.IsNullOrWhiteSpace(this.PackageSource))
            {
                var source = await AhPackages.GetPackageSourceAsync(this.PackageSource, context, context.CancellationToken);
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

                args.Append("--source ");
                args.AppendArgument(nuuget.SourceUrl);
            }

            if (this.ContinuousIntegrationBuild)
                args.AppendArgument("-p:ContinuousIntegrationBuild=true");

            this.AppendAdditionalArguments(args);

            if (!string.IsNullOrWhiteSpace(this.AdditionalArguments))
                args.Append(this.AdditionalArguments);

            this.LogDebug($"Ensuring working directory {context.WorkingDirectory} exists...");
            await fileOps.CreateDirectoryAsync(context.WorkingDirectory);

            var fullArgs = args.ToString();
            this.LogDebug($"Executing dotnet {fullArgs}...");

            bool errNothingToDo = false, errMsWebAppTargets = false, errMSB4062_tasks = false;

            this.MessageLogged += DotNetBuildOrPublishOperation_MessageLogged;
            int res = await this.ExecuteCommandLineAsync(
                context,
                new RemoteProcessStartInfo
                {
                    FileName = dotNetPath,
                    Arguments = fullArgs,
                    WorkingDirectory = context.WorkingDirectory
                }
            );
            this.MessageLogged -= DotNetBuildOrPublishOperation_MessageLogged;

            if (res == 0)
            {
                this.LogDebug("dotnet exited successfully (exitcode=0)");
            }
            else
            {
                this.LogError($"dotnet did not exit successfully (exitcode={res}).");
                if (errNothingToDo && !string.IsNullOrEmpty(this.PackageSource))
                {
                    this.LogInformation(
                        "[TIP] It doesn't look like any NuGet packages were restored during the build process. " +
                        $"This usually means that the Package Source specified ({this.PackageSource}) " +
                        "does not contain the required packages, which may lead this build error.");
                }

                if (errMsWebAppTargets && string.IsNullOrEmpty(this.VSToolsPath))
                {
                    this.LogInformation(
                        "[TIP] It looks like this project requires MSBuild targets that typically part of Visual Studio. " +
                        "To resolve this, set the \"VSToolsPath\" to \"embedded\", which will instruct MSBuild to try to use the " +
                        "common MSBuild targets that we've included in BuildMaster.");
                }
                else if (errMSB4062_tasks || errMsWebAppTargets)
                {
                    if (this.VSToolsPath == "embedded")
                    {
                        this.LogInformation(
                            "[TIP] Unfortunately, it looks like \"embedded\" didn't work as the VSToolsPath. " +
                            "If Visual Studio is installed on this server, try using \"search\" or entering the location (e.g. "
                            + @"C:\Program Files\Microsoft Visual Studio\2022\Enterprise\MSBuild\Microsoft\VisualStudio\v17.0" +
                            ") and then Devenv::Build (which uses Visual Studio). " +
                            "If Visual Studio is not installed, try using the MSBuild::Build-Project operation first.");
                    }
                    else
                    {
                        this.LogInformation(
                            "[TIP] Unfortunately, it looks like dotnet still isn't able to resolve the targets specified in VSToolsPath. " +
                            "Try switching to the MSBuild::Build-Project or Devenv::Build operation instead.");
                    }
                }
            }

            void DotNetBuildOrPublishOperation_MessageLogged(object sender, LogMessageEventArgs e)
            {
                if (!errNothingToDo && e.Message.Contains("Nothing to do. None of the projects specified contain packages to restore."))
                    errNothingToDo = true;

                if (!errMsWebAppTargets && Regex.IsMatch(e.Message, @"(error MSB4019: The imported project)(.*)(Web(Applications)?\\Microsoft\.)(.*)(\.targets)"))
                    errMsWebAppTargets = true;

                if (!errMSB4062_tasks && e.Message.Contains("error MSB4062"))
                    errMSB4062_tasks = true;
            }
        }

        protected virtual void AppendAdditionalArguments(StringBuilder args)
        {
        }
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

        private async Task<string> FindMSBuildPathUsingVSWhereAsync(IOperationExecutionContext context)
        {
            var fileOps = await context.Agent.GetServiceAsync<IFileOperationsExecuter>();
            var path = fileOps.CombinePath(await fileOps.GetBaseWorkingDirectoryAsync(), ".dotnet-ext");
            var vsWherePath = fileOps.CombinePath(path, "vswhere.exe");
            using (var src = typeof(DotNetBuildOrPublishOperation).Assembly.GetManifestResourceStream("Inedo.Extensions.DotNet.vswhere.exe"))
            {
                using var dest = await fileOps.OpenFileAsync(vsWherePath, FileMode.Create, FileAccess.Write);
                await src.CopyToAsync(dest, context.CancellationToken);
            }

            var outputFile = fileOps.CombinePath(path, "vswhere.out");

            // vswhere.exe documentation: https://github.com/Microsoft/vswhere/wiki
            // component IDs documented here: https://docs.microsoft.com/en-us/visualstudio/install/workload-and-component-ids
            var startInfo = new RemoteProcessStartInfo
            {
                FileName = vsWherePath,
                WorkingDirectory = PathEx.GetDirectoryName(vsWherePath),
                Arguments = @"-products * -nologo -format xml -utf8 -latest -sort -requires Microsoft.Component.MSBuild -find **\MSBuild.exe",
                OutputFileName = outputFile
            };

            this.LogDebug("Process: " + startInfo.FileName);
            this.LogDebug("Arguments: " + startInfo.Arguments);
            this.LogDebug("Working directory: " + startInfo.WorkingDirectory);

            await this.ExecuteCommandLineAsync(context, startInfo).ConfigureAwait(false);
            using var outStream = await fileOps.OpenFileAsync(outputFile, FileMode.Open, FileAccess.Read);

            var xdoc = await XDocument.LoadAsync(outStream, LoadOptions.None, context.CancellationToken);

            var files = from f in xdoc.Root.Descendants("file")
                        let file = f.Value
                        // unincluse arm for now
                        where file.IndexOf("arm64", StringComparison.OrdinalIgnoreCase) < 0
                        // prefer 32-bit MSBuild
                        orderby file.IndexOf("amd64", StringComparison.OrdinalIgnoreCase) > -1 ? 1 : 0
                        select file;

            var filePath = files.FirstOrDefault();

            if (string.IsNullOrWhiteSpace(filePath))
                return null;

            return PathEx.GetDirectoryName(filePath);
        }
    }
}
