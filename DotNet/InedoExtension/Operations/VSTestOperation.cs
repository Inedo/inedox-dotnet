﻿using System.ComponentModel;
using Inedo.Agents;
using Inedo.Diagnostics;
using Inedo.Documentation;
using Inedo.Extensibility;
using Inedo.Extensibility.Operations;
using Inedo.Extensions.DotNet.Operations.DotNet;
using Inedo.IO;

namespace Inedo.Extensions.DotNet.Operations
{
    [ScriptAlias("Execute-VSTest")]
    [DisplayName("Execute VSTest Tests")]
    [Description("Runs VSTest unit tests on a specified test project, recommended for tests in VS 2012 and later.")]
    [ScriptNamespace("WindowsSDK")]
    public sealed class VSTestOperation : ExecuteOperation, IVSWhereOperation
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
                var path = await this.FindUsingVSWhereAsync(context, @"-products * -nologo -format xml -utf8 -latest -sort -requiresAny -requires Microsoft.VisualStudio.PackageGroup.TestTools.Core Microsoft.VisualStudio.Component.TestTools.BuildTools -find **\vstest.console.exe", true).ConfigureAwait(false);

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


        Task<int> IVSWhereOperation.ExecuteCommandLineAsync(IOperationExecutionContext context, RemoteProcessStartInfo startInfo) => this.ExecuteCommandLineAsync(context, startInfo);
    }
}
