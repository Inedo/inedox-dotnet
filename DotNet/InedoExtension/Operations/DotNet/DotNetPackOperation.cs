using System.ComponentModel;
using System.Text;
using Inedo.Agents;
using Inedo.Diagnostics;
using Inedo.Documentation;
using Inedo.Extensibility;
using Inedo.Extensibility.Operations;
using Inedo.Extensibility.SecureResources;
using Inedo.Extensions.DotNet.SuggestionProviders;
using Inedo.Extensions.PackageSources;
using Inedo.Extensions.SecureResources;
using Inedo.Web;

namespace Inedo.Extensions.DotNet.Operations.DotNet;

[Tag("nuget")]
[DisplayName("dotnet pack")]
[ScriptAlias("Pack")]
[ScriptNamespace("DotNet")]
[Description("Creates a NuGet package from a .net project using the dotnet pack command.")]
[SeeAlso(typeof(NuGet.CreateNuGetPackageOperation), Comments = "The NuGet::Create-Package operation works only on Windows, but can also build a package from a .nuspec file.")]
[Note("This operation works on Windows and Linux as long as dotnet is installed.")]
public sealed class DotNetPackOperation : DotNetOperation
{
    [ScriptAlias("Project")]
    [DisplayName("Project path")]
    [Description("This must be the path to either a project file, solution file, or a directory containing a project or solution file.")]
    public string ProjectPath { get; set; }
    [ScriptAlias("Configuration")]
    [SuggestableValue(typeof(BuildConfigurationSuggestionProvider))]
    public string Configuration { get; set; }
    [ScriptAlias("PackageSource")]
    [DisplayName("Package source")]
    [SuggestableValue(typeof(NuGetPackageSourceSuggestionProvider))]
    [Description("If specified, this NuGet package source will be used to restore packages when building.")]
    public string PackageSource { get; set; }
    [ScriptAlias("Output")]
    [DisplayName("Output directory")]
    [Description("The output directory to place built packages in.")]
    public string Output { get; set; }

    [Category("Package options")]
    [ScriptAlias("PackageID")]
    [DisplayName("Package ID")]
    [PlaceholderText("not set")]
    public string PackageID { get; set; }
    [Category("Package options")]
    [ScriptAlias("PackageVersion")]
    [DisplayName("Package Version")]
    [PlaceholderText("not set")]
    public string PackageVersion { get; set; }
    [Category("Package options")]
    [ScriptAlias("VersionSuffix")]
    [DisplayName("Version suffix")]
    [PlaceholderText("not set (ignored if Package Version is set)")]
    public string VersionSuffix { get; set; }
    [Category("Package options")]
    [ScriptAlias("IncludeSymbols")]
    [DisplayName("Include symbols")]
    public bool IncludeSymbols { get; set; }
    [Category("Package options")]
    [ScriptAlias("IncludeSource")]
    [DisplayName("Include source")]
    public bool IncludeSource { get; set; }

    [Category("Advanced")]
    [ScriptAlias("Verbosity")]
    [DefaultValue(DotNetVerbosityLevel.Minimal)]
    public DotNetVerbosityLevel Verbosity { get; set; } = DotNetVerbosityLevel.Minimal;

    [ScriptAlias("ForceDependencyResolution")]
    [Undisclosed]
    public bool Force { get; set; }

    public override async Task ExecuteAsync(IOperationExecutionContext context)
    {
        var projectPath = context.ResolvePath(this.ProjectPath);

        var dotNetPath = await this.GetDotNetExePath(context, projectPath);
        if (string.IsNullOrEmpty(dotNetPath))
            return;

        var args = new StringBuilder("pack ");
        args.AppendArgument(projectPath);

        void maybeAppend(string arg, string maybeValue)
        {
            if (string.IsNullOrWhiteSpace(maybeValue))
                return;
            args.Append(arg);
            args.AppendArgument(maybeValue);
        }

        maybeAppend("--configuration ", this.Configuration);
        maybeAppend("--output ", this.Output);

        maybeAppend("-p:PackageID=", this.PackageID);
        maybeAppend("-p:PackageVersion=", this.PackageVersion);
        maybeAppend("--version-suffix ", this.VersionSuffix);
        if (this.IncludeSymbols)
            args.Append("--include-symbols");
        if (this.IncludeSource)
            args.Append("--include-source");


        if (this.Verbosity != DotNetVerbosityLevel.Minimal)
        {
            args.Append("--verbosity ");
            args.AppendArgument(this.Verbosity.ToString().ToLowerInvariant());
        }
        if (this.Force)
            args.Append("--force ");

        if (!string.IsNullOrWhiteSpace(this.PackageSource))
        {
            var packageSource = new PackageSourceId(this.PackageSource!);

            if (packageSource.Format == PackageSourceIdFormat.ProGetFeed)
            {
                var source = await AhPackages.GetPackageSourceAsync(this.PackageSource, context, context.CancellationToken);
                if (source == null)
                {
                    this.LogError($"Package source \"{this.PackageSource}\" not found.");
                    return;
                }
                if (source is not INuGetPackageSource nuuget)
                {
                    this.LogError($"Package source \"{this.PackageSource}\" is a {source.GetType().Name} source; it must be a NuGet source for use with this operation.");
                    return;
                }

                args.Append("--source ");
                args.AppendArgument(nuuget.SourceUrl);
            }
            else if (packageSource.Format == PackageSourceIdFormat.SecureResource)
            {
                if (!context.TryGetSecureResource(SecureResourceType.General, packageSource.GetResourceName(), out var resource) || resource is not NuGetPackageSource nps)
                {
                    this.LogError($"Package source \"{this.PackageSource}\" not found or is not a NuGetPackageSource.");
                    return;
                }
                args.Append("--source ");
                args.AppendArgument(nps.ApiEndpointUrl);
            }
            else if (packageSource.Format == PackageSourceIdFormat.Url)
            {
                args.Append("--source ");
                args.AppendArgument(packageSource.GetUrl());
            }
        }

        if (!string.IsNullOrWhiteSpace(this.AdditionalArguments))
            args.Append(this.AdditionalArguments);

        this.LogDebug($"Ensuring working directory {context.WorkingDirectory} exists...");
        var fileOps = await context.Agent.GetServiceAsync<IFileOperationsExecuter>();
        await fileOps.CreateDirectoryAsync(context.WorkingDirectory);

        var fullArgs = args.ToString();
        this.LogDebug($"Executing dotnet {fullArgs}...");

        int res = await this.ExecuteCommandLineAsync(
            context,
            new RemoteProcessStartInfo
            {
                FileName = dotNetPath,
                Arguments = fullArgs,
                WorkingDirectory = context.WorkingDirectory
            }
        );

        this.Log(res == 0 ? MessageLevel.Debug : MessageLevel.Error, $"dotnet exit code: {res}");
    }

    protected override ExtendedRichDescription GetDescription(IOperationConfiguration config)
    {
        var extended = new RichDescription();
        var output = config[nameof(Output)];

        if (!string.IsNullOrWhiteSpace(output))
            extended.AppendContent("Output: ", new DirectoryHilite(output));

        return new ExtendedRichDescription(
            new RichDescription(
                "dotnet pack ",
                new DirectoryHilite(config[nameof(ProjectPath)])
            ),
            extended
        );
    }
}
