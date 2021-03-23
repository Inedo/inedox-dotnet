using System.ComponentModel;
using Inedo.Extensibility;
using Inedo.Extensibility.VariableFunctions;

namespace Inedo.Extensions.DotNet.VariableFunctions
{
    [ScriptAlias("DotNetExePath")]
    [Description("Full path of dotnet.exe. The default is %PROGRAMFILES%\\dotnet\\dotnet.exe.")]
    [Category("Server")]
    [ExtensionConfigurationVariable(Required = false)]
    public sealed class DotNetExePathVariableFunction : ScalarVariableFunction
    {
        protected override object EvaluateScalar(IVariableFunctionContext context) => string.Empty;
    }
}
