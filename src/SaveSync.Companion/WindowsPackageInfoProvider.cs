using SaveSync.Core;
using Windows.ApplicationModel;
using Windows.Management.Deployment;

namespace SaveSync.Companion;

/// <summary>Resolves install state and display names from registered Windows packages.</summary>
public sealed class WindowsPackageInfoProvider : IPackageInfoProvider
{
    private readonly Dictionary<string, PackageDetails> _byFamily;

    public WindowsPackageInfoProvider()
    {
        _byFamily = new Dictionary<string, PackageDetails>(StringComparer.OrdinalIgnoreCase);
        // Empty user SID = current user. Requires no elevation.
        foreach (var package in new PackageManager().FindPackagesForUser(string.Empty))
        {
            if (package.IsFramework || package.IsResourcePackage) continue;
            _byFamily[package.Id.FamilyName] = new PackageDetails
            {
                FamilyName = package.Id.FamilyName,
                DisplayName = SafeDisplayName(package),
                InstallState = GetInstallState(package),
            };
        }
    }

    public PackageDetails? GetPackage(string familyName) =>
        _byFamily.TryGetValue(familyName, out var d) ? d : null;

    private static InstallState GetInstallState(Package package)
    {
        var status = package.Status;
        if (status.NeedsRemediation || status.Modified || status.DataOffline || status.PackageOffline)
            return InstallState.NeedsRepair;
        return status.VerifyIsOK() ? InstallState.Installed : InstallState.NeedsRepair;
    }

    private static string? SafeDisplayName(Package package)
    {
        try { return string.IsNullOrWhiteSpace(package.DisplayName) ? null : package.DisplayName; }
        catch { return null; }
    }
}
