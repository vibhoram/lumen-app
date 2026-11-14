// Lumen (personal fork) - system vitals: RAM, CPU, GPU, network, the apps using the most memory, and a RAM tidy-up.
//
// Design: sampling is cheap (a few API calls) and only runs while the Vitals page is open or the RAM alert is on.
// The "top apps" list walks the process table, so it runs only for the open page. Clearing RAM never closes anything:
// it asks Windows to trim working sets (the memory comes back on demand, nothing is lost), and at the strongest level
// also empties the standby/modified lists, which needs administrator rights (we say so instead of failing silently).

using System.ComponentModel;
using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;

namespace DIsland.Services;

public enum RamCleanMode { Normal, More, Maximum }

public sealed record TopApp(string Name, int Count, double GigaBytes);

public sealed record VitalsSnapshot(
    double RamPercent, double RamUsedGb, double RamTotalGb,
    double CpuPercent, double? GpuPercent, double VramUsedGb,
    double NetDownBytes, double NetUpBytes, int RefreshHz);

public sealed record CleanResult(double FreedGb, string Note);

public sealed class VitalsService : IDisposable
{
    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatusEx
    {
        public uint Length, MemoryLoad;
        public ulong TotalPhys, AvailPhys, TotalPageFile, AvailPageFile, TotalVirtual, AvailVirtual, AvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx status);
    [DllImport("kernel32.dll")] private static extern bool GetSystemTimes(out long idle, out long kernel, out long user);
    [DllImport("psapi.dll", SetLastError = true)] private static extern bool EmptyWorkingSet(IntPtr process);
    [DllImport("ntdll.dll")] private static extern int NtSetSystemInformation(int infoClass, ref int info, int length);
    [DllImport("advapi32.dll", SetLastError = true)] private static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);
    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)] private static extern bool LookupPrivilegeValue(string? system, string name, out long luid);
    [DllImport("advapi32.dll", SetLastError = true)] private static extern bool AdjustTokenPrivileges(IntPtr token, bool disableAll, ref TokenPrivileges state, int length, IntPtr prev, IntPtr ret);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
    [DllImport("user32.dll")] private static extern bool EnumDisplaySettings(string? device, int mode, ref DevMode dm);

    [StructLayout(LayoutKind.Sequential)]
    private struct TokenPrivileges { public int Count; public long Luid; public int Attributes; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    private struct DevMode
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
        public short SpecVersion, DriverVersion, Size, DriverExtra;
        public int Fields, PositionX, PositionY, DisplayOrientation, DisplayFixedOutput;
        public short Color, Duplex, YResolution, TTOption, Collate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string FormName;
        public short LogPixels;
        public int BitsPerPel, PelsWidth, PelsHeight, DisplayFlags, DisplayFrequency;
        public int ICMMethod, ICMIntent, MediaType, DitherType, Reserved1, Reserved2, PanningWidth, PanningHeight;
    }

    // names that must never be trimmed
    private static readonly HashSet<string> Protected = new(StringComparer.OrdinalIgnoreCase)
    {
        "System", "Idle", "Registry", "Memory Compression", "csrss", "wininit", "winlogon", "smss", "services", "lsass", "dwm", "fontdrvhost",
    };

    private long _lastIdle, _lastKernel, _lastUser;
    private double _cpu;
    private long _lastRx, _lastTx;
    private DateTime _lastNet = DateTime.MinValue;
    private double _down, _up;
    private System.Diagnostics.PerformanceCounter[]? _gpuCounters;
    private DateTime _gpuRefreshed = DateTime.MinValue;
    private bool _gpuUnavailable;
    private int _hz;

    public VitalsService()
    {
        GetSystemTimes(out _lastIdle, out _lastKernel, out _lastUser);
        try
        {
            var dm = new DevMode { Size = (short)Marshal.SizeOf<DevMode>() };
            if (EnumDisplaySettings(null, -1, ref dm)) _hz = dm.DisplayFrequency;
        }
        catch (Exception) { }
    }

    public static (double Percent, double UsedGb, double TotalGb) Ram()
    {
        var m = new MemoryStatusEx { Length = (uint)Marshal.SizeOf<MemoryStatusEx>() };
        if (!GlobalMemoryStatusEx(ref m) || m.TotalPhys == 0) return (0, 0, 0);
        var total = m.TotalPhys / 1073741824.0;
        var used = (m.TotalPhys - m.AvailPhys) / 1073741824.0;
        return (used / total * 100, used, total);
    }

    /// <summary>One reading of everything cheap. Call every second or two.</summary>
    public VitalsSnapshot Sample()
    {
        var (percent, used, total) = Ram();

        if (GetSystemTimes(out var idle, out var kernel, out var user))
        {
            var dIdle = idle - _lastIdle;
            var dTotal = (kernel - _lastKernel) + (user - _lastUser); // kernel time includes idle
            if (dTotal > 0) _cpu = Math.Clamp((1 - (double)dIdle / dTotal) * 100, 0, 100);
            (_lastIdle, _lastKernel, _lastUser) = (idle, kernel, user);
        }

        SampleNetwork();
        var (gpu, vram) = SampleGpu();
        return new VitalsSnapshot(percent, used, total, _cpu, gpu, vram, _down, _up, _hz);
    }

    private void SampleNetwork()
    {
        try
        {
            long rx = 0, tx = 0;
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up || nic.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel) continue;
                var s = nic.GetIPv4Statistics();
                rx += s.BytesReceived;
                tx += s.BytesSent;
            }
            var now = DateTime.UtcNow;
            if (_lastNet != DateTime.MinValue)
            {
                var secs = Math.Max(0.2, (now - _lastNet).TotalSeconds);
                _down = Math.Max(0, (rx - _lastRx) / secs);
                _up = Math.Max(0, (tx - _lastTx) / secs);
            }
            (_lastRx, _lastTx, _lastNet) = (rx, tx, now);
        }
        catch (Exception) { }
    }

    private (double? Percent, double VramGb) SampleGpu()
    {
        if (_gpuUnavailable) return (null, 0);
        try
        {
            // the "GPU Engine" instance list changes as apps start and stop: refresh it every 20 s
            if (_gpuCounters is null || (DateTime.UtcNow - _gpuRefreshed).TotalSeconds > 20)
            {
                if (_gpuCounters is not null) foreach (var c in _gpuCounters) c.Dispose();
                var category = new PerformanceCounterCategory("GPU Engine");
                _gpuCounters = category.GetInstanceNames()
                    .Where(n => n.Contains("engtype_3D", StringComparison.OrdinalIgnoreCase) || n.Contains("engtype_Compute", StringComparison.OrdinalIgnoreCase))
                    .Take(120)
                    .Select(n => new System.Diagnostics.PerformanceCounter("GPU Engine", "Utilization Percentage", n, true))
                    .ToArray();
                foreach (var c in _gpuCounters) { try { c.NextValue(); } catch (Exception) { } }
                _gpuRefreshed = DateTime.UtcNow;
                return (null, 0);
            }
            double sum = 0;
            foreach (var c in _gpuCounters) { try { sum += c.NextValue(); } catch (Exception) { } }
            return (Math.Clamp(sum, 0, 100), 0);
        }
        catch (Exception)
        {
            _gpuUnavailable = true; // no GPU counters on this PC
            return (null, 0);
        }
    }

    /// <summary>Apps grouped by name (a browser's many processes count as one), biggest first.</summary>
    public static List<TopApp> TopApps(int count)
    {
        var groups = new Dictionary<string, (int N, long Bytes)>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in Process.GetProcesses())
        {
            try
            {
                if (p.Id <= 4) continue;
                var name = p.ProcessName;
                var bytes = p.WorkingSet64;
                groups[name] = groups.TryGetValue(name, out var g) ? (g.N + 1, g.Bytes + bytes) : (1, bytes);
            }
            catch (Exception) { }
            finally { p.Dispose(); }
        }
        return groups.OrderByDescending(g => g.Value.Bytes).Take(count)
            .Select(g => new TopApp(Pretty(g.Key), g.Value.N, g.Value.Bytes / 1073741824.0)).ToList();
    }

    private static string Pretty(string process)
    {
        var name = process.Replace("_", " ");
        return name.Length == 0 ? process : char.ToUpperInvariant(name[0]) + name[1..];
    }

    /// <summary>The biggest memory user, for the "RAM is almost full" alert.</summary>
    public static string? BiggestApp()
    {
        var top = TopApps(1);
        return top.Count == 0 ? null : $"{top[0].Name} uses {top[0].GigaBytes:0.0} GB";
    }

    // ------------------------------------------------------------------ clearing RAM

    /// <summary>Trims memory at the chosen strength. Nothing is closed; Windows hands pages back as apps need them.</summary>
    public static async Task<CleanResult> CleanAsync(RamCleanMode mode)
    {
        return await Task.Run(() =>
        {
            var before = AvailableGb();
            var self = Environment.ProcessId;
            var trimmed = 0;
            foreach (var p in Process.GetProcesses())
            {
                try
                {
                    if (p.Id == self || p.Id <= 4 || Protected.Contains(p.ProcessName)) continue;
                    if (mode == RamCleanMode.Normal && p.SessionId == 0) continue; // user apps only
                    if (EmptyWorkingSet(p.Handle)) trimmed++;
                }
                catch (Exception) { /* protected or already gone */ }
                finally { p.Dispose(); }
            }

            var notes = new List<string>();
            if (mode >= RamCleanMode.More)
            {
                GC.Collect(2, GCCollectionMode.Forced, true);
                if (EnablePrivilege("SeProfileSingleProcessPrivilege") && EnablePrivilege("SeIncreaseQuotaPrivilege"))
                {
                    MemoryCommand(2); // empty every working set system-wide
                    if (mode == RamCleanMode.Maximum)
                    {
                        MemoryCommand(3); // write modified pages out
                        MemoryCommand(4); // purge the standby list
                    }
                }
                else notes.Add("run Lumen as administrator for the deepest clean");
            }

            Thread.Sleep(700); // let Windows settle before measuring
            var freed = Math.Max(0, AvailableGb() - before);
            var note = $"Trimmed {trimmed} apps" + (notes.Count > 0 ? ", " + notes[0] : "");
            return new CleanResult(freed, note);
        });
    }

    private static double AvailableGb()
    {
        var m = new MemoryStatusEx { Length = (uint)Marshal.SizeOf<MemoryStatusEx>() };
        GlobalMemoryStatusEx(ref m);
        return m.AvailPhys / 1073741824.0;
    }

    private static void MemoryCommand(int command)
    {
        try { var c = command; NtSetSystemInformation(80 /* SystemMemoryListInformation */, ref c, sizeof(int)); } catch (Exception) { }
    }

    private static bool EnablePrivilege(string name)
    {
        try
        {
            if (!OpenProcessToken(Process.GetCurrentProcess().Handle, 0x28 /* ADJUST | QUERY */, out var token)) return false;
            try
            {
                if (!LookupPrivilegeValue(null, name, out var luid)) return false;
                var state = new TokenPrivileges { Count = 1, Luid = luid, Attributes = 2 /* ENABLED */ };
                AdjustTokenPrivileges(token, false, ref state, 0, IntPtr.Zero, IntPtr.Zero);
                return Marshal.GetLastWin32Error() == 0; // 1300 = not all assigned (not an admin)
            }
            finally { CloseHandle(token); }
        }
        catch (Exception) { return false; }
    }

    public void Dispose()
    {
        if (_gpuCounters is not null) foreach (var c in _gpuCounters) c.Dispose();
        _gpuCounters = null;
    }
}
