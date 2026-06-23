using System;
using System.Collections.Generic;
using System.Configuration;
using System.Globalization;
using System.Linq;
using RLC_LoadBank_SeparateVer.Models;

namespace RLC_LoadBank_SeparateVer.Services
{
    /// <summary>
    /// Loads and saves the device list from/to app.config appSettings.
    /// Keys: Device.Count, Device.{i}.Use, .Type, .Name, .Ip, .Port,
    ///       .UnitId, .SlaveId, .CurrentReg, .Scale, .InputReg, .PollInterval, .Timeout
    /// Falls back to a built-in default list when the config has no Device.Count entry.
    /// </summary>
    public static class DeviceConfigService
    {
        public static IReadOnlyList<DeviceRecord> Load()
        {
            var s = ConfigurationManager.AppSettings;
            if (!int.TryParse(s["Device.Count"], out int count) || count == 0)
                return BuildDefault();

            var result = new List<DeviceRecord>(count);
            for (int i = 0; i < count; i++)
            {
                string p = $"Device.{i}.";
                result.Add(new DeviceRecord
                {
                    Use         = ParseBool(s[$"{p}Use"]),
                    Type        = ParseEnum<DeviceType>(s[$"{p}Type"], DeviceType.PLC),
                    Name        = s[$"{p}Name"] ?? string.Empty,
                    Ip          = s[$"{p}Ip"]   ?? string.Empty,
                    Port        = ParseInt(s[$"{p}Port"],         502),
                    UnitId      = ParseInt(s[$"{p}UnitId"],       1),
                    SlaveId     = ParseInt(s[$"{p}SlaveId"],      1),
                    CurrentReg  = ParseInt(s[$"{p}CurrentReg"],   0),
                    Scale       = ParseDouble(s[$"{p}Scale"],     1.0),
                    InputReg    = ParseInt(s[$"{p}InputReg"],     0),
                    PollInterval= ParseInt(s[$"{p}PollInterval"], 500),
                    Timeout     = ParseInt(s[$"{p}Timeout"],      1000),
                    State       = ConnState.Disconnected,
                });
            }
            return result;
        }

        public static void Save(IEnumerable<DeviceRecord> devices)
        {
            var cfg = ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.None);
            var s   = cfg.AppSettings.Settings;

            // Remove existing Device.* keys
            foreach (var k in s.AllKeys.Where(k => k.StartsWith("Device.", StringComparison.Ordinal)).ToList())
                s.Remove(k);

            var list = devices.ToList();
            Set(s, "Device.Count", list.Count.ToString());
            for (int i = 0; i < list.Count; i++)
            {
                var d = list[i];
                string p = $"Device.{i}.";
                Set(s, $"{p}Use",          d.Use.ToString());
                Set(s, $"{p}Type",         d.Type.ToString());
                Set(s, $"{p}Name",         d.Name  ?? string.Empty);
                Set(s, $"{p}Ip",           d.Ip    ?? string.Empty);
                Set(s, $"{p}Port",         d.Port.ToString());
                Set(s, $"{p}UnitId",       d.UnitId.ToString());
                Set(s, $"{p}SlaveId",      d.SlaveId.ToString());
                Set(s, $"{p}CurrentReg",   d.CurrentReg.ToString());
                Set(s, $"{p}Scale",        d.Scale.ToString("G", CultureInfo.InvariantCulture));
                Set(s, $"{p}InputReg",     d.InputReg.ToString());
                Set(s, $"{p}PollInterval", d.PollInterval.ToString());
                Set(s, $"{p}Timeout",      d.Timeout.ToString());
            }

            cfg.Save(ConfigurationSaveMode.Modified);
            ConfigurationManager.RefreshSection("appSettings");
        }

        // ──────────────────────────────────────────────────────────────────
        private static void Set(KeyValueConfigurationCollection s, string key, string value)
        {
            if (s[key] == null) s.Add(key, value);
            else s[key].Value = value;
        }

        private static bool   ParseBool(string v)                         => bool.TryParse(v, out bool r) && r;
        private static int    ParseInt(string v, int def)                 => int.TryParse(v, out int r) ? r : def;
        private static double ParseDouble(string v, double def)           => double.TryParse(v, NumberStyles.Any, CultureInfo.InvariantCulture, out double r) ? r : def;
        private static T      ParseEnum<T>(string v, T def) where T : struct
            => Enum.TryParse<T>(v, out var r) ? r : def;

        private static IReadOnlyList<DeviceRecord> BuildDefault()
        {
            var list = new List<DeviceRecord>
            {
                new DeviceRecord { Use=true, Type=DeviceType.PLC, Name="PLC1_PNL1", Ip="192.168.10.11", Port=502, UnitId=1, SlaveId=1 },
                new DeviceRecord { Use=true, Type=DeviceType.PLC, Name="PLC2_PNL2", Ip="192.168.10.12", Port=502, UnitId=1, SlaveId=1 },
                new DeviceRecord { Use=true, Type=DeviceType.PLC, Name="PLC3_PNL3", Ip="192.168.10.13", Port=502, UnitId=1, SlaveId=1 },
            };
            for (int i = 1; i <= 10; i++)
                list.Add(new DeviceRecord { Type=DeviceType.ISEM,  Name=$"ISEM2-WHRUH_{i:00}",  Ip=$"192.168.1.{9+i}",  Port=502, UnitId=i, SlaveId=i, CurrentReg=500,   Scale=0.01 });
            for (int i = 1; i <= 4;  i++)
                list.Add(new DeviceRecord { Type=DeviceType.GIMAC, Name=$"GIMAC1000_{i:00}",     Ip=$"192.168.1.{19+i}", Port=502, UnitId=i, SlaveId=i, CurrentReg=30003, Scale=1 });
            return list;
        }
    }
}
