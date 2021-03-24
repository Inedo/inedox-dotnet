using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Xml.Linq;
using Inedo.Agents;
using Inedo.Diagnostics;
using Inedo.Documentation;
using Inedo.Extensibility;
using Inedo.Extensibility.Operations;
using Inedo.Extensions.DotNet.SuggestionProviders;
using Inedo.IO;
using Inedo.Web;
using Microsoft.Win32;

namespace Inedo.Extensions.DotNet.Operations.MSBuild
{
    [Serializable]
    [Tag(".net")]
    [ScriptAlias("Build-Project")]
    [DisplayName("Build MSBuild Project")]
    [Description("Builds a project or solution using MSBuild.")]
    [ScriptNamespace("MSBuild")]
    [DefaultProperty(nameof(ProjectPath))]
    public sealed class BuildMSBuildProjectOperation : RemoteExecuteOperation
    {
        [Required]
        [ScriptAlias("ProjectFile")]
        [DisplayName("Project file")]
        [PlaceholderText("e.g. ProjectName.csproj or SolutionName.sln")]
        public string ProjectPath { get; set; }

        [ScriptAlias("Configuration")]
        [DefaultValue("Release")]
        [DisplayName("Configuration")]
        [SuggestableValue(typeof(BuildConfigurationSuggestionProvider))]
        public string BuildConfiguration { get; set; }

        [ScriptAlias("Platform")]
        [DisplayName("Target platform")]
        [SuggestableValue(typeof(TargetPlatformSuggestionProvider))]
        public string TargetPlatform { get; set; }

        [Category("Advanced")]
        [ScriptAlias("MSBuildProperties")]
        [DisplayName("MSBuild properties")]
        [Description("Additional properties to pass to MSBuild, formatted as key=value pairs.")]
        [FieldEditMode(FieldEditMode.Multiline)]
        public IEnumerable<string> MSBuildProperties { get; set; }

        [Category("Advanced")]
        [ScriptAlias("Arguments")]
        [DisplayName("Additional arguments")]
        [Description("Raw command line arguments to pass to MSBuild.")]
        public string AdditionalArguments { get; set; }

        [Category("Advanced")]
        [ScriptAlias("MSBuildToolsPath")]
        [DefaultValue("$MSBuildToolsPath")]
        [DisplayName("MSBuild tools path")]
        [Description("Full path of the directory containing the MSBuild tools to use. This is usually similar to C:\\Program Files (x86)\\MSBuild\\14.0\\Bin. "
            + "If no value is supplied, the operation will use vswhere to determine the path to the latest installation of MSBuild")]
        public string MSBuildToolsPath { get; set; }

        [ScriptAlias("To")]
        [DisplayName("Target directory")]
        [PlaceholderText("Default")]
        public string TargetDirectory { get; set; }

        protected override ExtendedRichDescription GetDescription(IOperationConfiguration config)
        {
            return new ExtendedRichDescription(
                new RichDescription(
                    "Build ",
                    new DirectoryHilite(config[nameof(this.ProjectPath)])
                ),
                new RichDescription(
                    "with ",
                    new Hilite(config[nameof(this.BuildConfiguration)]),
                    " configuration"
                )
            );
        }

        protected override async Task<object> RemoteExecuteAsync(IRemoteOperationExecutionContext context)
        {
            var projectFullPath = context.ResolvePath(this.ProjectPath);

            this.LogInformation($"Building {projectFullPath}...");

            var buildProperties = string.Join(";", this.MSBuildProperties ?? Enumerable.Empty<string>());

            var config = "Configuration=" + this.BuildConfiguration;
            if (!string.IsNullOrEmpty(this.TargetPlatform))
                config += ";Platform=" + this.TargetPlatform;

            if (!string.IsNullOrEmpty(buildProperties))
                config += ";" + buildProperties;

            var args = $"\"{projectFullPath}\" \"/p:{config}\"";
            if (!string.IsNullOrWhiteSpace(this.TargetDirectory))
                args += $" \"/p:OutDir={context.ResolvePath(this.TargetDirectory).TrimEnd('\\')}\\\\\"";

            if (!string.IsNullOrWhiteSpace(this.AdditionalArguments))
                args += " " + this.AdditionalArguments;

            var workingDir = PathEx.GetDirectoryName(projectFullPath);

            if (!DirectoryEx.Exists(workingDir))
                throw new DirectoryNotFoundException($"Directory {workingDir} does not exist.");

            int result = await this.InvokeMSBuildAsync(context, args, workingDir).ConfigureAwait(false);
            if (result != 0)
                this.LogError($"Build failed (msbuild returned {result}).");

            return null;
        }

        private async Task<int> InvokeMSBuildAsync(IRemoteOperationExecutionContext context, string arguments, string workingDirectory)
        {
            var msbuildLoggerPath = Path.Combine(
                Path.GetDirectoryName(typeof(BuildMSBuildProjectOperation).Assembly.Location),
                "BmBuildLogger.dll"
            );

            var allArgs = $"\"/logger:{msbuildLoggerPath}\" /noconsolelogger " + arguments;

            var msBuildPath = await this.GetMSBuildToolsPath(context).ConfigureAwait(false);
            if (msBuildPath == null)
                return -1;

            msBuildPath = Path.Combine(msBuildPath, "msbuild.exe");

            var startInfo = new RemoteProcessStartInfo
            {
                FileName = msBuildPath,
                Arguments = allArgs,
                WorkingDirectory = workingDirectory
            };

            this.LogDebug("Process: " + startInfo.FileName);
            this.LogDebug("Arguments: " + startInfo.Arguments);
            this.LogDebug("Working directory: " + startInfo.WorkingDirectory);
            
            return await this.ExecuteCommandLineAsync(context, startInfo).ConfigureAwait(false);
        }
        private async Task<string> GetMSBuildToolsPath(IRemoteOperationExecutionContext context)
        {
            if (!string.IsNullOrWhiteSpace(this.MSBuildToolsPath))
            {
                this.LogDebug("MSBuildToolsPath: " + this.MSBuildToolsPath);
                return this.MSBuildToolsPath;
            }

            string path = await this.FindMSBuildPathUsingVSWhereAsync(context).ConfigureAwait(false);

            if (path != null)
            {
                this.LogDebug("MSBuildToolsPath: " + path);
                return path;
            }

            this.LogDebug("Could not find MSBuildToolsPath using vswhere.exe, falling back to registry...");

            path = this.FindMSBuildUsingRegistry();

            if (path != null)
            {
                this.LogDebug("MSBuildToolsPath: " + path);
                return path;
            }

            this.LogError(@"Could not determine MSBuildToolsPath value on this server. To resolve this issue, ensure that MSBuild is available on this server (e.g. by installing the Visual Studio Build Tools) and retry the build, or create a server-scoped variable named $MSBuildToolsPath set to the location of the MSBuild tools. For example, the tools included with Visual Studio 2017 could be installed to C:\Program Files (x86)\Microsoft Visual Studio\2017\Enterprise\MSBuild\15.0\Bin");
            return null;            
        }

        private async Task<string> FindMSBuildPathUsingVSWhereAsync(IRemoteOperationExecutionContext context)
        {
            this.LogDebug("$MSBuildToolsPath variable is not set. Attempting to find the path to the latest version using vswhere.exe...");

            string vsWherePath = PathEx.Combine(
                Path.GetDirectoryName(typeof(BuildMSBuildProjectOperation).Assembly.Location),
                "vswhere.exe"
            );

            string outputFile = Path.GetTempFileName();

            // vswhere.exe documentation: https://github.com/Microsoft/vswhere/wiki
            // component IDs documented here: https://docs.microsoft.com/en-us/visualstudio/install/workload-and-component-ids
            var startInfo = new RemoteProcessStartInfo
            {
                FileName = vsWherePath,
                WorkingDirectory = Path.GetDirectoryName(vsWherePath),
                Arguments = @"-products * -nologo -format xml -utf8 -latest -sort -requires Microsoft.Component.MSBuild -find **\MSBuild.exe",
                OutputFileName = outputFile
            };

            this.LogDebug("Process: " + startInfo.FileName);
            this.LogDebug("Arguments: " + startInfo.Arguments);
            this.LogDebug("Working directory: " + startInfo.WorkingDirectory);

            await this.ExecuteCommandLineAsync(context, startInfo).ConfigureAwait(false);

            var xdoc = XDocument.Load(outputFile);

            var files = from f in xdoc.Root.Descendants("file")
                        let file = f.Value
                        // prefer 32-bit MSBuild
                        orderby file.IndexOf("amd64", StringComparison.OrdinalIgnoreCase) > -1 ? 1 : 0
                        select file;

            var filePath = files.FirstOrDefault();

            if (string.IsNullOrWhiteSpace(filePath))
                return null;

            return Path.GetDirectoryName(filePath);
        }

        private string FindMSBuildUsingRegistry()
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\MSBuild\ToolsVersions", false);
            if (key == null)
                return null;

            var latestVersion = key
                .GetSubKeyNames()
                .Select(k => new { Key = k, Version = TryParse(k) })
                .Where(v => v.Version != null)
                .OrderByDescending(v => v.Version)
                .FirstOrDefault();

            if (latestVersion == null)
                return null;

            using var subkey = key.OpenSubKey(latestVersion.Key, false);
            return subkey.GetValue("MSBuildToolsPath") as string;
        }

        protected override void LogProcessOutput(string text)
        {
            if (!string.IsNullOrWhiteSpace(text) && text.StartsWith("<BM>"))
            {
                var bytes = Convert.FromBase64String(text.Substring("<BM>".Length));
                var message = InedoLib.UTF8Encoding.GetString(bytes, 1, bytes.Length - 1);
                this.Log((MessageLevel)bytes[0], message);
            }
        }

        private static Version TryParse(string s)
        {
            _ = Version.TryParse(s, out Version v);
            return v;
        }
    }
}
