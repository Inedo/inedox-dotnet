using System.ComponentModel;
using Inedo.Agents;
using Inedo.Diagnostics;
using Inedo.Documentation;
using Inedo.ExecutionEngine.Executer;
using Inedo.Extensibility;
using Inedo.Extensibility.Credentials;
using Inedo.Extensibility.Operations;
using Inedo.Extensions.Credentials;
using Inedo.Extensions.PackageSources;
using Inedo.Extensions.SecureResources;
using Inedo.ProGet;
using Inedo.Web;

#nullable enable

namespace Inedo.Extensions.DotNet.Operations.NuGet
{
    [ScriptNamespace("NuGet")]
    [ScriptAlias("Push-Package")]
    [DisplayName("Push NuGet Package")]
    [Description("Publishes a NuGet package file to a NuGet package source.")]
    [DefaultProperty(nameof(PackagePath))]
    public sealed class PushNuGetPackageOperation : NuGetOperation
    {
        [Required]
        [ScriptAlias("FilePath")]
        [ScriptAlias("Package")]
        [DisplayName("Package file name")]
        [PlaceholderText("example: myPackage-1.0.0.nupkg")]
        public string? PackagePath { get; set; }
        [ScriptAlias("To")]
        [ScriptAlias("Source")]
        [DisplayName("Package source")]
        [SuggestableValue(typeof(NuGetPackageSourceSuggestionProvider))]
        public string? PackageSource { get; set; }

        [Category("Connection/Identity")]
        [ScriptAlias("FeedUrl")]
        [ScriptAlias("Url")]
        [DisplayName("NuGet server URL")]
        [PlaceholderText("Use server URL from package source")]
        public string? ApiEndpointUrl { get; set; }
        [Category("Connection/Identity")]
        [ScriptAlias("UserName")]
        [DisplayName("User name")]
        [PlaceholderText("Use user name from package source")]
        public string? UserName { get; set; }
        [Category("Connection/Identity")]
        [ScriptAlias("Password")]
        [DisplayName("Password")]
        [PlaceholderText("Use password from package source")]
        public string? Password { get; set; }
        [Category("Connection/Identity")]
        [ScriptAlias("ApiKey")]
        [DisplayName("NuGet API Key")]
        [PlaceholderText("Use API Key from package source")]
        public string? ApiKey { get; set; }

        public override async Task ExecuteAsync(IOperationExecutionContext context)
        {
            var packagePath = context.ResolvePath(this.PackagePath!);
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
                await this.ResolvePackageSourceAsync(context, context.CancellationToken);

            if (string.IsNullOrEmpty(this.ApiEndpointUrl))
            {
                this.LogError("FeedUrl is required if a package source is not specified.");
                return;
            }

            var nugetInfo = await this.GetNuGetInfoAsync(context);

            this.GetSourceUrl(out var pushUrl, out var displayUrl);

            this.LogInformation($"Pushing package {packagePath}...");

            int exitCode;
            if (nugetInfo.IsNuGetExe)
            {
                var apiKeyArg = string.Empty;
                var apiKeyDisplayArg = string.Empty;
                if (!string.IsNullOrEmpty(this.ApiKey))
                {
                    apiKeyArg = $"-ApiKey \"{this.ApiKey}\" ";
                    apiKeyDisplayArg = "-ApiKey XXXXX ";
                }

                exitCode = await this.ExecuteNuGetAsync(
                    context,
                    nugetInfo,
                    $"push \"{packagePath}\" {apiKeyArg}-Source \"{pushUrl}\" -NonInteractive",
                    null,
                    $"push \"{packagePath}\" {apiKeyDisplayArg}-Source \"{displayUrl}\" -NonInteractive"
                );
                if (exitCode != 0)
                    this.LogError($"NuGet exited with code {exitCode}");
            }
            else
            {
                var apiKeyArg = string.Empty;
                var apiKeyDisplayArg = string.Empty;
                if (!string.IsNullOrEmpty(this.ApiKey))
                {
                    apiKeyArg = $" --api-key \"{this.ApiKey}\"";
                    apiKeyDisplayArg = " --api-key XXXXX";
                }


                bool packagePushedMessage = false;
                bool pathNullMessage = false;
                void PushNuGetPackageOperation_MessageLogged(object? sender, LogMessageEventArgs e)
                {
                    packagePushedMessage |= e.Message == "Your package was pushed.";
                    pathNullMessage |= e.Message == "error: Value cannot be null. (Parameter 'path2')";
                }
                this.MessageLogged += PushNuGetPackageOperation_MessageLogged;
                exitCode = await this.ExecuteNuGetAsync(
                    context,
                    nugetInfo,
                    $"nuget push \"{packagePath}\" --source \"{pushUrl}\" {apiKeyArg}",
                    null,
                    $"nuget push \"{packagePath}\" --source \"{displayUrl}\" {apiKeyDisplayArg}"
                );
                this.MessageLogged -= PushNuGetPackageOperation_MessageLogged;
                if (exitCode != 0)
                {
                    if (packagePushedMessage && pathNullMessage)
                    {
                        this.LogInformation(
                            $"Although NuGet exited with error code {exitCode} and logged an error, it also reported that the package was successfully pushed. " +
                            "This seems to be the known NuGet bug, (https://github.com/NuGet/Home/issues/10645), so the error is being ignored.");
                        exitCode = 0;
                    }
                    else
                        this.LogError($"NuGet exited with code {exitCode}");
                }
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

        private async Task ResolvePackageSourceAsync(ICredentialResolutionContext context, CancellationToken cancellationToken)
        {
            this.LogDebug($"Using package source: {this.PackageSource}");

            string? apiUrl;
            string? apiKey = null;
            string? userName = null;
            string? password = null;

            var packageSource = new PackageSourceId(this.PackageSource!);

            switch (packageSource.Format)
            {
                case PackageSourceIdFormat.SecureResource:
                    if (!context.TryGetSecureResource(packageSource.GetResourceName(), out var resource) || resource is not NuGetPackageSource nps)
                    {
                        this.LogError($"Package source \"{this.PackageSource}\" not found or is not a NuGetPackageSource.");
                        return;
                    }

                    apiUrl = nps.ApiEndpointUrl;
                    switch (nps.GetCredentials(context))
                    {
                        case TokenCredentials tc:
                            apiKey = AH.Unprotect(tc.Token);
                            break;

                        case UsernamePasswordCredentials upc:
                            if (upc.UserName == "api")
                            {
                                apiKey = AH.Unprotect(upc.Password);
                            }
                            else
                            {
                                userName = upc.UserName;
                                password = AH.Unprotect(upc.Password);
                            }
                            break;

                        case null:
                            break;

                        default:
                            throw new ExecutionFailureException($"Invalid credential type: {nps.CredentialName}");
                    }
                    break;

                case PackageSourceIdFormat.ProGetFeed:
                    if (SecureCredentials.TryCreate(packageSource.GetProGetServiceCredentialName(), context) is not ProGetServiceCredentials creds)
                        throw new ExecutionFailureException($"{this.PackageSource} is not valid.");

                    // Feed may be either NuGet v2 or v3. Make sure to use appropriate API URL.
                    apiUrl = await GetFeedEndpointUrlAsync(packageSource.GetFeedName(), creds, cancellationToken);
                    apiKey = creds.APIKey;
                    userName = creds.UserName;
                    password = creds.Password;
                    apiUrl ??= $"{creds.ServiceUrl!.TrimEnd('/')}/nuget/{Uri.EscapeDataString(packageSource.GetFeedName())}";
                    break;

                case PackageSourceIdFormat.Url:
                    apiUrl = packageSource.GetUrl();
                    break;

                default:
                    throw new NotSupportedException();
            }

            this.ApiEndpointUrl = AH.CoalesceString(this.ApiEndpointUrl, apiUrl);
            this.ApiKey = AH.CoalesceString(this.ApiKey, apiKey);
            this.UserName = AH.CoalesceString(this.UserName, userName);
            this.Password = AH.CoalesceString(this.Password, password);
        }
        private void GetSourceUrl(out string url, out string displayUrl)
        {
            if (string.IsNullOrEmpty(this.UserName))
            {
                url = this.ApiEndpointUrl!;
                displayUrl = url;
            }
            else
            {
                var builder = new UriBuilder(this.ApiEndpointUrl!)
                {
                    UserName = this.UserName,
                    Password = this.Password ?? string.Empty
                };

                url = builder.ToString();
                builder.Password = "XXXXX";
                displayUrl = builder.ToString();
            }
        }

        private static async Task<string?> GetFeedEndpointUrlAsync(string feedName, ProGetServiceCredentials credentials, CancellationToken cancellationToken)
        {
            try
            {
                var client = new ProGetClient(credentials);
                await foreach (var feed in client.GetFeedsAsync(cancellationToken).ConfigureAwait(false))
                {
                    if (string.Equals(feed.Name, feedName, StringComparison.OrdinalIgnoreCase))
                        return feed.EndpointUrl;
                }
            }
            catch
            {
            }

            return null;
        }
    }
}
