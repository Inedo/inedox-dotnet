using System.ComponentModel;
using Inedo.Documentation;
using Inedo.Extensibility;
using Inedo.Extensibility.Operations;
using Inedo.Extensions.DotNet.SuggestionProviders;
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
            @"C:\Program Files\Microsoft Visual Studio\2022\Enterprise\Common7\IDE\devenv.exe" + 
            "If no value is supplied, the operation will use vswhere to determine the path to the latest installation of Visual Studio")]
        public string DevEnvPath { get; set; }


        public override Task ExecuteAsync(IOperationExecutionContext context)
        {
            if (string.IsNullOrEmpty(DevEnvPath))
            {
                // Use vswhere.exe to find devenv
            }

            var logFile = "temp file path or guid or something";

            // Run devenv.exe with arguments /build {this.BuildConfiguration} /out {logFile}
            // warn user logs will be shown after everything is complete

            // output contents of logfile

            throw new NotImplementedException();
        }

        protected override ExtendedRichDescription GetDescription(IOperationConfiguration config)
        {
            return new(
                new("DevEnv.exe Build"),
                new(new Hilite(this.ProjectPath), " (", new Hilite(this.BuildConfiguration), ").")
            );
        }
    }
}
