using System.Collections.Generic;
using System.Linq;
using Inedo.Extensions.SecureResources;

namespace Inedo.Extensions.DotNet
{
    internal static class Util
    {
        public static IEnumerable<SDK.PackageSourceInfo> GetPackageSources()
        {
            return from resource in SDK.GetSecureResources()
                   where resource.InstanceType == typeof(NuGetPackageSource)
                         || resource.InstanceType == typeof(UniversalPackageSource)
                   select new SDK.PackageSourceInfo(resource);
        }
        public static IEnumerable<SDK.ContainerSourceInfo> GetContainerSources()
        {
            return from resource in SDK.GetSecureResources()
                   where resource.InstanceType == typeof(ContainerSource)
                   select new SDK.ContainerSourceInfo(resource);
        }
    }
}
