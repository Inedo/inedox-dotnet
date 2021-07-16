using System.ComponentModel;
using System.Threading.Tasks;
using Inedo.Agents;
using Inedo.Diagnostics;
using Inedo.Documentation;
using Inedo.Extensibility;
using Inedo.Extensibility.Credentials;
using Inedo.Extensibility.Operations;
using Inedo.Extensibility.SecureResources;
using Inedo.Extensions.Credentials;
using Inedo.Extensions.SecureResources;
using Inedo.Web;
using UsernamePasswordCredentials = Inedo.Extensions.Credentials.UsernamePasswordCredentials;

namespace Inedo.Extensions.DotNet.Operations.NuGet
{
    [ScriptNamespace("NuGet")]
    [ScriptAlias("Push-Package")]
    [DisplayName("Push NuGet Package")]
    [Description("Publishes a NuGet package file to a NuGet package source.")]
    [Note("When Username/Password are specified, it will be passed as \"Username:Password\" via the API Key.")]
    [DefaultProperty(nameof(PackagePath))]
    public sealed class PushNuGetPackageOperation : NuGetOperation
    {
        [Required]
        [ScriptAlias("FilePath")]
        [ScriptAlias("Package")]
        [DisplayName("Path to .nupkg package file name")]
        public string PackagePath { get; set; }
        [ScriptAlias("To")]
        [ScriptAlias("Source")]
        [DisplayName("Package source")]
        [SuggestableValue(typeof(SecureResourceSuggestionProvider<NuGetPackageSource>))]
        public string PackageSource { get; set; }

        [Category("Connection/Identity")]
        [ScriptAlias("FeedUrl")]
        [ScriptAlias("Url")]
        [DisplayName("NuGet server URL")]
        [PlaceholderText("Use server URL from package source")]
        public string ApiEndpointUrl { get; set; }
        [Category("Connection/Identity")]
        [ScriptAlias("UserName")]
        [DisplayName("User name")]
        [PlaceholderText("Use user name from package source")]
        public string UserName { get; set; }
        [Category("Connection/Identity")]
        [ScriptAlias("Password")]
        [DisplayName("Password")]
        [PlaceholderText("Use password from package source")]
        public string Password { get; set; }
        [Category("Connection/Identity")]
        [ScriptAlias("ApiKey")]
        [DisplayName("NuGet API Key")]
        [PlaceholderText("Use API Key from package source")]
        public string ApiKey { get; set; }

        public override async Task ExecuteAsync(IOperationExecutionContext context)
        {
            var packagePath = context.ResolvePath(this.PackagePath);
            if (string.IsNullOrEmpty(packagePath))
            {
                this.LogError("Missing required argument \"Package\".");
                return;
            }

            var fileOps = await context.Agent.GetServiceAsync<IFileOperationsExecuter>();
            if (!await fileOps.FileExistsAsync(packagePath))
            {
                this.LogError($"Package file {packagePath} not found.");
                return;
            }

            if (!string.IsNullOrEmpty(this.PackageSource))
            {
                this.LogDebug($"Using package source: {this.PackageSource}");
                
                var packageSource = (NuGetPackageSource)SecureResource.Create(this.PackageSource, (IResourceResolutionContext)context);
                if (packageSource == null)
                {
                    this.LogError($"Package source \"{this.PackageSource}\" not found, or is not a NuGetPackageSource.");
                    return;
                }
                this.ApiEndpointUrl = packageSource.ApiEndpointUrl;

                if (!string.IsNullOrEmpty(packageSource.CredentialName))
                {
                    this.LogDebug($"Using credentials: {packageSource.CredentialName}...");
                    var credentials = packageSource.GetCredentials((ICredentialResolutionContext)context);
                    if (credentials is TokenCredentials tokenCredentials)
                    {
                        this.ApiKey = AH.Unprotect(tokenCredentials.Token);
                    }
                    else if (credentials is UsernamePasswordCredentials usernameCredentials)
                    {
                        this.UserName = usernameCredentials.UserName;
                        this.Password = AH.Unprotect(usernameCredentials.Password);
                    }
                    else
                    {
                        this.LogError($"Package source \"{packageSource.CredentialName}\" not found, or is not a Token or UsernamePassword type.");
                        return;
                    }
                }
            }

            if (this.ApiEndpointUrl == null)
            {
                this.LogError($"No Url was specified to push a package to; you must either set a package source or the argument.");
                return;
            }
            if (!string.IsNullOrEmpty(this.UserName))
            {
                if (string.IsNullOrEmpty(this.Password))
                    this.LogWarning($"Username specified but password is blank.");
                
                if (string.IsNullOrEmpty(this.ApiKey))
                    this.ApiKey = this.UserName + ":" + this.Password;
                else
                    this.LogWarning($"ApiKey is specified, so Username/Password will be ignored");

            }

            var nugetInfo = await this.GetNuGetInfoAsync(context);

            this.LogInformation($"Pushing package {packagePath}...");

            if (nugetInfo.IsNuGetExe)
            {
                await this.ExecuteNuGetAsync(
                    context,
                    nugetInfo,
                    $"push \"{packagePath}\" -ApiKey \"{this.ApiKey}\" -Source \"{this.ApiEndpointUrl}\" -NonInteractive",
                    null,
                    $"push \"{packagePath}\" -ApiKey XXXXX -Source \"{this.ApiEndpointUrl}\" -NonInteractive"
                );
            }
            else
            {
                await this.ExecuteNuGetAsync(
                    context,
                    nugetInfo,
                    $"nuget push \"{packagePath}\" --api-key \"{this.ApiKey}\" --source \"{this.ApiEndpointUrl}\"",
                    null,
                    $"nuget push \"{packagePath}\" --api-key XXXXX --source \"{this.ApiEndpointUrl}\""
                );
            }
        }

        protected override ExtendedRichDescription GetDescription(IOperationConfiguration config)
        {
            return new ExtendedRichDescription(
                new RichDescription(
                    "Push NuGet package ",
                    new DirectoryHilite(config[nameof(this.PackagePath)])
                ),
                new RichDescription(
                    "to ",
                    new Hilite(config[nameof(this.PackageSource)])
                )
            );
        }
    }
}
