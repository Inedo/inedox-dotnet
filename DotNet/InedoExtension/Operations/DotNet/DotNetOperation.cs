using System.ComponentModel;
using System.Text;
using System.Text.RegularExpressions;
using Inedo.Agents;
using Inedo.Diagnostics;
using Inedo.Documentation;
using Inedo.ExecutionEngine;
using Inedo.ExecutionEngine.Executer;
using Inedo.ExecutionEngine.Variables;
using Inedo.Extensibility;
using Inedo.Extensibility.Operations;
using Inedo.IO;
using Inedo.Web;

namespace Inedo.Extensions.DotNet.Operations.DotNet
{
    [Tag(".net")]
    public abstract class DotNetOperation : ExecuteOperation
    {
        private static readonly LazyRegex WarningRegex = new(@"\bwarning\b", RegexOptions.Compiled);
        private static readonly Lazy<string> DotNetInstallPs1 = new(() => LoadScript("dotnet-install.ps1"));
        private static readonly Lazy<string> DotNetInstallSh = new(() => LoadScript("dotnet-install.sh"));

        protected DotNetOperation()
        {
        }

        [Category("Advanced")]
        [ScriptAlias("AdditionalArguments")]
        [DisplayName("Additional arguments")]
        public string AdditionalArguments { get; set; }

        [Category("Advanced")]
        [ScriptAlias("EnsureDotNetInstalled")]
        [DisplayName("Ensure dotnet installed")]
        [Description("This uses Microsoft's dotnet-install script to ensure that the specified version is installed. Values other than \"auto\" will be passed to the Channel parameter. The \"auto\" value will attempt to determine the SDK your project uses and ensure that it is installed.")]
        [SuggestableValue("auto", "7.0", "6.0", "5.0")]
        [PlaceholderText("not set (do not install)")]
        public string EnsureDotNetInstalled { get; set; }

        [Category("Advanced")]
        [ScriptAlias("DotNetPath")]
        [ScriptAlias("DotNetExePath", Obsolete = true)]
        [DisplayName("dotnet path")]
        [PlaceholderText("default")]
        [Description("Full path of dotnet.exe (or dotnet on Linux). This is usually C:\\Program Files\\dotnet\\dotnet.exe on Windows. If no value is supplied, the operation will default to %PROGRAMFILES%\\dotnet\\dotnet.exe for Windows and dotnet (from the path) on Linux.")]
        public string DotNetExePath { get; set; }

        protected override void LogProcessOutput(string text) => this.Log(WarningRegex.IsMatch(text) ? MessageLevel.Warning : MessageLevel.Debug, text);

        protected async Task<string> GetDotNetExePath(IOperationExecutionContext context, string projectPath, bool logErrorIfNotFound = true)
        {
            if (!string.IsNullOrWhiteSpace(this.DotNetExePath))
            {
                this.LogDebug($"dotnet path specified as: {this.DotNetExePath}");
                return this.DotNetExePath;
            }

            if (!string.IsNullOrEmpty(this.EnsureDotNetInstalled))
                await this.InstallDotNetAsync(context, projectPath);

            this.LogDebug("$DotNetExePath is not specified; attempting to find dotnet...");

            await foreach (var path in GetPossibleDotNetPathsAsync(context))
            {
                this.LogDebug($"dotnet path: {path}");
                return path;
            }

            if (logErrorIfNotFound)
            {
                this.LogError("Could find dotnet on this server.");
                this.LogInformation(
                    "[TIP] This error usually means that the .NET SDK is not installed on this server. Try downloading/installing .NET SDK " +
                    "on this server or go to the advanced tab to ensure that dotnet is installed when running this operation/script, and retry the build. " +
                    "If .NET is installed, then you can create a server-scoped variable named $DotNetExePath " +
                    "to set the location of dotnet.exe (or dotnet on Linux)."
                );
            }

            return null;
        }

        private async IAsyncEnumerable<string> GetPossibleDotNetPathsAsync(IOperationExecutionContext context)
        {
            var fileOps = await context.Agent.GetServiceAsync<IFileOperationsExecuter>();
            var remoteProcess = await context.Agent.GetServiceAsync<IRemoteProcessExecuter>();

            if (fileOps.DirectorySeparator == '\\')
            {
                var localAppDataDir = await remoteProcess.GetEnvironmentVariableValueAsync("LocalAppData");
                if (!string.IsNullOrWhiteSpace(localAppDataDir))
                {
                    var path = fileOps.CombinePath(localAppDataDir, "Microsoft", "dotnet", "dotnet.exe");
                    this.LogDebug($"Searching for dotnet at {path}...");
                    if (await fileOps.FileExistsAsync(path))
                        yield return path;
                }

                var programFilesDir = await remoteProcess.GetEnvironmentVariableValueAsync("ProgramFiles");
                if (!string.IsNullOrWhiteSpace(programFilesDir))
                {
                    var path = fileOps.CombinePath(localAppDataDir, "dotnet", "dotnet.exe");
                    this.LogDebug($"Searching for dotnet at {path}...");
                    if (await fileOps.FileExistsAsync(path))
                        yield return path;
                }
            }
            else
            {
                bool inPath = false;
                try
                {
                    await using var process = remoteProcess.CreateProcess(
                        new RemoteProcessStartInfo
                        {
                            FileName = "dotnet",
                            Arguments = "--info"
                        }
                    );

                    await process.StartAsync(context.CancellationToken);

                    await process.WaitAsync(context.CancellationToken);
                    inPath = process.ExitCode == 0;
                }
                catch
                {
                }

                if (inPath)
                    yield return "dotnet";

                var homeDir = await remoteProcess.GetEnvironmentVariableValueAsync("HOME");
                if (!string.IsNullOrWhiteSpace(homeDir))
                {
                    var path = fileOps.CombinePath(homeDir, ".dotnet", "dotnet");
                    this.LogDebug($"Searching for dotnet at {path}...");
                    if (await fileOps.FileExistsAsync(path))
                        yield return path;
                }
            }

            this.LogDebug("Searching for dotnet in Path environment variable...");
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

        private async Task InstallDotNetAsync(IOperationExecutionContext context, string projectPath)
        {
            this.LogInformation("Ensuring dotnet SDK is present...");

            var fileOps = await context.Agent.GetServiceAsync<IFileOperationsExecuter>();

            string globalJsonPath = null;
            string channel = "STS";

            if (string.Equals(this.EnsureDotNetInstalled, "auto", StringComparison.OrdinalIgnoreCase))
            {
                globalJsonPath = await FindGlobalJsonAsync(fileOps, Path.HasExtension(projectPath) ? PathEx.GetDirectoryName(projectPath) : projectPath);
                if (!string.IsNullOrEmpty(globalJsonPath))
                    this.LogDebug($"Found global.json at {globalJsonPath}.");
            }
            else
            {
                channel = this.EnsureDotNetInstalled;
            }

            using var sw = new StringWriter();
            var writer = new ScriptWriter(sw);
            var action = fileOps.DirectorySeparator == '\\' ? GetPSCall(globalJsonPath, channel) : GetSHExec(globalJsonPath, channel);
            action.Write(writer);

            var nested = context.CreateNestedOtterScript(sw.ToString());
            var scriptVar = new NestedVariable(fileOps.DirectorySeparator == '\\' ? DotNetInstallPs1.Value : DotNetInstallSh.Value);

            nested.SetAdditionalVariables(
                new Dictionary<RuntimeVariableName, IRuntimeVariable>
                {
                    [scriptVar.Name] = scriptVar
                }
            );

            var result = await nested.ExecuteAsync(context.CancellationToken);
            if (result >= ExecutionStatus.Error)
                throw new ExecutionFailureException("Failure installing .NET SDK.");

            this.LogInformation(".NET SDK installed/verified.");
        }
        private static ActionStatement GetSHExec(string globalJsonPath, string channel)
        {
            var args = new StringBuilder("--no-path");

            if (!string.IsNullOrEmpty(globalJsonPath))
            {
                args.Append(" --jsonfile ");
                args.AppendArgument(globalJsonPath);
            }
            else
            {
                args.Append(" --channel ");
                args.AppendArgument(channel);
            }

            return new ActionStatement(
                "SHExec",
                new Dictionary<string, string>
                {
                    ["Text"] = "$installDotNet",
                    ["Arguments"] = ProcessedString.FromRuntimeValue(args.ToString()).ToString()
                }
            );
        }
        private static ActionStatement GetPSCall(string globalJsonPath, string channel)
        {
            var parameters = new Dictionary<string, RuntimeValue> { ["NoPath"] = "true" };

            if (!string.IsNullOrEmpty(globalJsonPath))
                parameters["JSonFile"] = globalJsonPath;
            else
                parameters["Channel"] = channel;

            return new ActionStatement(
                "PSCall",
                new Dictionary<string, string>
                {
                    ["Name"] = "dotnet-install.ps1",
                    ["ScriptText"] = "$installDotNet",
                    ["Parameters"] = ProcessedString.FromRuntimeValue(new RuntimeValue(parameters)).ToString()
                }
            );
        }
        private static async Task<string> FindGlobalJsonAsync(IFileOperationsExecuter fileOps, string path)
        {
            var baseDir = await fileOps.GetBaseWorkingDirectoryAsync();

            while (!string.IsNullOrEmpty(path) && !path.Equals(baseDir, StringComparison.OrdinalIgnoreCase))
            {
                var globalJsonPath = fileOps.CombinePath(path, "global.json");
                if (await fileOps.FileExistsAsync(globalJsonPath))
                    return globalJsonPath;

                path = PathEx.GetDirectoryName(path);
            }

            return null;
        }
        private static string LoadScript(string name)
        {
            using var stream = typeof(DotNetOperation).Assembly.GetManifestResourceStream($"Inedo.Extensions.DotNet.{name}");
            using var reader = new StreamReader(stream, InedoLib.UTF8Encoding);
            return reader.ReadToEnd();
        }

        private sealed class NestedVariable : IRuntimeVariable
        {
            private readonly string value;

            public NestedVariable(string value) => this.value = value;

            public RuntimeVariableName Name => new("installDotNet", RuntimeValueType.Scalar);

            public RuntimeValue GetValue() => this.value;
        }
    }
}
