using Inedo.Agents;
using Inedo.Diagnostics;
using Inedo.Extensibility.Operations;

#nullable enable

namespace Inedo.Extensions.DotNet.Operations.DotNet;

internal interface IVSWhereOperation : ILogSink
{
    Task<int> ExecuteCommandLineAsync(IOperationExecutionContext context, RemoteProcessStartInfo startInfo);
}
