using System.ComponentModel;
using Inedo.Documentation;
using Inedo.Extensibility;
using Inedo.Extensibility.VariableFunctions;

namespace Inedo.Extensions.DotNet.VariableFunctions
{
    [Tag("nuget")]
    [ScriptAlias("NuGetExePath")]
    [Description("The path to the nuget.exe client. When not specified, the included nuget.exe client is used.")]
    [ExtensionConfigurationVariable(Required = false)]
    public sealed class NuGetExePathVariableFunction : ScalarVariableFunction
    {
        protected override object EvaluateScalar(IVariableFunctionContext context) => string.Empty;
    }
}
