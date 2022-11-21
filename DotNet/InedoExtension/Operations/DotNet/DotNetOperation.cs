using System.ComponentModel;
using System.Text.RegularExpressions;
using Inedo.Agents;
using Inedo.Diagnostics;
using Inedo.Documentation;
using Inedo.Extensibility;
using Inedo.Extensibility.Operations;
using Inedo.IO;

namespace Inedo.Extensions.DotNet.Operations.DotNet
{
    [Tag(".net")]
    public abstract class DotNetOperation : ExecuteOperation
    {
        private static readonly LazyRegex WarningRegex = new(@"\bwarning\b", RegexOptions.Compiled);

        protected DotNetOperation()
        {
        }

        [Category("Advanced")]
        [ScriptAlias("AdditionalArguments")]
        [DisplayName("Additional arguments")]
        public string AdditionalArguments { get; set; }

        [Category("Advanced")]
        [ScriptAlias("DotNetPath")]
        [ScriptAlias("DotNetExePath", Obsolete = true)]
        [DisplayName("dotnet path")]
        [PlaceholderText("default")]
        [Description("Full path of dotnet.exe (or dotnet on Linux). This is usually C:\\Program Files\\dotnet\\dotnet.exe on Windows. If no value is supplied, the operation will default to %PROGRAMFILES%\\dotnet\\dotnet.exe for Windows and dotnet (from the path) on Linux.")]
        public string DotNetExePath { get; set; }

        protected override void LogProcessOutput(string text) => this.Log(WarningRegex.IsMatch(text) ? MessageLevel.Warning : MessageLevel.Debug, text);

        protected async Task<string> GetDotNetExePath(IOperationExecutionContext context, bool logErrorIfNotFound = true)
        {
            if (!string.IsNullOrWhiteSpace(this.DotNetExePath))
            {
                this.LogDebug($"dotnet path: {this.DotNetExePath}");
                return this.DotNetExePath;
            }

            await foreach (var path in GetPossibleDotNetPathsAsync(context))
            {
                this.LogDebug($"dotnet path: {path}");
                return path;
            }

            if (logErrorIfNotFound)
            {
                this.LogError("Could find dotnet.exe on this server.");
                this.LogInformation(
                    "[TIP] This error usually means that the .NET SDK is not installed on this server. Try downloading/installing .NET SDK " +
                    "on this server, and retry the build. If .NET is installed, then you can create a server-scoped variable named $DotNetExePath " +
                    "to set the location of dotnet.exe (or dotnet on Linux)."
                );
            }

            return null;
        }

        private static async IAsyncEnumerable<string> GetPossibleDotNetPathsAsync(IOperationExecutionContext context)
        {
            var fileOps = await context.Agent.GetServiceAsync<IFileOperationsExecuter>();
            var remoteProcess = await context.Agent.GetServiceAsync<IRemoteProcessExecuter>();

            if (fileOps.DirectorySeparator == '\\')
            {
                var localAppDataDir = await remoteProcess.GetEnvironmentVariableValueAsync("LocalAppData");
                if (!string.IsNullOrWhiteSpace(localAppDataDir))
                {
                    var path = fileOps.CombinePath(localAppDataDir, "Microsoft", "dotnet", "dotnet.exe");
                    if (await fileOps.FileExistsAsync(path))
                        yield return path;
                }

                var programFilesDir = await remoteProcess.GetEnvironmentVariableValueAsync("ProgramFiles");
                if (!string.IsNullOrWhiteSpace(programFilesDir))
                {
                    var path = fileOps.CombinePath(localAppDataDir, "dotnet", "dotnet.exe");
                    if (await fileOps.FileExistsAsync(path))
                        yield return path;
                }
            }
            else
            {
                var homeDir = await remoteProcess.GetEnvironmentVariableValueAsync("HOME");
                if (!string.IsNullOrWhiteSpace(homeDir))
                {
                    var path = fileOps.CombinePath(homeDir, ".dotnet", "dotnet");
                    if (await fileOps.FileExistsAsync(path))
                        yield return path;
                }
            }

            var pathDirs = await remoteProcess.GetEnvironmentVariableValueAsync("PATH");
            if (!string.IsNullOrWhiteSpace(pathDirs))
            {
                var binaryName = fileOps.DirectorySeparator == '\\' ? "dotnet.exe" : "dotnet";
                foreach (var p in pathDirs.Split(';', StringSplitOptions.TrimEntries))
                {
                    var name = PathEx.GetFileName(p);
                    if (name is "dotnet" or ".dotnet")
                    {
                        var path = fileOps.CombinePath(p, binaryName);
                        if (await fileOps.FileExistsAsync(path))
                            yield return path;
                    }
                }
            }
        }
    }
}
