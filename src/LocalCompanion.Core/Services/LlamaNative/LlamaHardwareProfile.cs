using System.Diagnostics;
using System.Runtime.InteropServices;
using LocalCompanion.Localization;
using Microsoft.Win32;

namespace LocalCompanion.Services.LlamaNative;

internal readonly record struct LlamaHardwareSnapshot(
    bool IsArm64,
    bool HasNvidiaGpu,
    bool HasAmdRadeonGpu,
    bool HasQualcommAdreno,
    bool HasVulkanCapableGpu,
    bool UsesUnifiedMemory,
    long TotalPhysicalMemoryBytes,
    long DedicatedVramBytes);

/// <summary>起動時の GPU / CPU / メモリ環境を判定し、llama.cpp ビルド種別を決める。</summary>
internal static class LlamaHardwareProfile
{
    private static readonly Lazy<LlamaHardwareSnapshot> Cached = new(Capture);

    internal static LlamaHardwareSnapshot Current => Cached.Value;

    internal static string ResolvePreferredVariant(LlamaHardwareSnapshot? snapshot = null)
    {
        var hw = snapshot ?? Current;
        if (hw.IsArm64)
        {
            if (hw.HasQualcommAdreno)
                return "opencl-adreno";
            return "cpu";
        }

        if (hw.HasNvidiaGpu)
            return "cuda";
        if (hw.HasAmdRadeonGpu)
            return "hip-radeon";
        if (hw.HasVulkanCapableGpu)
            return "vulkan";
        return "cpu";
    }

    internal static IReadOnlyList<string> BuildInstallFallbackChain(string primary, LlamaHardwareSnapshot hw)
    {
        var chain = new List<string>();
        void Add(string v)
        {
            if (!chain.Contains(v, StringComparer.OrdinalIgnoreCase))
                chain.Add(v);
        }

        Add(primary);
        if (hw.IsArm64)
        {
            if (!string.Equals(primary, "cpu", StringComparison.OrdinalIgnoreCase))
                Add("cpu");
            return chain;
        }

        if (string.Equals(primary, "cuda", StringComparison.OrdinalIgnoreCase))
        {
            if (hw.HasVulkanCapableGpu || hw.HasAmdRadeonGpu)
                Add("vulkan");
            Add("cpu");
            return chain;
        }

        if (string.Equals(primary, "hip-radeon", StringComparison.OrdinalIgnoreCase))
        {
            Add("vulkan");
            Add("cpu");
            return chain;
        }

        if (string.Equals(primary, "vulkan", StringComparison.OrdinalIgnoreCase))
        {
            Add("cpu");
            return chain;
        }

        Add("cpu");
        return chain;
    }

    internal static string GetCpuZipArchSuffix(LlamaHardwareSnapshot hw) =>
        hw.IsArm64 ? "arm64" : "x64";

    internal static string DescribeVariant(string variant)
    {
        var loc = LocalizationService.Instance;
        return variant switch
        {
            "cuda" => loc.Get("Hardware.Variant.Cuda"),
            "hip-radeon" => loc.Get("Hardware.Variant.HipRadeon"),
            "vulkan" => loc.Get("Hardware.Variant.Vulkan"),
            "opencl-adreno" => loc.Get("Hardware.Variant.OpenClAdreno"),
            _ => loc.Get("Hardware.Variant.Cpu"),
        };
    }

    private static LlamaHardwareSnapshot Capture()
    {
        var adapters = ReadDisplayAdapters();
        var names = adapters.Select(a => a.Name).ToList();
        var isArm64 = RuntimeInformation.ProcessArchitecture is Architecture.Arm64 or Architecture.Arm;
        var hasNvidiaSmi = HasNvidiaGpu();
        var hasNvidia = hasNvidiaSmi || names.Any(IsNvidiaName);
        var hasAmd = names.Any(IsAmdRadeonName);
        var hasAdreno = isArm64 && (HasQualcommProcessor() || names.Any(IsAdrenoName));
        var hasVulkan = names.Any(a => IsVulkanCapableName(a) && !IsMicrosoftBasicAdapter(a));
        var nvidiaVram = hasNvidiaSmi ? ReadNvidiaVramBytes() : 0;
        var registryVram = adapters
            .Where(a => !IsMicrosoftBasicAdapter(a.Name))
            .Select(a => a.VramBytes)
            .DefaultIfEmpty(0)
            .Max();
        var dedicatedVram = Math.Max(nvidiaVram, registryVram);
        var usesUnified = isArm64
            || (hasAdreno && dedicatedVram < 4L * 1024 * 1024 * 1024)
            || (!hasNvidia && dedicatedVram < 2L * 1024 * 1024 * 1024 && names.All(a => IsMicrosoftBasicAdapter(a) || !IsDiscreteGpuName(a)));

        return new LlamaHardwareSnapshot(
            isArm64,
            hasNvidia,
            hasAmd,
            hasAdreno,
            hasVulkan,
            usesUnified,
            ReadTotalPhysicalMemoryBytes(),
            dedicatedVram);
    }

    private static bool HasNvidiaGpu()
    {
        try
        {
            var psi = new ProcessStartInfo("nvidia-smi", "-L")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var proc = Process.Start(psi);
            if (proc is null)
                return false;
            proc.WaitForExit(3000);
            return proc.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    private static bool HasQualcommProcessor()
    {
        var id = Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER") ?? "";
        return id.Contains("Qualcomm", StringComparison.OrdinalIgnoreCase)
            || id.Contains("Snapdragon", StringComparison.OrdinalIgnoreCase);
    }

    private static List<(string Name, long VramBytes)> ReadDisplayAdapters()
    {
        var adapters = new List<(string Name, long VramBytes)>();
        try
        {
            const string keyPath = @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}";
            using var baseKey = Registry.LocalMachine.OpenSubKey(keyPath);
            if (baseKey is null)
                return adapters;

            foreach (var subName in baseKey.GetSubKeyNames())
            {
                using var sub = baseKey.OpenSubKey(subName);
                var desc = sub?.GetValue("DriverDesc")?.ToString();
                if (string.IsNullOrWhiteSpace(desc))
                    continue;

                var vram = ReadAdapterVramBytes(sub);
                adapters.Add((desc, vram));
            }
        }
        catch
        {
            /* ignore */
        }

        return adapters;
    }

    private static long ReadAdapterVramBytes(RegistryKey? sub)
    {
        if (sub is null)
            return 0;

        foreach (var valueName in new[] { "HardwareInformation.qwMemorySize", "HardwareInformation.MemorySize" })
        {
            var raw = sub.GetValue(valueName);
            switch (raw)
            {
                case long l when l > 0:
                    return l;
                case int i when i > 0:
                    return i;
                case byte[] bytes when bytes.Length >= 8:
                    return BitConverter.ToInt64(bytes, 0);
            }
        }

        var adapterRam = sub.GetValue("AdapterRAM");
        return adapterRam switch
        {
            long l when l > 0 => l,
            int i when i > 0 => i,
            _ => 0,
        };
    }

    private static long ReadNvidiaVramBytes()
    {
        try
        {
            var psi = new ProcessStartInfo("nvidia-smi",
                "--query-gpu=memory.total --format=csv,noheader,nounits")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var proc = Process.Start(psi);
            if (proc is null)
                return 0;
            var line = proc.StandardOutput.ReadLine()?.Trim();
            proc.WaitForExit(3000);
            if (proc.ExitCode != 0 || string.IsNullOrWhiteSpace(line))
                return 0;
            if (int.TryParse(line.Split('.')[0], out var mib) && mib > 0)
                return (long)mib * 1024 * 1024;
        }
        catch
        {
            /* ignore */
        }

        return 0;
    }

    private static bool IsDiscreteGpuName(string name) =>
        IsNvidiaName(name) || IsAmdRadeonName(name) || name.Contains("Arc", StringComparison.OrdinalIgnoreCase);

    private static long ReadTotalPhysicalMemoryBytes()
    {
        var status = new MemoryStatusEx { dwLength = (uint)Marshal.SizeOf<MemoryStatusEx>() };
        return GlobalMemoryStatusEx(ref status) ? (long)status.ullTotalPhys : 0;
    }

    private static bool IsNvidiaName(string name) =>
        name.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase)
        || name.Contains("GeForce", StringComparison.OrdinalIgnoreCase)
        || name.Contains("RTX", StringComparison.OrdinalIgnoreCase)
        || name.Contains("GTX", StringComparison.OrdinalIgnoreCase);

    private static bool IsAmdRadeonName(string name) =>
        name.Contains("AMD", StringComparison.OrdinalIgnoreCase)
        || name.Contains("Radeon", StringComparison.OrdinalIgnoreCase)
        || name.Contains("ATI", StringComparison.OrdinalIgnoreCase);

    private static bool IsAdrenoName(string name) =>
        name.Contains("Adreno", StringComparison.OrdinalIgnoreCase)
        || name.Contains("Qualcomm", StringComparison.OrdinalIgnoreCase);

    private static bool IsVulkanCapableName(string name)
    {
        if (IsMicrosoftBasicAdapter(name))
            return false;
        return IsNvidiaName(name)
            || IsAmdRadeonName(name)
            || name.Contains("Intel", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Arc", StringComparison.OrdinalIgnoreCase)
            || IsAdrenoName(name);
    }

    private static bool IsMicrosoftBasicAdapter(string name) =>
        name.Contains("Microsoft Basic Display", StringComparison.OrdinalIgnoreCase)
        || name.Contains("Microsoft Remote Display", StringComparison.OrdinalIgnoreCase);

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatusEx
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx lpBuffer);
}
