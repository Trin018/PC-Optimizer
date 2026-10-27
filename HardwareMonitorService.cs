using System;
using System.Collections.Generic;
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

        public string MotherboardName { get; set; } = "Placa-Mãe";
        public List<string> FanReadings { get; set; } = new List<string>();
    }

    public class HardwareMonitorService : IDisposable
    {
        private Computer _computer;
        private bool _initialized = false;

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
            if (!_initialized || _computer == null) return snap;

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

                            if (s.SensorType == SensorType.Temperature && (s.Name.Contains("Package") || s.Name.Contains("Tctl") || s.Name.Contains("Core (Tctl") || s.Name.Contains("Average") || snap.CpuTemp == 0))
                                snap.CpuTemp = val;
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

                            if (s.SensorType == SensorType.Temperature)
                            {
                                if (s.Name.Contains("Hot Spot")) snap.GpuHotspot = val;
                                else if (s.Name.Contains("Core") || snap.GpuTemp == 0) snap.GpuTemp = val;
                            }
                            else if (s.SensorType == SensorType.Load && s.Name.Contains("Core"))
                                snap.GpuLoad = val;
                            else if (s.SensorType == SensorType.Power)
                                snap.GpuPowerWatts = val;
                            else if (s.SensorType == SensorType.Fan)
                                snap.GpuFanRpm = val;
                            else if (s.SensorType == SensorType.Control && s.Name.Contains("Fan"))
                                snap.GpuFanPercent = val;
                            else if (s.SensorType == SensorType.Clock)
                            {
                                if (s.Name.Contains("Core")) snap.GpuCoreClock = val;
                                else if (s.Name.Contains("Memory")) snap.GpuMemClock = val;
                            }
                            else if (s.SensorType == SensorType.SmallData)
                            {
                                if (s.Name.Contains("Memory Used") || s.Name.Contains("D3D Dedicated Memory Used")) snap.GpuVramUsedMb = val;
                                else if (s.Name.Contains("Memory Total")) snap.GpuVramTotalMb = val;
                            }
                        }
                    }
                }
            }
            catch { }
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