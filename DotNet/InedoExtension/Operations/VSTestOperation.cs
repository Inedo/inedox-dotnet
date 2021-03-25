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

namespace Inedo.Extensions.DotNet.Operations
{
    [ScriptAlias("Execute-VSTest")]
    [DisplayName("Execute VSTest Tests")]
    [Description("Runs VSTest unit tests on a specified test project, recommended for tests in VS 2012 and later.")]
    [ScriptNamespace("WindowsSDK")]
    public sealed class VSTestOperation : ExecuteOperation
    {
        [Required]
        [ScriptAlias("TestContainer")]
        [DisplayName("Test container")]
        public string TestContainer { get; set; }

        [ScriptAlias("Group")]
        [DisplayName("Test group")]
        [PlaceholderText("Unit Tests")]
        public string TestGroup { get; set; }

        [Category("Advanced")]
        [ScriptAlias("Arguments")]
        [DisplayName("Additional arguments")]
        public string AdditionalArguments { get; set; }

        [Category("Advanced")]
        [ScriptAlias("ClearExistingTestResults")]
        [DisplayName("Clear existing results")]
        [Description("When true, the test results directory will be cleared before the tests are run.")]
        public bool ClearExistingTestResults { get; set; }

        [Category("Advanced")]
        [ScriptAlias("VsTestPath")]
        [DisplayName("VSTest Path")]
        [DefaultValue("$VSTestExePath")]
        [Description(@"The path to vstest.console.exe, typically: <br /><br />"
    + @"%VSINSTALLDIR%\Common7\IDE\CommonExtensions\Microsoft\TestWindow\vstest.console.exe<br /><br />" 
    + "Leave this value blank to auto-detect the latest vstest.console.exe using vswhere.exe")]
        public string VsTestPath { get; set; }

        public override async Task ExecuteAsync(IOperationExecutionContext context)
        {
            var vsTestPath = await this.GetVsTestPathAsync(context);
            if (string.IsNullOrEmpty(vsTestPath))
                return;

            var fileOps = await context.Agent.GetServiceAsync<IFileOperationsExecuter>();

            var containerPath = context.ResolvePath(this.TestContainer);
            var sourceDirectory = PathEx.GetDirectoryName(containerPath);
            var resultsPath = PathEx.Combine(sourceDirectory, "TestResults");

            if (this.ClearExistingTestResults)
            {
                this.LogDebug($"Clearing {resultsPath} directory...");
                await fileOps.ClearDirectoryAsync(resultsPath);
            }

            await this.ExecuteCommandLineAsync(
                context,
                new RemoteProcessStartInfo
                {
                    FileName = vsTestPath,
                    Arguments = $"\"{containerPath}\" /logger:trx {this.AdditionalArguments}",
                    WorkingDirectory = sourceDirectory
                }
            );

            if (!await fileOps.DirectoryExistsAsync(resultsPath))
            {
                this.LogError("Could not find the generated \"TestResults\" directory after running unit tests at: " + sourceDirectory);
                return;
            }

            var trxFiles = (await fileOps.GetFileSystemInfosAsync(resultsPath, new MaskingContext(new[] { "*.trx" }, Enumerable.Empty<string>())))
                .OfType<SlimFileInfo>()
                .ToList();

            if (trxFiles.Count == 0)
            {
                this.LogError("There are no .trx files in the \"TestResults\" directory.");
                return;
            }

            var trxPath = trxFiles
                .Aggregate((latest, next) => next.LastWriteTimeUtc > latest.LastWriteTimeUtc ? next : latest)
                .FullName;

            await context.RecordUnitTestResultsAsync(trxPath, this.TestGroup);
        }

        protected override ExtendedRichDescription GetDescription(IOperationConfiguration config)
        {
            return new ExtendedRichDescription(
                new RichDescription(
                    "Run VSTest on ",
                    new DirectoryHilite(config[nameof(this.TestContainer)])
                )
            );
        }

        private async Task<string> GetVsTestPathAsync(IOperationExecutionContext context)
        {
            if (string.IsNullOrWhiteSpace(this.VsTestPath))
            {
                this.LogDebug("$VsTestExePath variable not configured and VsTestPath not specified, attempting to find using vswhere.exe...");
                var path = await this.FindVsTestConsoleWithVsWhereAsync(context).ConfigureAwait(false);

                if (path != null)
                {
                    this.LogDebug("Using VS test path: " + path);
                    return path;
                }

                this.LogError("Unable to find vstest.console.exe. Verify that VSTest is installed and set a $VSTestExePath variable in context to its full path.");
                return null;
            }
            else
            {
                this.LogDebug("VSTestExePath = " + this.VsTestPath);
                var fileOps = await context.Agent.GetServiceAsync<IFileOperationsExecuter>().ConfigureAwait(false);
                bool exists = await fileOps.FileExistsAsync(this.VsTestPath).ConfigureAwait(false);
                if (!exists)
                {
                    this.LogError($"The file {this.VsTestPath} does not exist. Verify that VSTest is installed.");
                    return null;
                }

                return this.VsTestPath;
            }
        }

        private async Task<string> FindVsTestConsoleWithVsWhereAsync(IOperationExecutionContext context)
        {
            var remoteMethodEx = await context.Agent.GetServiceAsync<IRemoteMethodExecuter>().ConfigureAwait(false);

            string vsWherePath = await remoteMethodEx.InvokeFuncAsync(RemoteGetVsWherePath).ConfigureAwait(false);
            string outputFile = await remoteMethodEx.InvokeFuncAsync(Path.GetTempFileName).ConfigureAwait(false);

            // vswhere.exe documentation: https://github.com/Microsoft/vswhere/wiki
            // component IDs documented here: https://docs.microsoft.com/en-us/visualstudio/install/workload-and-component-ids
            var startInfo = new RemoteProcessStartInfo
            {
                FileName = vsWherePath,
                WorkingDirectory = Path.GetDirectoryName(vsWherePath),
                Arguments = @"-products * -nologo -format xml -utf8 -latest -sort -requiresAny -requires Microsoft.VisualStudio.PackageGroup.TestTools.Core Microsoft.VisualStudio.Component.TestTools.BuildTools -find **\vstest.console.exe",
                OutputFileName = outputFile
            };

            this.LogDebug("Process: " + startInfo.FileName);
            this.LogDebug("Arguments: " + startInfo.Arguments);
            this.LogDebug("Working directory: " + startInfo.WorkingDirectory);

            await this.ExecuteCommandLineAsync(context, startInfo).ConfigureAwait(false);

            var fileOps = await context.Agent.GetServiceAsync<IFileOperationsExecuter>().ConfigureAwait(false);

            XDocument xdoc;
            using (var file = await fileOps.OpenFileAsync(outputFile, FileMode.Open, FileAccess.Read).ConfigureAwait(false))
            {
                xdoc = XDocument.Load(file);
            }

            var files = from f in xdoc.Root.Descendants("file")
                        let file = f.Value
                        select file;

            var filePath = files.FirstOrDefault();

            if (string.IsNullOrWhiteSpace(filePath))
                return null;

            return filePath;
        }

        private string RemoteGetVsWherePath()
        {
            string vsWherePath = PathEx.Combine(
                Path.GetDirectoryName(typeof(VSTestOperation).Assembly.Location),
                "vswhere.exe"
            );

            return vsWherePath;
        }
    }
}
