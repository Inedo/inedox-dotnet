using System.Xml.Linq;
using Inedo.Agents;
using Inedo.Diagnostics;
using Inedo.Extensibility.Operations;
using Inedo.Extensions.DotNet.Operations.DotNet;
using Inedo.IO;

#nullable enable

namespace Inedo.Extensions.DotNet.Operations;

internal static class OperationExtensions
{
    public static async Task<string?> FindUsingVSWhereAsync(this IVSWhereOperation operation, IOperationExecutionContext context, string args)
    {
        var fileOps = await context.Agent.GetServiceAsync<IFileOperationsExecuter>();

        if (fileOps.DirectorySeparator != '\\')
        {
            operation.LogWarning("vswhere.exe is only supported on Windows.");
            return null;
        }

        var path = fileOps.CombinePath(await fileOps.GetBaseWorkingDirectoryAsync(), ".dotnet-ext");
        if(!fileOps.DirectoryExists(path))
            await fileOps.CreateDirectoryAsync(path);
        var vsWherePath = fileOps.CombinePath(path, "vswhere.exe");
        using (var src = typeof(DotNetBuildOrPublishOperation).Assembly.GetManifestResourceStream("Inedo.Extensions.DotNet.vswhere.exe")!)
        {
            using var dest = await fileOps.OpenFileAsync(vsWherePath, FileMode.Create, FileAccess.Write);
            await src.CopyToAsync(dest, context.CancellationToken);
        }

        var outputFile = fileOps.CombinePath(path, "vswhere.out");

        // vswhere.exe documentation: https://github.com/Microsoft/vswhere/wiki
        // component IDs documented here: https://docs.microsoft.com/en-us/visualstudio/install/workload-and-component-ids
        var startInfo = new RemoteProcessStartInfo
        {
            FileName = vsWherePath,
            WorkingDirectory = PathEx.GetDirectoryName(vsWherePath),
            Arguments = $"-products * -nologo -format xml -utf8 -latest -sort {args}",
            OutputFileName = outputFile
        };

        operation.LogDebug($"Process: {startInfo.FileName}");
        operation.LogDebug($"Arguments: {startInfo.Arguments}");
        operation.LogDebug($"Working directory: {startInfo.WorkingDirectory}");

        await operation.ExecuteCommandLineAsync(context, startInfo).ConfigureAwait(false);
        string? toolPath;
        using (var outStream = await fileOps.OpenFileAsync(outputFile, FileMode.Open, FileAccess.Read))
        {
            var xdoc = await XDocument.LoadAsync(outStream, LoadOptions.None, context.CancellationToken);

            var files = from f in xdoc.Root!.Descendants("file")
                        let file = f.Value
                        // unincluse arm for now
                        where file.IndexOf("arm64", StringComparison.OrdinalIgnoreCase) < 0
                        // prefer 32-bit MSBuild
                        orderby file.IndexOf("amd64", StringComparison.OrdinalIgnoreCase) > -1 ? 1 : 0
                        select file;

            var filePath = files.FirstOrDefault();

            toolPath = string.IsNullOrWhiteSpace(filePath) ? null : PathEx.GetDirectoryName(filePath);
        }

        try
        {
            await fileOps.DeleteFileAsync(outputFile);
        }
        catch
        {
        }

        return toolPath;
    }
}
