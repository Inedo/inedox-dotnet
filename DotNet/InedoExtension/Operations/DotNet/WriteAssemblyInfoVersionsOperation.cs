using System.ComponentModel;
using System.Text;
using System.Text.RegularExpressions;
using Inedo.Agents;
using Inedo.Diagnostics;
using Inedo.Documentation;
using Inedo.Extensibility;
using Inedo.Extensibility.Operations;
using Inedo.IO;

namespace Inedo.Extensions.DotNet.Operations.DotNet
{
    [Tag(".net")]
    [DisplayName("Write Assembly Versions")]
    [ScriptAlias("Write-AssemblyVersion")]
    [Description("Updates AssemblyVersion, AssemblyFileVersion, and AssemblyInformationalVersion Attributes (in AssemblyInfo source files).")]
    [ScriptNamespace("DotNet")]
    [Note("This operation is intended to be used when assembly version attributes are stored directly in AssemblyInfo.cs. To set these values in the project file instead, use DotNet::SetProjectVersion.")]
    [SeeAlso(typeof(SetProjectVersionOperation))]
    public sealed partial class WriteAssemblyInfoVersionsOperation : ExecuteOperation
    {
       
        [ScriptAlias("AssemblyVersion")]
        [ScriptAlias("Version", Obsolete = true)]
        [DisplayName("Assembly version")]
        [DefaultValue("$ReleaseNumber.$PackageNumber")]
        public string Version { get; set; }
        [ScriptAlias("FileVersion")]
        [DisplayName("File version")]
        [PlaceholderText("same as AssemblyVersion")]
        public string FileVersion { get; set; }
        [ScriptAlias("InformationalVersion")]
        [DisplayName("Informational version")]
        [PlaceholderText("same as AssemblyVersion")]
        public string InformationalVersion { get; set; }

        [ScriptAlias("FromDirectory")]
        [DisplayName("From directory")]
        [PlaceholderText("$WorkingDirectory")]
        public string SourceDirectory { get; set; }

        [Category("Advanced")]
        [ScriptAlias("Include")]
        [MaskingDescription]
        [DefaultValue("**\\AssemblyInfo.cs")]
        public IEnumerable<string> Includes { get; set; }
        [Category("Advanced")]
        [ScriptAlias("Exclude")]
        public IEnumerable<string> Excludes { get; set; }

        public override async Task ExecuteAsync(IOperationExecutionContext context)
        {
            try
            {
                var dotStar = this.Version.EndsWith(".*");
                var version = dotStar ? this.Version.Substring(0, this.Version.Length - 2) : this.Version;
                new Version(version);
            }
            catch
            {
                this.LogError($"The specified version ({this.Version}) is not a valid .NET assembly version.");
                return;
            }

            this.LogInformation($"Setting assembly version attributes to {this.Version}...");

            var fileOps = context.Agent.GetService<IFileOperationsExecuter>();
            var matches = (await fileOps.GetFileSystemInfosAsync(context.ResolvePath(this.SourceDirectory), new MaskingContext(this.Includes, this.Excludes)).ConfigureAwait(false))
                .OfType<SlimFileInfo>()
                .ToList();

            if (matches.Count == 0)
            {
                this.LogWarning("No matching files found.");
                return;
            }

            foreach (var match in matches)
            {
                this.LogInformation($"Writing assembly versions attributes to {match.FullName}...");
                string text;
                Encoding encoding;

                using (var stream = await fileOps.OpenFileAsync(match.FullName, FileMode.Open, FileAccess.Read).ConfigureAwait(false))
                using (var reader = new StreamReader(stream, true))
                {
                    text = await reader.ReadToEndAsync().ConfigureAwait(false);
                    encoding = reader.CurrentEncoding;
                }

                if (AttributeRegex().IsMatch(text))
                {
                    text = AttributeRegex().Replace(text, this.GetReplacement);

                    var attr = match.Attributes;
                    if ((attr & FileAttributes.ReadOnly) != 0)
                        await fileOps.SetAttributesAsync(match.FullName, attr & ~FileAttributes.ReadOnly).ConfigureAwait(false);

                    using var stream = await fileOps.OpenFileAsync(match.FullName, FileMode.Create, FileAccess.Write).ConfigureAwait(false);
                    using var writer = new StreamWriter(stream, encoding);
                    await writer.WriteAsync(text).ConfigureAwait(false);
                }
            }
        }

        protected override ExtendedRichDescription GetDescription(IOperationConfiguration config)
        {
            return new ExtendedRichDescription(
                new RichDescription(
                    "Set AssemblyVersion Attributes to ",
                    new Hilite(config[nameof(Version)])
                ),
                new RichDescription(
                    "in ",
                    new DirectoryHilite(config[nameof(SourceDirectory)]),
                    " matching ",
                    new MaskHilite(config[nameof(Includes)], config[nameof(Excludes)])
                )
            );
        }

        private string GetReplacement(Match m)
        {
            string version;
            var attribute = m.Groups[2];

            if (attribute.Value == "File")
                version = AH.CoalesceString(this.FileVersion, this.Version);
            else if (attribute.Value == "Informational")
                version = AH.CoalesceString(this.InformationalVersion, this.Version);
            else
                version = this.Version;

            return m.Groups[1].Value + version + m.Groups[3].Value;
        }

        [GeneratedRegex(@"(?<1>(System\.Reflection\.)?Assembly(?<2>File|Informational)?Version(Attribute)?\s*\(\s*"")[^""]*(?<3>""\s*\))", RegexOptions.ExplicitCapture | RegexOptions.Compiled)]
        private static partial Regex AttributeRegex();
    }
}
