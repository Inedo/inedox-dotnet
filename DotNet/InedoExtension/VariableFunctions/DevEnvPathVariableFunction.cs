using System.ComponentModel;
using Inedo.Extensibility;
using Inedo.Extensibility.VariableFunctions;

namespace Inedo.Extensions.DotNet.VariableFunctions
{
    [ScriptAlias("DevEnvPath")]
    [Description(
        "Full path to devenv.exe. This is usually similar to " +
        @"C:\Program Files\Microsoft Visual Studio\2022\Enterprise\Common7\IDE\devenv.exe" +
        "If no value is supplied, the operation will use vswhere to determine the path to the latest installation of Visual Studio")]
    [Category("Server")]
    [ExtensionConfigurationVariable(Required = false)]
    public sealed class DevEnvPathVariableFunction : ScalarVariableFunction
    {
        protected override object EvaluateScalar(IVariableFunctionContext context) => string.Empty;
    }
}
