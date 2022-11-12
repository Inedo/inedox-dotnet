using System;
using System.ComponentModel;
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
using Inedo.Extensions.PackageSources;
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

        [Category("Advanced")]
        [ScriptAlias("Version")]
        [PlaceholderText("not set")]
        public string Version { get; set; }
        [Category("Advanced")]
        [ScriptAlias("Framework")]
        [SuggestableValue(typeof(TargetFrameworkSuggestionProvider))]
        public string Framework { get; set; }
        [Category("Advanced")]
        [ScriptAlias("Runtime")]
        [SuggestableValue(typeof(RuntimeSuggestionProvider))]
        public string Runtime { get; set; }
        [Category("Advanced")]
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
        [Category("Advanced")]
        [ScriptAlias("ForceDependencyResolution")]
        [DisplayName("Force dependency resolution")]
        [PlaceholderText("false")]
        public bool Force { get; set; }
        [Category("Advanced")]
        [ScriptAlias("Verbosity")]
        [DefaultValue(DotNetVerbosityLevel.Minimal)]
        public DotNetVerbosityLevel Verbosity { get; set; } = DotNetVerbosityLevel.Minimal;

        protected abstract string CommandName { get; }

        public override async Task ExecuteAsync(IOperationExecutionContext context)
        {
            var dotNetPath = await this.GetDotNetExePath(context);
            if (string.IsNullOrEmpty(dotNetPath))
                return;

            var projectPath = context.ResolvePath(this.ProjectPath);

            var args = new StringBuilder($"{this.CommandName} ");
            args.AppendArgument(projectPath);

            maybeAppend("--configuration ", this.Configuration);
            maybeAppend("--framework ", this.Framework);
            maybeAppend("--runtime ", this.Runtime);
            maybeAppend("--output ", this.Output);
            maybeAppend("-p:Version=", this.Version);

#warning Implement VSToolsPath
            if (!string.IsNullOrEmpty(this.VSToolsPath))
            {
                string vsToolsPathArg;
                if (this.VSToolsPath == "embedded")
                {
                    // extract VSTargets.zip to a directory if not already done (ExtTmp??)
                    // vSToolsPath = that directory
                    vsToolsPathArg = null;
                }
                else if (this.VSToolsPath == "search")
                {
                    this.LogDebug("VSToolsPath is set to \"search\", so using vswhere.exe to search...");
                    // use vswhere to try finding this path
                    vsToolsPathArg = @"C:\Program Files\Microsoft Visual Studio\2022\Enterprise\MSBuild\Microsoft\VisualStudio\v17.0";
                    if (vsToolsPathArg == null)
                        this.LogWarning("VSToolsPath is set to \"search\", but a location could not be found.");
                    else
                        this.LogDebug("Found path: " + vsToolsPathArg);
                }
                else
                    vsToolsPathArg = this.VSToolsPath;

                maybeAppend("-p:VSToolsPath=", vsToolsPathArg);
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

            this.AppendAdditionalArguments(args);

            if (!string.IsNullOrWhiteSpace(this.AdditionalArguments))
                args.Append(this.AdditionalArguments);

            this.LogDebug($"Ensuring working directory {context.WorkingDirectory} exists...");
            var fileOps = await context.Agent.GetServiceAsync<IFileOperationsExecuter>();
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
                this.LogDebug("dotnet exited successfully (exitcode=0)");
            
            else
            {
                this.LogError($"dotnet did not exit successfully (exitcode={res}).");
                if (errNothingToDo && !string.IsNullOrEmpty(this.PackageSource))
                    this.LogInformation(
                        $"[TIP] It doesn't look like any NuGet packages were restored during the build process. " +
                        $"This usually means that the Package Source specified ({this.PackageSource}) " +
                        "does not contain the required packages, which may lead this build error.");

                if (errMsWebAppTargets && string.IsNullOrEmpty(this.VSToolsPath))
                    this.LogInformation(
                        $"[TIP] It looks like this project requires MSBuild targets that typically part of Visual Studio. " +
                        $"To resolve this, set the \"VSToolsPath\" to \"embedded\", which will instruct MSBuild to try to use the " +
                        $"common MSBuild targets that we've included in BuildMaster.");

                else if (errMSB4062_tasks || errMsWebAppTargets)
                {
                    if (this.VSToolsPath == "embedded")
                        this.LogInformation(
                            $"[TIP] Unfortunately, it looks like \"embedded\" didn't work as the VSToolsPath. " +
                            $"If Visual Studio is installed on this server, try using \"search\" or entering the location (e.g. " 
                            + @"C:\Program Files\Microsoft Visual Studio\2022\Enterprise\MSBuild\Microsoft\VisualStudio\v17.0" +
                            ") and then Devenv::Build (which uses Visual Studio). " +
                            "If Visual Studio is not installed, try using the MSBuild::Build-Project operation first.");
                    else
                        this.LogInformation(
                            $"[TIP] Unfortunately, it looks like dotnet still isn't able to resolve the targets specified in VSToolsPath. " +
                            $"Try switching to the MSBuild::Build-Project or Devenv::Build operation instead.");

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
            void maybeAppend(string arg, string maybeValue)
            {
                if (string.IsNullOrWhiteSpace(maybeValue))
                    return;
                args.Append(arg);
                args.AppendArgument(maybeValue);
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
    }
}
