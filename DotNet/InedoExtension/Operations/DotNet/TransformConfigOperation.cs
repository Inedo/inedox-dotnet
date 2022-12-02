using System.ComponentModel;
using Inedo.Agents;
using Inedo.Diagnostics;
using Inedo.Documentation;
using Inedo.ExecutionEngine.Executer;
using Inedo.Extensibility;
using Inedo.Extensibility.Operations;
using Inedo.IO;
using Microsoft.VisualStudio.Jdt;
using Microsoft.Web.XmlTransform;

#nullable enable

namespace Inedo.Extensions.DotNet.Operations.DotNet
{
    [Tag(".net")]
    [ScriptNamespace("DotNet")]
    [ScriptAlias("Transform-Config")]
    [DisplayName("Transform config file")]
    [Description("Applies a transform to an XML or JSON .NET config file.")]
    public sealed class TransformConfigOperation : ExecuteOperation
    {
        [Required]
        [ScriptAlias("BaseConfig")]
        [DisplayName("Config file to transform")]
        public string? BaseConfig { get; set; }
        [Required]
        [ScriptAlias("TransformConfig")]
        [DisplayName("Transform file")]
        public string? TransformConfig { get; set; }
        [ScriptAlias("Target")]
        [DisplayName("Target")]
        [PlaceholderText("overwrite source")]
        public string? TargetFile { get; set; }
        [ScriptAlias("ConfigType")]
        [DisplayName("Config file type")]
        [DefaultValue(ConfigFileType.Auto)]
        public ConfigFileType ConfigType { get; set; }

        public override async Task ExecuteAsync(IOperationExecutionContext context)
        {
            if (string.IsNullOrWhiteSpace(this.BaseConfig))
                throw new ExecutionFailureException("Missing required value: BaseConfig");
            if (string.IsNullOrWhiteSpace(this.TransformConfig))
                throw new ExecutionFailureException("Missing required value: TransformConfig");

            var fileOps = await context.Agent.GetServiceAsync<IFileOperationsExecuter>();
            var baseConfigPath = context.ResolvePath(this.BaseConfig);
            this.LogDebug($"Base config file: {baseConfigPath}");
            if (!await fileOps.FileExistsAsync(baseConfigPath))
                throw new ExecutionFailureException($"Base config file {baseConfigPath} does not exist.");

            var transformPath = context.ResolvePath(this.TransformConfig);
            this.LogDebug($"Transform file: {transformPath}");
            if (!await fileOps.FileExistsAsync(transformPath))
                throw new ExecutionFailureException($"Transform file {transformPath} does not exist.");

            if (this.ConfigType == ConfigFileType.Auto)
            {
                var ext = Path.GetExtension(baseConfigPath);
                if (ext.Equals(".json", StringComparison.OrdinalIgnoreCase))
                    this.ConfigType = ConfigFileType.JSON;
                else if (ext.Equals(".config", StringComparison.OrdinalIgnoreCase))
                    this.ConfigType = ConfigFileType.XML;
                else
                    throw new ExecutionFailureException("Could not detect type of tranform to use. You must explictly set the ConfigType property XML or JSON.");
            }

            bool success = false;
            using var tempStream = new TemporaryStream();

            using (var sourceStream = await fileOps.OpenFileAsync(baseConfigPath, FileMode.Open, FileAccess.Read))
            {
                using var transformStream = await fileOps.OpenFileAsync(transformPath, FileMode.Open, FileAccess.Read);

                if (this.ConfigType == ConfigFileType.XML)
                {
                    using var doc = new XmlTransformableDocument();
                    using var transform = new XmlTransformation(transformStream, new XmlLogger(context.Log));

                    doc.PreserveWhitespace = true;
                    doc.Load(sourceStream);

                    if (transform.Apply(doc))
                    {
                        success = true;
                        doc.Save(tempStream);
                    }
                }
                else
                {
                    var transform = new JsonTransformation(transformStream, new JsonLogger(this));
                    using var result = transform.Apply(sourceStream);
                    await result.CopyToAsync(tempStream, context.CancellationToken);
                    success = true;
                }
            }

            if (success)
            {
                string targetFile;
                if (!string.IsNullOrWhiteSpace(this.TargetFile))
                    targetFile = context.ResolvePath(this.TargetFile);
                else
                    targetFile = baseConfigPath;

                this.LogDebug($"Writing {targetFile}...");
                using var targetStream = await fileOps.OpenFileAsync(targetFile, FileMode.Create, FileAccess.Write);
                tempStream.Position = 0;
                await tempStream.CopyToAsync(targetStream, context.CancellationToken);
            }
        }

        protected override ExtendedRichDescription GetDescription(IOperationConfiguration config)
        {
            var target = (string)(config[nameof(TargetFile)]);
            var d = new RichDescription();
            if (!string.IsNullOrWhiteSpace(target))
                d.AppendContent("to ", new DirectoryHilite(target));

            return new ExtendedRichDescription(
                new RichDescription(
                    "Transform ",
                    new DirectoryHilite(config[nameof(BaseConfig)]),
                    " config file using ",
                    new DirectoryHilite(config[nameof(TransformConfig)])
                ),
                d
            );
        }

        private sealed class XmlLogger : IXmlTransformationLogger
        {
            private readonly Stack<IScopedLog> logs = new();

            public XmlLogger(IScopedLog log) => this.logs.Push(log);

            private IScopedLog Current
            {
                get
                {
                    lock (this.logs)
                    {
                        return this.logs.Peek();
                    }
                }
            }

            public void StartSection(string message, params object[] messageArgs)
            {
                lock (this.logs)
                {
                    this.logs.Push(this.Current.CreateNestedLog(Format(message, messageArgs)));
                }
            }
            public void StartSection(MessageType type, string message, params object[] messageArgs) => this.StartSection(message, messageArgs);

            public void EndSection(string message, params object[] messageArgs)
            {
                lock (this.logs)
                {
                    if (this.logs.Count > 1)
                        this.logs.Pop().Dispose();
                }
            }
            public void EndSection(MessageType type, string message, params object[] messageArgs) => this.EndSection(message, messageArgs);

            public void LogError(string message, params object[] messageArgs) => this.Current.LogError(Format(message, messageArgs));
            public void LogError(string file, string message, params object[] messageArgs) => this.Current.LogError($"{file}: {Format(message, messageArgs)}");
            public void LogError(string file, int lineNumber, int linePosition, string message, params object[] messageArgs) => this.Current.LogError($"{file} ({lineNumber}, {linePosition}): {Format(message, messageArgs)}");
            public void LogErrorFromException(Exception ex) => this.Current.LogError(ex);
            public void LogErrorFromException(Exception ex, string file) => this.Current.LogError($"{file}: {ex.Message}", ex);
            public void LogErrorFromException(Exception ex, string file, int lineNumber, int linePosition) => this.Current.LogError($"{file} ({lineNumber}, {linePosition}): {ex.Message}", ex);

            public void LogMessage(string message, params object[] messageArgs) => this.Current.LogInformation(Format(message, messageArgs));
            public void LogMessage(MessageType type, string message, params object[] messageArgs) => this.Current.Log(type == MessageType.Normal ? MessageLevel.Information : MessageLevel.Debug, Format(message, messageArgs));

            public void LogWarning(string message, params object[] messageArgs) => this.Current.LogWarning(Format(message, messageArgs));
            public void LogWarning(string file, string message, params object[] messageArgs) => this.Current.LogWarning($"{file}: {Format(message, messageArgs)}");
            public void LogWarning(string file, int lineNumber, int linePosition, string message, params object[] messageArgs) => this.Current.LogWarning($"{file} ({lineNumber}, {linePosition}): {Format(message, messageArgs)}");

            private static string Format(string message, object[] args)
            {
                if (args == null || args.Length == 0)
                {
                    return message;
                }
                else
                {
                    try
                    {
                        return string.Format(message, args);
                    }
                    catch
                    {
                        return message;
                    }
                }
            }
        }

        private sealed class JsonLogger : IJsonTransformationLogger
        {
            private readonly ILogSink log;

            public JsonLogger(ILogSink log) => this.log = log;

            public void LogError(string message) => this.log.LogError(message);
            public void LogError(string message, string fileName, int lineNumber, int linePosition) => this.log.LogError($"{fileName} ({lineNumber}, {linePosition}): {message}");
            public void LogErrorFromException(Exception ex) => this.log.LogError(ex);
            public void LogErrorFromException(Exception ex, string fileName, int lineNumber, int linePosition) => this.log.LogError($"{fileName} ({lineNumber}, {linePosition}): {ex.Message}", ex);

            public void LogMessage(string message) => this.log.LogInformation(message);
            public void LogMessage(string message, string fileName, int lineNumber, int linePosition) => this.log.LogInformation($"{fileName} ({lineNumber}, {linePosition}): {message}");

            public void LogWarning(string message) => this.log.LogWarning(message);
            public void LogWarning(string message, string fileName) => this.log.LogWarning($"{fileName}: {message}");
            public void LogWarning(string message, string fileName, int lineNumber, int linePosition) => this.log.LogWarning($"{fileName} ({lineNumber}, {linePosition}): {message}");
        }
    }

    public enum ConfigFileType
    {
        Auto,
        XML,
        JSON
    }
}
