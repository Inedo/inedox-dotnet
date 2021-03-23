using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Xml.Linq;
using Inedo.Agents;
using Inedo.Diagnostics;
using Inedo.Documentation;
using Inedo.Extensibility;
using Inedo.Extensibility.Operations;
using Inedo.IO;

namespace Inedo.Extensions.DotNet.Operations.DotNet
{
    [Tag(".net")]
    [DisplayName("Set Project Version")]
    [ScriptAlias("Set-ProjectVersion")]
    [Description("Sets the version elements in .NET project files to a specified value.")]
    [ScriptNamespace("DotNet")]
    [SeeAlso(typeof(DotNetBuildOperation))]
    [SeeAlso(typeof(WriteAssemblyInfoVersionsOperation))]
    [Note("This operation is intended to be used when generating assembly info properties from a .NET project file. To set attributes in AssemblyInfo.cs, use DotNet::WriteAssemblyVersion.")]
    [Example(@"# Build ~\src\MyProject.csproj with Release configuration, restoring NuGet packages from the InternalNuGet source
DotNet::SetProjectVersion
(
    Version: $ReleaseNumber,
    AssemblyVersion: $ReleaseNumber.0
    FileVersion: $ReleaseNumber.$BuildNumber
);")]
    public sealed class SetProjectVersionOperation : ExecuteOperation
    {
        [ScriptAlias("FromDirectory")]
        [DisplayName("From directory")]
        [PlaceholderText("$WorkingDirectory")]
        public string SourceDirectory { get; set; }
        [Required]
        [ScriptAlias("Version")]
        [DefaultValue("$ReleaseNumber")]
        public string Version { get; set; } = "$ReleaseNumber";

        [Category("Advanced")]
        [ScriptAlias("Include")]
        [MaskingDescription]
        [DefaultValue("**.csproj")]
        public IEnumerable<string> Includes { get; set; }
        [Category("Advanced")]
        [ScriptAlias("Exclude")]
        public IEnumerable<string> Excludes { get; set; }
        [Category("Advanced")]
        [ScriptAlias("AssemblyVersion")]
        [DisplayName("Assembly version")]
        public string AssemblyVersion { get; set; }
        [Category("Advanced")]
        [ScriptAlias("FileVersion")]
        [DisplayName("File version")]
        public string FileVersion { get; set; }
        [Category("Advanced")]
        [ScriptAlias("PackageVersion")]
        [DisplayName("Package version")]
        public string PackageVersion { get; set; }

        public override async Task ExecuteAsync(IOperationExecutionContext context)
        {
            var sourcePath = context.ResolvePath(this.SourceDirectory);
            var maskingContext = new MaskingContext(this.Includes, this.Excludes);
            this.LogDebug($"Searching for files matching {maskingContext.ToString().Replace(Environment.NewLine, ", ")} in {sourcePath}...");

            var fileOps = await context.Agent.GetServiceAsync<IFileOperationsExecuter>();
            var matches = (await fileOps.GetFileSystemInfosAsync(context.ResolvePath(this.SourceDirectory), new MaskingContext(this.Includes, this.Excludes)))
                .OfType<SlimFileInfo>()
                .ToList();

            if (matches.Count == 0)
            {
                this.LogWarning("No matching files found.");
                return;
            }

            this.LogDebug($"Found {matches.Count} matching files.");

            foreach (var projectFile in matches)
            {
                try
                {
                    this.LogDebug($"Reading {projectFile.FullName}...");

                    XDocument xdoc;
                    using (var stream = await fileOps.OpenFileAsync(projectFile.FullName, FileMode.Open, FileAccess.Read))
                    {
                        xdoc = XDocument.Load(stream);
                    }

                    this.LogDebug($"{projectFile.FullName} loaded.");

                    if (xdoc.Root.Name.LocalName != "Project")
                    {
                        this.LogError($"{projectFile.FullName} is not a valid project file; root element is \"{xdoc.Root.Name.LocalName}\", expected \"Project\".");
                        continue;
                    }

                    this.LogInformation($"Setting Version in {projectFile.FullName} to {this.Version}...");
                    UpdateOrAdd(xdoc, "Version", this.Version);
                    if (!string.IsNullOrWhiteSpace(this.AssemblyVersion))
                    {
                        this.LogInformation($"Setting AssemblyVersion in {projectFile.FullName} to {this.AssemblyVersion}...");
                        UpdateOrAdd(xdoc, "AssemblyVersion", this.AssemblyVersion);
                    }

                    if (!string.IsNullOrWhiteSpace(this.FileVersion))
                    {
                        this.LogInformation($"Setting FileVersion in {projectFile.FullName} to {this.FileVersion}...");
                        UpdateOrAdd(xdoc, "FileVersion", this.FileVersion);
                    }

                    if (!string.IsNullOrWhiteSpace(this.PackageVersion))
                    {
                        this.LogInformation($"Setting PackageVersion in {projectFile.FullName} to {this.PackageVersion}...");
                        UpdateOrAdd(xdoc, "PackageVersion", this.PackageVersion);
                    }

                    this.LogDebug($"Writing {projectFile.FullName}...");

                    using (var stream = await fileOps.OpenFileAsync(projectFile.FullName, FileMode.Create, FileAccess.Write))
                    {
                        xdoc.Save(stream);
                    }

                    this.LogDebug($"{projectFile.FullName} saved.");
                }
                catch (Exception ex)
                {
                    this.LogError($"Unable to update {projectFile.FullName}: {ex.Message}");
                }
            }
        }

        protected override ExtendedRichDescription GetDescription(IOperationConfiguration config)
        {
            return new ExtendedRichDescription(
                new RichDescription(
                    "Set .NET Project Version to ",
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

        private static void UpdateOrAdd(XDocument xdoc, string name, string value)
        {
            var element = (from g in xdoc.Root.Elements("PropertyGroup")
                           from p in g.Elements()
                           where p.Name.LocalName == name
                           select p).FirstOrDefault();

            if (element != null)
            {
                element.Value = value;
            }
            else
            {
                var group = xdoc.Root.Element("PropertyGroup");
                if (group == null)
                {
                    group = new XElement("PropertyGroup");
                    xdoc.Root.Add(group);
                }

                group.Add(new XElement(name, value));
            }
        }
    }
}
