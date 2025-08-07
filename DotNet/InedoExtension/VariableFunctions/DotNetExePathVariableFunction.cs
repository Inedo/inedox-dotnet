using System.ComponentModel;
using Inedo.Extensibility;
using Inedo.Extensibility.VariableFunctions;

#nullable enable

namespace Inedo.Extensions.DotNet.VariableFunctions;

[Category("Server")]
[ScriptAlias("DotNetExePath")]
[Description("Full path to dotnet.exe. When this value is specified, it will become the default instance of dotnet to use for the server.")]
[ExtensionConfigurationVariable(Required = false)]
public sealed class DotNetExePathVariableFunction : ScalarVariableFunction
{
    protected override object? EvaluateScalar(IVariableFunctionContext context) => string.Empty;
}
