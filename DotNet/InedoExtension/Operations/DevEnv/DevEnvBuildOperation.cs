using System.ComponentModel;
using System.Text;
using System.Xml.Linq;
using Inedo.Agents;
using Inedo.Diagnostics;
using Inedo.Documentation;
using Inedo.Extensibility;
using Inedo.Extensibility.Operations;
using Inedo.Extensions.DotNet.Operations.DotNet;
using Inedo.Extensions.DotNet.SuggestionProviders;
using Inedo.IO;
using Inedo.Web;

namespace Inedo.Extensions.DotNet.Operations.DevEnv
{
    [ScriptAlias("Build")]
    [Description("Runs devenv.exe (Visual Studio) to build the specified project or solution.")]
    [ScriptNamespace("DevEnv")]
    public sealed class DevEnvBuildOperation : ExecuteOperation
    {
        [Required]
        [ScriptAlias("ProjectFile")]
        [DisplayName("Project file")]
        [PlaceholderText("e.g. ProjectName.csproj or SolutionName.sln")]
        public string ProjectPath { get; set; }

        [Required]
        [ScriptAlias("Configuration")]
        [DefaultValue("Release")]
        [DisplayName("Configuration")]
        [SuggestableValue(typeof(BuildConfigurationSuggestionProvider))]
        public string BuildConfiguration { get; set; }

        [Category("Advanced")]
        [ScriptAlias("Arguments")]
        [DisplayName("Additional arguments")]
        [Description("Raw command line arguments to pass to devenv.exe")]
        public string AdditionalArguments { get; set; }

        [Category("Advanced")]
        [ScriptAlias("DevEnvPath")]
        [DefaultValue("$DevEnvPath")]
        [DisplayName("devenv.exe path")]
        [Description("Full path to devenv.exe. This is usually similar to " +
            @"C:\Program Files\Microsoft Visual Studio\2022\Enterprise\Common7\IDE\devenv.exe." + 
            " If no value is supplied, the operation will use vswhere to determine the path to the latest installation of Visual Studio")]
        public string DevEnvPath { get; set; }

        public override async Task ExecuteAsync(IOperationExecutionContext context)
        {
            if (string.IsNullOrEmpty(this.DevEnvPath))
            {
                this.DevEnvPath = await FindDevEnvPathUsingVSWhereAsync(context);
                if (string.IsNullOrEmpty(this.DevEnvPath))
                {
                    this.LogError("DevEnvPath is not set and could not find devenv.exe using vswhere.");
                    return;
                }
            }

            var sb = new StringBuilder();
            sb.AppendArgument(context.ResolvePath(this.ProjectPath));
            sb.Append("/build ");
            sb.AppendArgument(this.BuildConfiguration);
            sb.Append(this.AdditionalArguments);

            int result = await this.ExecuteCommandLineAsync(
                context,
                new RemoteProcessStartInfo
                {
                    FileName = this.DevEnvPath,
                    Arguments = sb.ToString(),
                }
            );

            if (result != 0)
                this.LogError($"devenv.exe returned exit code {result}.");
        }

        protected override ExtendedRichDescription GetDescription(IOperationConfiguration config)
        {
            return new(
                new("DevEnv.exe Build"),
                new(new Hilite(this.ProjectPath), " (", new Hilite(this.BuildConfiguration), ").")
            );
        }

        private async Task<string> FindDevEnvPathUsingVSWhereAsync(IOperationExecutionContext context)
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
                Arguments = @"-products * -nologo -format xml -utf8 -latest -sort -find **\devenv.exe",
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
                        select file;

            var filePath = files.FirstOrDefault();

            if (string.IsNullOrWhiteSpace(filePath))
                return null;

            return filePath;
        }
    }
}
