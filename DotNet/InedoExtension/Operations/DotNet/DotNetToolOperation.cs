using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Inedo.Agents;
using Inedo.Diagnostics;
using Inedo.Documentation;
using Inedo.Extensibility;
using Inedo.Extensibility.Operations;
using Inedo.Extensions.DotNet.SuggestionProviders;
using Inedo.IO;
using Inedo.Web;

namespace Inedo.Extensions.DotNet.Operations.DotNet
{
    [ScriptAlias("Tool")]
    [DisplayName("Run dotnet Tool")]
    [Description("Runs a dotnet tool, optionally ensuring that it is installed.")]
    [DefaultProperty(nameof(Command))]
    [Example(@"# Install and run the latest version of dotnetsay locally
DotNet::Tool dotnetsay
(
    Arguments: Hello World!,
    PackageId: dotnetsay
);")]
    public sealed class DotNetToolOperation : DotNetOperation
    {
        [Required]
        [ScriptAlias("Command")]
        public string Command { get; set; }
        [ScriptAlias("Arguments")]
        [DisplayName("Arguments")]
        public string CommandArguments { get; set; }
        [ScriptAlias("Global")]
        [DisplayName("Global tool")]
        public bool Global { get; set; }

        [Category("Installation")]
        [ScriptAlias("PackageId")]
        [DisplayName("Package ID")]
        public string PackageId { get; set; }
        [Category("Installation")]
        [ScriptAlias("Version")]
        [PlaceholderText("latest")]
        public string Version { get; set; }
        [Category("Installation")]
        [ScriptAlias("PackageSource")]
        [DisplayName("Package source")]
        [SuggestableValue(typeof(NuGetPackageSourceSuggestionProvider))]
        public string PackageSource { get; set; }

        public override async Task ExecuteAsync(IOperationExecutionContext context)
        {
            var dotNetPath = await this.GetDotNetExePath(context);
            if (string.IsNullOrEmpty(dotNetPath))
                return;

            int res;

            if (!string.IsNullOrWhiteSpace(this.PackageId))
            {
                var sb = new StringBuilder("tool ");

                var tools = await this.GetToolInfoAsync(dotNetPath, context, this.Global);
                var match = tools.FirstOrDefault(t => string.Equals(t.Id, this.PackageId, StringComparison.OrdinalIgnoreCase));
                if (match == null)
                {
                    // when installing local and there are no tools, a manifest may be needed, so look for one
                    if (!this.Global && tools.Count == 0)
                    {
                        if (!await HasLocalManifestAsync(await context.Agent.GetServiceAsync<IFileOperationsExecuter>(), context.WorkingDirectory))
                        {
                            res = await this.ExecuteCommandLineAsync(
                                context,
                                new RemoteProcessStartInfo
                                {
                                    FileName = dotNetPath,
                                    Arguments = "new tool-manifest",
                                    WorkingDirectory = context.WorkingDirectory
                                }
                            );

                            if (res != 0)
                            {
                                this.LogError($"dotnet exited with error: {res}");
                                return;
                            }
                        }
                    }

                    sb.Append("install");
                }
                else
                {
                    if (string.Equals(match.Version, this.Version, StringComparison.OrdinalIgnoreCase))
                    {
                        this.LogInformation($"{this.PackageId} v{match.Version} is already installed.");
                        goto Run;
                    }

                    sb.Append("update");
                }

                this.AddIdAndVersionParameters(sb);

                if (!string.IsNullOrWhiteSpace(this.PackageSource))
                {
                    var source = Util.GetPackageSources()
                        .FirstOrDefault(s => string.Equals(s.ResourceInfo.Name, this.PackageSource, StringComparison.OrdinalIgnoreCase));

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

                    sb.Append(" --add-source ");
                    sb.AppendArgument(source.FeedUrl);
                }

                res = await this.ExecuteCommandLineAsync(
                    context,
                    new RemoteProcessStartInfo
                    {
                        FileName = dotNetPath,
                        Arguments = sb.ToString(),
                        WorkingDirectory = context.WorkingDirectory
                    }
                );

                if (res != 0)
                {
                    this.LogError($"dotnet exited with error: {res}");
                    return;
                }

                this.LogInformation("Tool installed.");
            }

            Run:
            this.LogInformation($"Running dotnet command: {this.Command}");
            res = await this.ExecuteCommandLineAsync(
                context,
                new RemoteProcessStartInfo
                {
                    FileName = this.Global ? this.Command : dotNetPath,
                    Arguments = this.Global ? this.CommandArguments : $"tool run \"{this.Command}\" {this.CommandArguments}",
                    WorkingDirectory = context.WorkingDirectory
                }
            );

            if (res != 0)
                this.LogError($"dotnet exited with error: {res}");
        }

        protected override ExtendedRichDescription GetDescription(IOperationConfiguration config)
        {
            RichDescription longDesc = null;
            string packageId = config[nameof(PackageId)];
            if (!string.IsNullOrEmpty(packageId))
            {
                longDesc = new RichDescription(
                    "install ",
                    new Hilite(packageId),
                    "-",
                    new Hilite(AH.CoalesceString(config[nameof(Version)], "latest")),
                    " if necessary"
                );
            }

            return new ExtendedRichDescription(
                new RichDescription(
                    "dotnet tool ",
                    new Hilite(config[nameof(Command)]),
                    " ",
                    new Hilite(config[nameof(CommandArguments)])
                ),
                longDesc
            );
        }

        private static async Task<bool> HasLocalManifestAsync(IFileOperationsExecuter fileOps, string path)
        {
            var toolManifestPath = fileOps.CombinePath(path, ".config", "dotnet-tools.json");
            if (await fileOps.FileExistsAsync(toolManifestPath))
                return true;

            var rootPath = PathEx.GetDirectoryName(path);
            if (string.IsNullOrEmpty(rootPath))
                return false;

            return await HasLocalManifestAsync(fileOps, rootPath);
        }
        private void AddIdAndVersionParameters(StringBuilder sb)
        {
            sb.Append(' ');
            sb.Append('\"');
            sb.Append(this.PackageId);
            sb.Append('\"');

            if (!string.IsNullOrWhiteSpace(this.Version) && !this.Version.Equals("latest", StringComparison.OrdinalIgnoreCase))
            {
                if (this.Version.Equals("latest-prerelease", StringComparison.OrdinalIgnoreCase))
                    sb.Append(" --prerelease");

                sb.Append(" --version \"");
                sb.Append(this.Version);
                sb.Append('\"');
            }

            if (this.Global)
                sb.Append(" --global");
            else
                sb.Append(" --local");
        }
        private async Task<List<ToolInfo>> GetToolInfoAsync(string dotNetPath, IOperationExecutionContext context, bool global)
        {
            var remote = await context.Agent.GetServiceAsync<IRemoteProcessExecuter>();
            using var process = remote.CreateProcess(
                new RemoteProcessStartInfo
                {
                    FileName = dotNetPath,
                    Arguments = "tool list " + (global ? " --global" : " --local"),
                    WorkingDirectory = context.WorkingDirectory
                }
            );

            var tools = new List<ToolInfo>();
            bool readToolInfo = false;
            var errorLines = new List<string>();

            process.OutputDataReceived += (s, e) =>
            {
                if (!readToolInfo)
                {
                    if (e.Data.StartsWith("--"))
                        readToolInfo = true;
                }
                else
                {
                    var parts = e.Data.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 2)
                        tools.Add(new ToolInfo(parts[0], parts[1]));
                }
            };

            process.ErrorDataReceived += (s, e) => errorLines.Add(e.Data);

            await process.StartAsync(context.CancellationToken);
            await process.WaitAsync(context.CancellationToken);

            return tools;
        }

        private sealed class ToolInfo
        {
            public ToolInfo(string id, string version)
            {
                this.Id = id;
                this.Version = version;
            }

            public string Id { get; }
            public string Version { get; }
        }
    }
}
