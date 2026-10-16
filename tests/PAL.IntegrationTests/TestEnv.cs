namespace PAL.IntegrationTests;

/// <summary>Shared environment probes and asset lookup for integration tests.</summary>
internal static class TestEnv
{
    /// <summary>
    /// Whether a usable Vulkan ICD is visible (tests must not launch the
    /// engine blind: at the pinned Filament release, engine creation
    /// without an ICD terminates the process).
    /// </summary>
    public static bool VulkanUsable(out string reason)
    {
        if (Environment.GetEnvironmentVariable("PAL_TEST_FORCE_NO_VULKAN") == "1")
        {
            reason = "forced off with PAL_TEST_FORCE_NO_VULKAN=1";
            return false;
        }
        return PAL.Internal.VulkanAvailability.IsUsable(out reason);
    }

    /// <summary>Walks up and sideways from the test binary for repo assets.</summary>
    public static string FindSiblingAsset(params string[] relative)
    {
        var dir = AppContext.BaseDirectory;
        for (int i = 0; i < 10; i++)
        {
            var candidate = Path.Combine([dir, .. relative]);
            if (File.Exists(candidate))
            {
                return candidate;
            }
            // Also check sibling directories of the current root level.
            var parent = Path.GetDirectoryName(dir.TrimEnd(Path.DirectorySeparatorChar));
            if (string.IsNullOrEmpty(parent))
            {
                break;
            }
            dir = parent;
        }
        throw new FileNotFoundException(
            $"Asset not found: {Path.Combine(relative)}. Integration tests expect the PAL " +
            "and FilamentSharp repositories checked out side by side.");
    }
}
