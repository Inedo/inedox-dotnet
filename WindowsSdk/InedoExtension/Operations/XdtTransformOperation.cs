﻿using System.ComponentModel;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Inedo.Agents;
using Inedo.Diagnostics;
using Inedo.Documentation;
using Inedo.Extensibility;
using Inedo.Extensibility.Operations;
using Inedo.IO;

namespace Inedo.Extensions.WindowsSdk.Operations
{
    [DisplayName("XDT Transform")]
    [ScriptAlias("XDT-Transform")]
    [Description("Performs an XDT transform on a configuration file.")]
    [Tag("windows")]
    [ScriptNamespace("Windows", PreferUnqualified = true)]
    public sealed class XdtTransformOperation : ExecuteOperation
    {
        [Required]
        [ScriptAlias("SourceFile")]
        [DisplayName("Source file")]
        [Description("The source file the transform is applied to.")]
        public string SourceFile { get; set; }
        [Required]
        [ScriptAlias("TransformFile")]
        [DisplayName("Transform file")]
        [Description("The XDT file used to transform the configuration file.")]
        public string TransformFile { get; set; }
        [ScriptAlias("DestinationFile")]
        [DisplayName("Destination file")]
        [Description("The file path for the result of the transform. If not specified, the source file will be used.")]
        public string DestinationFile { get; set; }
        [ScriptAlias("PreserveWhitespace")]
        [DisplayName("Preserve whitespace")]
        [Description("Indicates whether whitespace should be preserved in the destination file.")]
        public bool PreserveWhitespace { get; set; }
        [ScriptAlias("Verbose")]
        [DisplayName("Verbose logging")]
        public bool Verbose { get; set; }

        private static string GetCttPath(IRemoteMethodExecuter executer)
        {
            string assemblyDir = executer.InvokeFunc(GetAgentProviderAssemblyDirectory);
            return PathEx.Combine(assemblyDir, "Resources", "ctt.exe");
        }

        private static string GetAgentProviderAssemblyDirectory()
        {
            return PathEx.GetDirectoryName(typeof(XdtTransformOperation).Assembly.Location);
        }

        public async override Task ExecuteAsync(IOperationExecutionContext context)
        {
            var fileOps = context.Agent.GetService<IFileOperationsExecuter>();
            var remoteExecuter = context.Agent.GetService<IRemoteMethodExecuter>();
            
            var transformExePath = GetCttPath(remoteExecuter);

            if (!fileOps.FileExists(transformExePath))
                throw new FileNotFoundException("ctt.exe could not be found on the agent.", transformExePath);

            string arguments = this.BuildArguments(context.WorkingDirectory);

            this.LogInformation("Performing XDT transform...");

            await this.ExecuteCommandLineAsync(
                context, 
                new RemoteProcessStartInfo { FileName = transformExePath, Arguments = arguments }
            );
        }

        private string BuildArguments(string workingDir)
        {
            var buffer = new StringBuilder();
            buffer.AppendFormat("source:\"{0}\"", PathEx.Combine(workingDir, this.SourceFile));
            buffer.AppendFormat(" transform:\"{0}\"", PathEx.Combine(workingDir, this.TransformFile));
            buffer.AppendFormat(" destination:\"{0}\"", PathEx.Combine(workingDir, AH.CoalesceString(this.DestinationFile, this.SourceFile)));
            buffer.Append(" indent");
            if (this.PreserveWhitespace)
                buffer.Append(" preservewhitespace");
            if (this.Verbose)
                buffer.Append(" verbose");

            return buffer.ToString();
        }

        protected override ExtendedRichDescription GetDescription(IOperationConfiguration config)
        {
            string source = config[nameof(SourceFile)];
            string dest = config[nameof(DestinationFile)];

            var desc = new ExtendedRichDescription(
                 new RichDescription(
                     "XDT Transform ",
                     new Hilite(source)
                 ),
                 new RichDescription()
             );

            if (source != dest)
            {
                desc.LongDescription.AppendContent(
                    "to ",
                    new DirectoryHilite(dest)
                );
            }

            desc.LongDescription.AppendContent(
                new RichDescription(
                     " using ",
                     new DirectoryHilite(config[nameof(TransformFile)])
                 )
            );

            return desc;
        }
    }
}
