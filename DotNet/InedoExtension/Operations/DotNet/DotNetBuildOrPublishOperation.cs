﻿using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using Inedo.Agents;
using Inedo.Diagnostics;
using Inedo.Documentation;
using Inedo.ExecutionEngine.Executer;
using Inedo.Extensibility;
using Inedo.Extensibility.Operations;
using Inedo.Extensions.DotNet.SuggestionProviders;
using Inedo.Extensions.PackageSources;
using Inedo.Extensions.SecureResources;
using Inedo.ProGet;
using Inedo.Web;

namespace Inedo.Extensions.DotNet.Operations.DotNet
{
    [DefaultProperty(nameof(ProjectPath))]
    public abstract class DotNetBuildOrPublishOperation : DotNetOperation, IVSWhereOperation
    {
        private static readonly LazyRegex ErrorRegex = new(@":\s*error\b", RegexOptions.Compiled);

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

        [ScriptAlias("ImageBasedService")]
        [SuggestableValue(typeof(DotNetIBSSuggestionProvider))]
        public string ImageBasedService { get; set; }

        protected abstract string CommandName { get; }

        private bool UseContainer => !string.IsNullOrEmpty(this.ImageBasedService);

        protected override void LogProcessOutput(string text)
        {
            if (this.UseContainer && ErrorRegex.IsMatch(text))
                this.Log(MessageLevel.Error, text);
            else
                base.LogProcessOutput(text);
        }
        protected override void LogProcessError(string text)
        {
            if (this.UseContainer)
                this.LogDebug(text);
            else
                base.LogProcessError(text);
        }

        public override async Task ExecuteAsync(IOperationExecutionContext context)
        {
            Func<string, string> resolvePath = context.ResolvePath;
            DockerHostShim docker = null;
            if (this.UseContainer)
            {
                this.LogDebug($"Performing containerized build using \"{this.ImageBasedService}\" image based service.");
                docker = await DockerHostShim.CreateAsync(context);
                resolvePath = docker.ResolveContainerPath;
            }

            var projectPath = resolvePath(this.ProjectPath);

            var dotNetPath = this.UseContainer ? "dotnet" : await this.GetDotNetExePath(context, projectPath);
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
                args.AppendArgument(resolvePath(this.Output));
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
                    vsToolsPathArg = await this.FindUsingVSWhereAsync(context, "-requires Microsoft.Component.MSBuild -find **\\MSBuild.exe");
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
                var packageSource = new PackageSourceId(this.PackageSource!);

                if (packageSource.Format == PackageSourceIdFormat.ProGetFeed)
                {
                    IPackageSource source = null;
                    try
                    {
                        source = await AhPackages.GetPackageSourceAsync(this.PackageSource, context, context.CancellationToken);
                    }
                    catch
                    {
                    }

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
                else if(packageSource.Format == PackageSourceIdFormat.SecureResource)
                {
                    if (!context.TryGetSecureResource(packageSource.GetResourceName(), out var resource) || resource is not NuGetPackageSource nps)
                    {
                        this.LogError($"Package source \"{this.PackageSource}\" not found or is not a NuGetPackageSource.");
                        return;
                    }
                    args.Append("--source ");
                    args.AppendArgument(nps.ApiEndpointUrl);
                }
                else if(packageSource.Format == PackageSourceIdFormat.Url)
                {
                    args.Append("--source ");
                    args.AppendArgument(packageSource.GetUrl());
                }
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

            int res;

            var startInfo = new RemoteProcessStartInfo
            {
                FileName = dotNetPath,
                Arguments = fullArgs,
                WorkingDirectory = context.WorkingDirectory
            };

            if (docker == null)
            {
                res = await this.ExecuteCommandLineAsync(context, startInfo);
            }
            else
            {
                res = await docker.ExecuteInContainerAsync(
                    new ContainerStartInfoShim(
                        this.ImageBasedService,
                        startInfo,
                        OutputDataReceived: this.LogProcessOutput,
                        ErrorDataReceived: this.LogProcessError
                    ),
                    context.CancellationToken
                );
            }

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

        Task<int> IVSWhereOperation.ExecuteCommandLineAsync(IOperationExecutionContext context, RemoteProcessStartInfo startInfo) => this.ExecuteCommandLineAsync(context, startInfo);

        private sealed class DockerHostShim
        {
            private readonly object host;

            private DockerHostShim(object host) => this.host = host;

            public static async Task<DockerHostShim> CreateAsync(IOperationExecutionContext context)
            {
                try
                {
                    return (await CreateInternalAsync(context)) ?? throw new ExecutionFailureException($"Server {context.ServerName} does not have a Docker engine.");
                }
                catch
                {
                    throw new ExecutionFailureException($"Containerized builds are not supported in this version of {SDK.ProductName}.");
                }
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            public string ResolveContainerPath(string relativePath) => ((IDockerHost)this.host).ResolveContainerPath(relativePath);
            [MethodImpl(MethodImplOptions.NoInlining)]
            public Task<int> ExecuteInContainerAsync(ContainerStartInfoShim containerStartInfo, CancellationToken cancellationToken = default)
            {
                return ((IDockerHost)this.host).ExecuteInContainerAsync(
                    new ContainerStartInfo(containerStartInfo.Image, containerStartInfo.StartInfo, containerStartInfo.MapExecutionDirectory, OutputDataReceived: containerStartInfo.OutputDataReceived, ErrorDataReceived: containerStartInfo.ErrorDataReceived),
                    cancellationToken
                );
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static async Task<DockerHostShim> CreateInternalAsync(IOperationExecutionContext context)
            {
                var docker = await context.TryGetServiceAsync<IDockerHost>();
                if (docker != null)
                    return new DockerHostShim(docker);
                else
                    return null;
            }
        }

        private sealed record ContainerStartInfoShim(string Image, RemoteProcessStartInfo StartInfo, bool MapExecutionDirectory = true, Action<string> OutputDataReceived = null, Action<string> ErrorDataReceived = null);
    }
}
