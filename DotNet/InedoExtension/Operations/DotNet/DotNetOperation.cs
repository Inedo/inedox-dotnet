using System;
using System.ComponentModel;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Inedo.Agents;
using Inedo.Diagnostics;
using Inedo.Documentation;
using Inedo.Extensibility;
using Inedo.Extensibility.Operations;

namespace Inedo.Extensions.DotNet.Operations.DotNet
{
    [Tag(".net")]
    [Note("This operation requires .NET Core build tools v2.0+ to be installed on the server.")]
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

        protected async Task<string> GetDotNetExePath(IOperationExecutionContext context)
        {
            if (!string.IsNullOrWhiteSpace(this.DotNetExePath))
            {
                this.LogDebug("dotnet path: " + this.DotNetExePath);
                return this.DotNetExePath;
            }

            var fileOps = await context.Agent.GetServiceAsync<IFileOperationsExecuter>();
            if (fileOps.DirectorySeparator == '\\')
            {
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
            else
            {
                return "dotnet";
            }
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
