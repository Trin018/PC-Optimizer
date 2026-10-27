using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Management;
using LibreHardwareMonitor.Hardware;

namespace PCOptimizer
{
    public class DiskRingInfo
    {
        public string DriveLetter { get; set; }
        public string VolumeLabel { get; set; }
        public double TotalGB { get; set; }
        public double FreeGB { get; set; }
        public double UsedPercent { get; set; }
    }

    public class HardwareSnapshot
    {
        public string CpuName { get; set; } = "Processador";
        public float CpuTemp { get; set; }
        public float CpuLoad { get; set; }
        public float CpuPowerWatts { get; set; }
        public float CpuClockMhz { get; set; }

        public string GpuName { get; set; } = "Placa Gráfica";
        public float GpuTemp { get; set; }
        public float GpuHotspot { get; set; }
        public float GpuLoad { get; set; }
        public float GpuPowerWatts { get; set; }
        public float GpuFanRpm { get; set; }
        public float GpuFanPercent { get; set; }
        public float GpuCoreClock { get; set; }
        public float GpuMemClock { get; set; }
        public float GpuVramUsedMb { get; set; }
        public float GpuVramTotalMb { get; set; }

        public float RamAvailableMb { get; set; }
        public string MotherboardName { get; set; } = "Placa-Mãe";
        public List<string> FanReadings { get; set; } = new List<string>();
    }

    public class HardwareMonitorService : IDisposable
    {
        private Computer _computer;
        private bool _initialized = false;

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential, CharSet = System.Runtime.InteropServices.CharSet.Auto)]
        private class MEMORYSTATUSEX
        {
            public uint dwLength = (uint)System.Runtime.InteropServices.Marshal.SizeOf(typeof(MEMORYSTATUSEX));
            public uint dwMemoryLoad;
            public ulong ullTotalPhys;
            public ulong ullAvailPhys;
            public ulong ullTotalPageFile;
            public ulong ullAvailPageFile;
            public ulong ullTotalVirtual;
            public ulong ullAvailVirtual;
            public ulong ullAvailExtendedVirtual;
        }

        [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Auto, SetLastError = true)]
        private static extern bool GlobalMemoryStatusEx([System.Runtime.InteropServices.In, System.Runtime.InteropServices.Out] MEMORYSTATUSEX lpBuffer);

        public void Initialize()
        {
            if (_initialized) return;
            try
            {
                _computer = new Computer
                {
                    IsCpuEnabled = true,
                    IsGpuEnabled = true,
                    IsMemoryEnabled = true,
                    IsMotherboardEnabled = true,
                    IsControllerEnabled = true,
                    IsStorageEnabled = false
                };
                _computer.Open();
                _initialized = true;
            }
            catch { }
        }

        public HardwareSnapshot ReadSensors()
        {
            var snap = new HardwareSnapshot();

            // 1. Ler RAM Livre Nativamente via Kernel32 (Instantâneo e nunca falha em nenhum idioma!)
            try
            {
                MEMORYSTATUSEX mem = new MEMORYSTATUSEX();
                if (GlobalMemoryStatusEx(mem))
                {
                    snap.RamAvailableMb = mem.ullAvailPhys / (1024f * 1024f);
                }
            }
            catch { }

            // 2. Ler Sensores via LibreHardwareMonitor
            if (_initialized && _computer != null)
            {
                try
                {
                    foreach (IHardware hw in _computer.Hardware)
                    {
                        hw.Update();

                        if (hw.HardwareType == HardwareType.Motherboard)
                        {
                            snap.MotherboardName = hw.Name;
                            foreach (IHardware sub in hw.SubHardware)
                            {
                                sub.Update();
                                foreach (ISensor s in sub.Sensors)
                                {
                                    if (s.SensorType == SensorType.Fan && s.Value.HasValue && s.Value.Value > 0)
                                        snap.FanReadings.Add($"{s.Name}: {s.Value.Value:0} RPM");
                                }
                            }
                        }
                        else if (hw.HardwareType == HardwareType.Cpu)
                        {
                            snap.CpuName = hw.Name;
                            foreach (ISensor s in hw.Sensors)
                            {
                                if (!s.Value.HasValue) continue;
                                float val = s.Value.Value;

                                if (s.SensorType == SensorType.Temperature && val > 0 && val < 115)
                                {
                                    if (s.Name.Contains("Package") || s.Name.Contains("Tctl") || s.Name.Contains("Average") || s.Name.Contains("Core") || snap.CpuTemp == 0)
                                        snap.CpuTemp = val;
                                }
                                else if (s.SensorType == SensorType.Load && s.Name.Contains("Total"))
                                    snap.CpuLoad = val;
                                else if (s.SensorType == SensorType.Power && (s.Name.Contains("Package") || s.Name.Contains("Cores")))
                                    if (val > snap.CpuPowerWatts) snap.CpuPowerWatts = val;
                                else if (s.SensorType == SensorType.Clock && s.Name.Contains("Core"))
                                    if (val > snap.CpuClockMhz) snap.CpuClockMhz = val;
                            }
                        }
                        else if (hw.HardwareType == HardwareType.GpuNvidia || hw.HardwareType == HardwareType.GpuAmd || hw.HardwareType == HardwareType.GpuIntel)
                        {
                            snap.GpuName = hw.Name;
                            foreach (ISensor s in hw.Sensors)
                            {
                                if (!s.Value.HasValue) continue;
                                float val = s.Value.Value;

                                if (s.SensorType == SensorType.Temperature && val > 0)
                                {
                                    if (s.Name.Contains("Hot Spot")) snap.GpuHotspot = val;
                                    else if (s.Name.Contains("Core") || snap.GpuTemp == 0) snap.GpuTemp = val;
                                }
                                else if (s.SensorType == SensorType.Load && s.Name.Contains("Core"))
                                    snap.GpuLoad = val;
                                else if (s.SensorType == SensorType.Power && val > snap.GpuPowerWatts)
                                    snap.GpuPowerWatts = val;
                                else if (s.SensorType == SensorType.Fan && val > snap.GpuFanRpm)
                                    snap.GpuFanRpm = val;
                                else if (s.SensorType == SensorType.Control && s.Name.Contains("Fan"))
                                    snap.GpuFanPercent = val;
                                else if (s.SensorType == SensorType.Clock && s.Name.Contains("Core"))
                                    snap.GpuCoreClock = val;
                            }
                        }
                    }
                }
                catch { }
            }

            // 3. Fallback Direto NVIDIA-SMI (Suporte 100% Garantido para RTX 5080 / Série 40 / 30!)
            if (snap.GpuTemp == 0 || snap.GpuPowerWatts == 0)
            {
                try
                {
                    ProcessStartInfo psi = new ProcessStartInfo("nvidia-smi", "--query-gpu=name,temperature.gpu,fan.speed,power.draw,utilization.gpu,clocks.current.graphics,memory.used,memory.total --format=csv,noheader,nounits")
                    {
                        RedirectStandardOutput = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };
                    using (Process p = Process.Start(psi))
                    {
                        string output = p?.StandardOutput.ReadToEnd();
                        p?.WaitForExit(800);
                        if (!string.IsNullOrWhiteSpace(output))
                        {
                            string[] pts = output.Trim().Split(',');
                            if (pts.Length >= 8)
                            {
                                snap.GpuName = pts[0].Trim();
                                float.TryParse(pts[1].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out float gTemp);
                                float.TryParse(pts[2].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out float gFan);
                                float.TryParse(pts[3].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out float gWatts);
                                float.TryParse(pts[4].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out float gLoad);
                                float.TryParse(pts[5].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out float gClock);
                                float.TryParse(pts[6].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out float vUsed);
                                float.TryParse(pts[7].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out float vTotal);

                                if (gTemp > 0) snap.GpuTemp = gTemp;
                                if (gFan > 0) snap.GpuFanPercent = gFan;
                                if (gWatts > 0) snap.GpuPowerWatts = gWatts;
                                if (gLoad >= 0) snap.GpuLoad = gLoad;
                                if (gClock > 0) snap.GpuCoreClock = gClock;
                                snap.GpuVramUsedMb = vUsed;
                                snap.GpuVramTotalMb = vTotal;
                            }
                        }
                    }
                }
                catch { }
            }

            // 4. Fallback de Carga/Temperatura CPU via WMI caso o Windows bloqueie o driver WinRing0
            if (snap.CpuLoad == 0)
            {
                try
                {
                    using (var searcher = new ManagementObjectSearcher("SELECT LoadPercentage, CurrentClockSpeed FROM Win32_Processor"))
                    {
                        foreach (ManagementObject obj in searcher.Get())
                        {
                            snap.CpuLoad = Convert.ToSingle(obj["LoadPercentage"]);
                            if (snap.CpuClockMhz == 0) snap.CpuClockMhz = Convert.ToSingle(obj["CurrentClockSpeed"]);
                            break;
                        }
                    }
                }
                catch { }
            }

            return snap;
        }

        public static List<DiskRingInfo> GetDisksInfo()
        {
            var list = new List<DiskRingInfo>();
            try
            {
                foreach (DriveInfo d in DriveInfo.GetDrives())
                {
                    if (!d.IsReady) continue;
                    double total = d.TotalSize / (1024.0 * 1024 * 1024);
                    double free = d.AvailableFreeSpace / (1024.0 * 1024 * 1024);
                    double usedPct = total > 0 ? ((total - free) / total) * 100.0 : 0;
                    list.Add(new DiskRingInfo
                    {
                        DriveLetter = d.Name.TrimEnd('\\'),
                        VolumeLabel = string.IsNullOrWhiteSpace(d.VolumeLabel) ? d.DriveType.ToString() : d.VolumeLabel,
                        TotalGB = total,
                        FreeGB = free,
                        UsedPercent = usedPct
                    });
                }
            }
            catch { }
            return list;
        }

        public void Dispose()
        {
            try { _computer?.Close(); } catch { }
        }
    }
}