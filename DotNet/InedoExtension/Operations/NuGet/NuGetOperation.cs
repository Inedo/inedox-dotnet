using System.ComponentModel;
using Inedo.Agents;
using Inedo.Diagnostics;
using Inedo.Documentation;
using Inedo.Extensibility;
using Inedo.Extensibility.Operations;
using Inedo.Extensions.DotNet.Operations.DotNet;
using Inedo.IO;

namespace Inedo.Extensions.DotNet.Operations.NuGet
{
    [Tag("nuget")]
    public abstract class NuGetOperation : DotNetOperation
    {
        protected NuGetOperation()
        {
        }

        [Category("Advanced")]
        [ScriptAlias("NuGetExePath")]
        [DefaultValue("$NuGetExePath")]
        [DisplayName("NuGet.exe path")]
        [Description("Full path to NuGet.exe on the target server. When not set, the included nuget.exe will be used. This will only be used if dotnet is not available on a Windows server or PreferNuGetExe is set to true.")]
        public string NuGetExePath { get; set; }
        [Category("Advanced")]
        [ScriptAlias("PreferNuGetExe")]
        [DisplayName("Prefer NuGet.exe")]
        [Description("When true, NuGet.exe will be used when run on Windows if it is available, even if dotnet is also available.")]
        public bool PreferNuGetExe { get; set; }

        protected async Task<ToolInfo> GetNuGetInfoAsync(IOperationExecutionContext context)
        {
            var dotNetPath = await this.GetDotNetExePath(context, false);

            // nuget.exe can only be used on windows
            if (await context.Agent.TryGetServiceAsync<ILinuxFileOperationsExecuter>() == null)
            {
                // use dotnet unless the PreferNuGet flag is specified or dotnet is not installed
                if (this.PreferNuGetExe || string.IsNullOrEmpty(dotNetPath))
                {
                    var nugetPath = await this.GetNuGetExePathAsync(context);
                    this.LogDebug("Using nuget.exe at " + nugetPath);
                    return new ToolInfo(nugetPath, true);
                }
            }

            return new ToolInfo(dotNetPath, false);
        }
        protected async Task<int> ExecuteNuGetAsync(IOperationExecutionContext context, ToolInfo toolInfo, string args, string workingDirectory, string logArgs = null)
        {
            if (!string.IsNullOrWhiteSpace(this.AdditionalArguments))
            {
                args += " " + this.AdditionalArguments;
                if (logArgs != null)
                    logArgs += this.AdditionalArguments;
            }

            this.LogDebug("Executing: " + toolInfo.ExePath + " " + (logArgs ?? args));

            int exitCode = await this.ExecuteCommandLineAsync(
                context,
                new RemoteProcessStartInfo
                {
                    FileName = toolInfo.ExePath,
                    Arguments = args,
                    WorkingDirectory = workingDirectory
                }
            ).ConfigureAwait(false);

            return exitCode;
        }
        protected static string TrimDirectorySeparator(string d)
        {
            if (string.IsNullOrEmpty(d))
                return d;
            if (d.Length == 1)
                return d;

            return d.TrimEnd('\\', '/');
        }

        private async Task<string> GetNuGetExePathAsync(IOperationExecutionContext context)
        {
            if (!string.IsNullOrEmpty(this.NuGetExePath))
                return context.ResolvePath(this.NuGetExePath);

            var executer = await context.Agent.GetServiceAsync<IRemoteMethodExecuter>().ConfigureAwait(false);
            string assemblyDir = await executer.InvokeFuncAsync(GetNuGetExeDirectory).ConfigureAwait(false);

            return PathEx.Combine(assemblyDir, "nuget.exe");
        }
        private static string GetNuGetExeDirectory() => PathEx.GetDirectoryName(typeof(NuGetOperation).Assembly.Location);

        protected sealed class ToolInfo
        {
            public ToolInfo(string exePath, bool isNuGet)
            {
                this.ExePath = exePath;
                this.IsNuGetExe = isNuGet;
            }

            public string ExePath { get; }
            public bool IsNuGetExe { get; }
        }
    }
}
