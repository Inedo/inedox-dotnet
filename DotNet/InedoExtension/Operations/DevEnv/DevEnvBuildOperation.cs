using System.ComponentModel;
using System.Text;
using Inedo.Agents;
using Inedo.Diagnostics;
using Inedo.Documentation;
using Inedo.Extensibility;
using Inedo.Extensibility.Operations;
using Inedo.Extensions.DotNet.Operations.DotNet;
using Inedo.Extensions.DotNet.SuggestionProviders;
using Inedo.Web;

namespace Inedo.Extensions.DotNet.Operations.DevEnv
{
    [ScriptAlias("Build")]
    [Description("Runs devenv.exe (Visual Studio) to build the specified project or solution.")]
    [ScriptNamespace("DevEnv")]
    public sealed class DevEnvBuildOperation : ExecuteOperation, IVSWhereOperation
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
                this.DevEnvPath = await this.FindUsingVSWhereAsync(context, "-find **\\devenv.exe");
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

        Task<int> IVSWhereOperation.ExecuteCommandLineAsync(IOperationExecutionContext context, RemoteProcessStartInfo startInfo) => this.ExecuteCommandLineAsync(context, startInfo);
    }
}
