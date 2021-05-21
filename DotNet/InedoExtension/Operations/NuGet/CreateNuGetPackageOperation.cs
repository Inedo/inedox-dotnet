using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using Inedo.Agents;
using Inedo.Diagnostics;
using Inedo.Documentation;
using Inedo.Extensibility;
using Inedo.Extensibility.Operations;
using Inedo.IO;

namespace Inedo.Extensions.DotNet.Operations.NuGet
{
    [Tag("nuget")]
    [ScriptNamespace("NuGet")]
    [ScriptAlias("Create-Package")]
    [DisplayName("Create NuGet Package")]
    [Description("Creates a NuGet package from a project or .nuspec file (requires Windows).")]
    [DefaultProperty(nameof(ProjectPath))]
    [SeeAlso(typeof(DotNet.DotNetPackOperation), Comments = "The DotNet::Pack operation is cross-platform, but only works on project files.")]
    public sealed class CreateNuGetPackageOperation : ExecuteOperation
    {
        [Required]
        [ScriptAlias("SourceFile")]
        [DisplayName("Source file")]
        [Description("The .nuspec or MSBuild project that will be passed to NuGet.exe.")]
        public string ProjectPath { get; set; }
        [Category("Advanced")]
        [ScriptAlias("Verbose")]
        [DisplayName("Verbose logging")]
        public bool Verbose { get; set; }
        [ScriptAlias("Version")]
        [Description("The package version that will be passed to NuGet.exe.")]
        public string Version { get; set; }
        [ScriptAlias("Symbols")]
        [Description("When true, the -Symbols argument will be passed to NuGet.exe.")]
        public bool Symbols { get; set; }
        [ScriptAlias("Build")]
        [Description("When true, the -Build argument will be passed to NuGet.exe.")]
        public bool Build { get; set; }

        [Category("Advanced")]
        [ScriptAlias("Properties")]
        [Description("When Build is true, these values will be passed to NuGet.exe as MSBuild properties in the format PROP=VALUE.")]
        public IEnumerable<string> Properties { get; set; }
        [Category("Advanced")]
        [ScriptAlias("IncludeReferencedProjects")]
        [DisplayName("Include ref. projects")]
        [Description("When true, the -IncludeReferencedProjects argument will be passed to NuGet.exe.")]
        public bool IncludeReferencedProjects { get; set; }
        [ScriptAlias("OutputDirectory")]
        [DisplayName("Output directory")]
        [Description("The output directory that will be passed to NuGet.exe.")]
        public string OutputDirectory { get; set; }
        [DisplayName("Source directory")]
        [Description("The working directory to use when executing NuGet.")]
        [ScriptAlias("SourceDirectory")]
        [DefaultValue("$WorkingDirectory")]
        public string SourceDirectory { get; set; }

        [Category("Advanced")]
        [ScriptAlias("NuGetExePath")]
        [DefaultValue("$NuGetExePath")]
        [DisplayName("NuGet.exe path")]
        [Description("Full path to NuGet.exe on the target server. When not set, the included nuget.exe will be used. This will only be used if dotnet is not available on a Windows server or PreferNuGetExe is set to true.")]
        public string NuGetExePath { get; set; }
        [Category("Advanced")]
        [ScriptAlias("Arguments")]
        [DisplayName("Additional arguments")]
        [Description("When specified, these arguments will be passed to NuGet.exe verbatim.")]
        public string AdditionalArguments { get; set; }

        public override async Task ExecuteAsync(IOperationExecutionContext context)
        {
            var fileOps = await context.Agent.GetServiceAsync<IFileOperationsExecuter>().ConfigureAwait(false);
            var nugetExe = await this.GetNuGetExePathAsync(context).ConfigureAwait(false);
            if (string.IsNullOrEmpty(nugetExe))
            {
                this.LogError("nuget.exe path was empty.");
                return;
            }

            var sourceDirectory = context.ResolvePath(this.SourceDirectory);
            var outputDirectory = context.ResolvePath(this.OutputDirectory, this.SourceDirectory);
            var fullProjectPath = context.ResolvePath(this.ProjectPath, this.SourceDirectory);

            if (!await fileOps.FileExistsAsync(fullProjectPath).ConfigureAwait(false))
            {
                this.LogError(fullProjectPath + " does not exist.");
                return;
            }

            fileOps.CreateDirectory(outputDirectory);

            this.LogInformation($"Creating NuGet package from {fullProjectPath} to {outputDirectory}...");
            await this.ExecuteNuGet(context, nugetExe, fullProjectPath, sourceDirectory, outputDirectory).ConfigureAwait(false);
        }

        protected override ExtendedRichDescription GetDescription(IOperationConfiguration config)
        {
            return new ExtendedRichDescription(
                new RichDescription(
                    "Create NuGet package from ",
                    new DirectoryHilite(config[nameof(this.ProjectPath)])
                ),
                new RichDescription(
                    "in ",
                    new DirectoryHilite(config[nameof(this.OutputDirectory)])
                )
            );
        }

        private Task ExecuteNuGet(IOperationExecutionContext context, string nugetExe, string projectPath, string sourceDirectory, string outputDirectory)
        {
            var argList = new List<string>
            {
                "\"" + projectPath + "\"",
                "-BasePath \"" + TrimDirectorySeparator(sourceDirectory) + "\"",
                "-OutputDirectory \"" + TrimDirectorySeparator(outputDirectory) + "\""
            };

            bool isNuspec = projectPath.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase);
            var properties = this.Properties?.ToList();

            if (this.Verbose)
                argList.Add("-Verbose");
            if (!string.IsNullOrEmpty(this.Version))
                argList.Add("-Version \"" + this.Version + "\"");
            if (this.Symbols)
                argList.Add("-Symbols");
            if (this.IncludeReferencedProjects)
                argList.Add("-IncludeReferencedProjects");
            if (this.Build && !isNuspec)
                argList.Add("-Build");
            if (properties?.Count > 0 && !isNuspec)
                argList.Add("-Properties \"" + string.Join(";", properties) + "\"");

            return this.ExecuteNuGetAsync(context, nugetExe, "pack " + string.Join(" ", argList));
        }

        private async Task<string> GetNuGetExePathAsync(IOperationExecutionContext context)
        {
            if (!string.IsNullOrEmpty(this.NuGetExePath))
                return context.ResolvePath(this.NuGetExePath);

            var executer = await context.Agent.GetServiceAsync<IRemoteMethodExecuter>().ConfigureAwait(false);
            string assemblyDir = await executer.InvokeFuncAsync(GetNuGetExeDirectory).ConfigureAwait(false);

            return PathEx.Combine(assemblyDir, "nuget.exe");
        }
        private async Task ExecuteNuGetAsync(IOperationExecutionContext context, string nugetExe, string args, string logArgs = null)
        {
            if (!string.IsNullOrWhiteSpace(this.AdditionalArguments))
            {
                args += " " + this.AdditionalArguments;
                if (logArgs != null)
                    logArgs += this.AdditionalArguments;
            }

            this.LogDebug("Executing: " + nugetExe + " " + (logArgs ?? args));

            int exitCode = await this.ExecuteCommandLineAsync(
                context,
                new RemoteProcessStartInfo
                {
                    FileName = nugetExe,
                    Arguments = args
                }
            ).ConfigureAwait(false);

            if (exitCode != 0)
                this.LogError($"NuGet.exe exited with code {exitCode}");
        }
        protected override void LogProcessOutput(string text)
        {
            if (text.Contains("Unable to find version ") || text.StartsWith("WARNING: "))
                this.LogWarning(text);
            else
                base.LogProcessOutput(text);
        }

        private static string GetNuGetExeDirectory() => PathEx.GetDirectoryName(typeof(CreateNuGetPackageOperation).Assembly.Location);
        private static string TrimDirectorySeparator(string d)
        {
            if (string.IsNullOrEmpty(d))
                return d;
            if (d.Length == 1)
                return d;

            return d.TrimEnd('\\', '/');
        }
    }
}
