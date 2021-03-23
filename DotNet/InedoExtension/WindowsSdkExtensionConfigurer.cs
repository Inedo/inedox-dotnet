using System;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace Inedo.Extensions.DotNet
{
    internal static class WindowsSdkExtensionConfigurer
    {
        private static readonly LazyRegex VersionMatch = new LazyRegex(@"\d+\.\d+", RegexOptions.Compiled | RegexOptions.Singleline);

        /// <summary>
        /// Returns the location of the Windows SDK if it is installed.
        /// </summary>
        /// <returns>Path to the Windows SDK if it is installed; otherwise null.</returns>
        internal static string GetWindowsSdkInstallRoot()
        {
            using (var windowsKey = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Microsoft SDKs\Windows", false))
            {
                if (windowsKey == null)
                    return null;

                // Later versions of the SDK have this value, but it might not always be there.
                var installFolder = windowsKey.GetValue("CurrentInstallFolder") as string;
                if (!string.IsNullOrEmpty(installFolder))
                    return installFolder;

                var subkeys = windowsKey.GetSubKeyNames();
                if (subkeys.Length == 0)
                    return null;

                // Sort subkeys to find the highest version number.
                Array.Sort<string>(subkeys, (a, b) =>
                {
                    var aMatch = VersionMatch.Match(a);
                    var bMatch = VersionMatch.Match(b);
                    if (!aMatch.Success && !bMatch.Success)
                        return 0;
                    else if (!bMatch.Success)
                        return -1;
                    else if (!aMatch.Success)
                        return 1;
                    else
                        return -new Version(aMatch.Value).CompareTo(new Version(bMatch.Value));
                });

                using (var versionKey = windowsKey.OpenSubKey(subkeys[0], false))
                {
                    return versionKey.GetValue("InstallationFolder") as string;
                }
            }
        }
    }
}
