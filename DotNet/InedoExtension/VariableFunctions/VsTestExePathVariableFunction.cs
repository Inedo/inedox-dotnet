using System.ComponentModel;
using Inedo.Extensibility;
using Inedo.Extensibility.VariableFunctions;

namespace Inedo.Extensions.DotNet.VariableFunctions
{
    [ScriptAlias("VsTestExePath")]
    [Description("The full path to vstest.console.exe; if empty will attempt to resolve the path automatically.")]
    [Category("Server")]
    [ExtensionConfigurationVariable(Required = false)]
    public sealed class VsTestExePathVariableFunction : ScalarVariableFunction
    {
        protected override object EvaluateScalar(IVariableFunctionContext context) => string.Empty;
    }
}
