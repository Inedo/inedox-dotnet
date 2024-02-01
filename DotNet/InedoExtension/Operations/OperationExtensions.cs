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
        try
        {
            var executer = await context.Agent.GetServiceAsync<IRemoteMethodExecuter>().ConfigureAwait(false);
            var assemblyDir = await executer.InvokeFuncAsync(() => PathEx.GetDirectoryName(typeof(OperationExtensions).Assembly.Location)).ConfigureAwait(false);
            if (assemblyDir == null)
                return null;

            var vsWherePath =  PathEx.Combine(assemblyDir, "vshwere.exe");
            var outputFile = await executer.InvokeFuncAsync(Path.GetTempFileName).ConfigureAwait(false);

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
        catch (Exception ex)
        {
            operation.LogWarning($"Failed to find tool by running vswhere.exe: {ex.Message}", ex.ToString());
            return null;
        }
    }
}
