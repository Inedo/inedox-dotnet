using System;
using System.Reflection;
using System.Runtime.InteropServices;
using Inedo.Extensibility;

[assembly: AssemblyTitle("Windows SDK")]
[assembly: AssemblyDescription("Contains actions to build software targeting Windows or .NET platforms, including Build Project, MSBuild, Click Once, etc.")]

[assembly: ComVisible(false)]
[assembly: AssemblyCompany("Inedo, LLC")]
[assembly: AssemblyProduct("BuildMaster")]
[assembly: AssemblyCopyright("Copyright © 2021")]
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]
[assembly: CLSCompliant(false)]

[assembly: AppliesTo(InedoProduct.BuildMaster | InedoProduct.Otter)]
