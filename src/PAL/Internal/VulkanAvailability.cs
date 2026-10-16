using System.Runtime.InteropServices;

namespace PAL.Internal;

/// <summary>
/// Probes Vulkan availability before Filament is asked for a Vulkan engine.
/// </summary>
/// <remarks>
/// At the pinned Filament release (v1.78.0), engine creation with the Vulkan
/// backend <b>terminates the process</b> from a worker thread when no usable
/// Vulkan ICD is visible (FilamentSharp cannot convert that into an error).
/// PAL therefore probes first: load the Vulkan loader, create an instance
/// and enumerate physical devices. Only when at least one device is
/// reported does PAL hand the backend to Filament — turning a hard abort
/// into an actionable <see cref="PALException"/>.
/// </remarks>
internal static class VulkanAvailability
{
    private const int VkSuccess = 0;
    private const int VkErrorIncompatibleDriver = -9;

    /// <summary>
    /// Checks whether a Vulkan device is reachable in this process
    /// environment (honors <c>VK_ICD_FILENAMES</c>, <c>VK_DRIVER_FILES</c>
    /// and the loader's default discovery).
    /// </summary>
    /// <param name="failureReason">
    /// When returning false, a human-readable reason (missing loader, no
    /// devices, loader error) to embed in the exception PAL throws.
    /// </param>
    public static bool IsUsable(out string failureReason)
    {
        failureReason = string.Empty;

        if (!NativeLibrary.TryLoad("libvulkan.so.1", out var library) &&
            !NativeLibrary.TryLoad("libvulkan.so", out library))
        {
            failureReason =
                "The Vulkan loader (libvulkan.so.1) is not installed. Install the Vulkan " +
                "loader package (e.g. libvulkan1 on Debian/Ubuntu) and a Vulkan ICD " +
                "(mesa-vulkan-drivers, or a vendor driver).";
            return false;
        }

        try
        {
            var createInstance = GetProcAddress<vkCreateInstanceDelegate>(
                library, "vkCreateInstance");
            if (createInstance is null)
            {
                failureReason = "The Vulkan library is loaded but vkCreateInstance is missing.";
                return false;
            }

            // A minimal, valid VkInstanceCreateInfo (sType 1 =
            // VK_STRUCTURE_TYPE_INSTANCE_CREATE_INFO, no layers/extensions,
            // no application info). The loader validates pCreateInfo itself,
            // so a null pointer would be rejected before any ICD is probed.
            var createInfo = new VkInstanceCreateInfo
            {
                SType = 1,
                PNext = IntPtr.Zero,
                Flags = 0,
                ApplicationInfo = IntPtr.Zero,
                EnabledLayerCount = 0,
                EnabledLayerNames = IntPtr.Zero,
                EnabledExtensionCount = 0,
                EnabledExtensionNames = IntPtr.Zero,
            };

            int result = createInstance(ref createInfo, IntPtr.Zero, out IntPtr instance);
            if (result != VkSuccess)
            {
                failureReason =
                    $"vkCreateInstance failed with Vulkan result {result} " +
                    $"({DescribeResult(result)}). A usable Vulkan installation is required: " +
                    "check your ICD configuration (VK_ICD_FILENAMES) or install one " +
                    "(mesa-vulkan-drivers provides the lavapipe software device).";
                return false;
            }

            try
            {
                var enumerateDevices = GetProcAddress<vkEnumeratePhysicalDevicesDelegate>(
                    library, "vkEnumeratePhysicalDevices");
                if (enumerateDevices is null)
                {
                    failureReason = "The Vulkan library is missing vkEnumeratePhysicalDevices.";
                    return false;
                }

                uint count = 0;
                result = enumerateDevices(instance, ref count, IntPtr.Zero);
                if (result != VkSuccess)
                {
                    failureReason =
                        $"vkEnumeratePhysicalDevices failed with Vulkan result {result}.";
                    return false;
                }
                if (count == 0)
                {
                    failureReason =
                        "The Vulkan loader works but no physical device (ICD) is visible. " +
                        "Install a Vulkan driver (mesa-vulkan-drivers for the lavapipe " +
                        "software device) or set VK_ICD_FILENAMES to an installed ICD " +
                        "manifest (FilamentSharp's scripts/setup-headless-vulkan.sh does " +
                        "this without root).";
                    return false;
                }
                return true;
            }
            finally
            {
                var destroyInstance = GetProcAddress<vkDestroyInstanceDelegate>(
                    library, "vkDestroyInstance");
                destroyInstance?.Invoke(instance, IntPtr.Zero);
            }
        }
        finally
        {
            // Keep the library loaded: Filament's backend will re-load it
            // anyway through its own dlopen.
        }
    }

    private static string DescribeResult(int result) => result switch
    {
        VkErrorIncompatibleDriver => "VK_ERROR_INCOMPATIBLE_DRIVER",
        -7 => "VK_ERROR_EXTENSION_NOT_PRESENT",
        -12 => "VK_ERROR_INITIALIZATION_FAILED",
        _ => "unknown",
    };

    private static T? GetProcAddress<T>(IntPtr library, string name)
        where T : Delegate
    {
        var address = NativeLibrary.GetExport(library, name);
        return address == IntPtr.Zero ? null : Marshal.GetDelegateForFunctionPointer<T>(address);
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int vkCreateInstanceDelegate(
        ref VkInstanceCreateInfo createInfo, IntPtr allocator, out IntPtr instance);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate void vkDestroyInstanceDelegate(IntPtr instance, IntPtr allocator);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int vkEnumeratePhysicalDevicesDelegate(
        IntPtr instance, ref uint count, IntPtr devices);

    /// <summary>
    /// Minimal VkInstanceCreateInfo (x64 layout matches the Vulkan ABI:
    /// natural alignment with padding between 32-bit and pointer fields).
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct VkInstanceCreateInfo
    {
        public int SType;                 // VK_STRUCTURE_TYPE_INSTANCE_CREATE_INFO
        public IntPtr PNext;
        public uint Flags;
        public IntPtr ApplicationInfo;
        public uint EnabledLayerCount;
        public IntPtr EnabledLayerNames;
        public uint EnabledExtensionCount;
        public IntPtr EnabledExtensionNames;
    }
}
