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
using Inedo.ExecutionEngine;
using Inedo.ExecutionEngine.Executer;
using Inedo.Extensibility;
using Inedo.Extensibility.Operations;
using Inedo.IO;

namespace Inedo.Extensions.DotNet.Operations.DotNet
{
    [Tag(".net"), Tag("nuget")]
    [ScriptNamespace("DotNet")]
    [ScriptAlias("Get-Dependencies")]
    [DisplayName("Get NuGet Dependencies")]
    [Description("Inspects a .NET build project/packages.config to return the required versions of NuGet package dependencies.")]
    [Example(@"# Store project dependencies in %depends map
DotNet::Get-Dependencies
(
    ProjectPath: ~\Src\MyProject,
    Dependencies => %depends
);")]
    [Example(@"# Store referenced version of Newtonsoft.Json in $jsonVersion
DotNet::Get-Dependencies
(
    ProjectPath: ~\Src\MyProject,
    PackageId: Newtonsoft.Json,
    Dependency => $jsonVersion
);")]
    public sealed class GetDependenciesOperation : ExecuteOperation
    {
        [ScriptAlias("ProjectPath")]
        [DisplayName("Project path")]
        [PlaceholderText("$WorkingDirectory")]
        public string ProjectPath { get; set; }
        [ScriptAlias("PackageId")]
        [DisplayName("Package ID")]
        [PlaceholderText("return all packages as a map")]
        public string PackageId { get; set; }
        [Output]
        [ScriptAlias("Dependencies")]
        [ScriptAlias("Dependency")]
        [Description("If PackageId is specified, the name of a text variable to receive the package version. If PackageId is not specified, the name of a map variable to received all dependency information.")]
        public RuntimeValue Dependencies { get; set; }

        public override async Task ExecuteAsync(IOperationExecutionContext context)
        {
            var fileOps = await context.Agent.GetServiceAsync<IFileOperationsExecuter>();

            var results = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            var projectPath = context.ResolvePath(this.ProjectPath);
            if (await fileOps.FileExistsAsync(projectPath))
                projectPath = PathEx.GetDirectoryName(projectPath);

            if (!await fileOps.DirectoryExistsAsync(projectPath))
                throw new ExecutionFailureException($"The specified path ({projectPath}) does not exist.");

            this.LogDebug($"Looking for project files in {projectPath}...");

            var matches = await fileOps.GetFileSystemInfosAsync(projectPath, new MaskingContext(new[] { "*.*proj", "packages.config" }, Enumerable.Empty<string>()));
            if (matches.Count == 0)
                throw new ExecutionFailureException($"Could not find any .NET project files or packages.config files in {projectPath}.");

            foreach (var m in matches)
            {
                this.LogDebug($"Analyzing {m.FullName}...");

                if (string.Equals(m.Name, "packages.config", StringComparison.OrdinalIgnoreCase))
                    addRange(await this.ParsePackagesConfigAsync(fileOps, m.FullName));
                else
                    addRange(await this.ParseProjectAsync(fileOps, m.FullName));
            }

            if (!string.IsNullOrWhiteSpace(this.PackageId))
            {
                if (!results.TryGetValue(this.PackageId, out var value))
                    throw new ExecutionFailureException($"Package {this.PackageId} is not referenced.");

                this.Dependencies = value;
            }
            else
            {
                this.Dependencies = new RuntimeValue(
                    results.ToDictionary(p => p.Key, p => new RuntimeValue(p.Value), StringComparer.OrdinalIgnoreCase)
                );
            }

            void addRange(IEnumerable<KeyValuePair<string, string>> items)
            {
                if (items != null)
                {
                    foreach (var i in items)
                        results[i.Key] = i.Value;
                }
            }
        }

        protected override ExtendedRichDescription GetDescription(IOperationConfiguration config)
        {
            var id = (string)config[nameof(PackageId)];
            if (string.IsNullOrWhiteSpace(id))
            {
                return new ExtendedRichDescription(
                    new RichDescription(
                        "Write ",
                        new DirectoryHilite(config[nameof(ProjectPath)]),
                        " NuGet dependencies"
                    ),
                    new RichDescription(
                        "to ",
                        new Hilite(config[nameof(Dependencies)])
                    )
                );
            }
            else
            {
                return new ExtendedRichDescription(
                    new RichDescription(
                        "Write version of ",
                        new Hilite(id),
                        " used by ",
                        new DirectoryHilite(config[nameof(ProjectPath)])
                    ),
                    new RichDescription(
                        "to ",
                        new Hilite(config[nameof(Dependencies)])
                    )
                );
            }
        }

        private async Task<Dictionary<string, string>> ParsePackagesConfigAsync(IFileOperationsExecuter fileOps, string filePath)
        {
            XDocument xdoc;

            using (var stream = await fileOps.OpenFileAsync(filePath, FileMode.Open, FileAccess.Read))
            {
                try
                {
                    xdoc = XDocument.Load(stream);
                }
                catch (Exception ex)
                {
                    this.LogWarning($"{filePath} is not a valid XML file: {ex.Message}");
                    return null;
                }
            }

            if (xdoc.Root?.Name.LocalName != "packages")
            {
                this.LogWarning($"{filePath} is not a valid NuGet packages.config file: missing root \"packages\" element.");
                return null;
            }

            return xdoc.Root
                .Elements("package")
                .ToDictionary(e => (string)e.Attribute("id"), e => (string)e.Attribute("version"), StringComparer.OrdinalIgnoreCase);
        }
        private async Task<Dictionary<string, string>> ParseProjectAsync(IFileOperationsExecuter fileOps, string filePath)
        {
            XDocument xdoc;

            using (var stream = await fileOps.OpenFileAsync(filePath, FileMode.Open, FileAccess.Read))
            {
                try
                {
                    xdoc = XDocument.Load(stream);
                }
                catch (Exception ex)
                {
                    this.LogWarning($"{filePath} is not a valid XML file: {ex.Message}");
                    return null;
                }
            }

            var elements = xdoc.Root
                .Descendants()
                .Where(d => d.Name.LocalName == "PackageReference");

            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var e in elements)
                result[(string)e.Attribute("Include")] = (string)e.Attribute("Version");

            return result;
        }
    }
}
