using System.Diagnostics;
using System.Globalization;
using TomatoFlow.Helpers;
using TomatoFlow.Models;
using Microsoft.Win32;

namespace TomatoFlow.Services;

public static class HardwareDetector
{
    public static HardwareInfo Detect()
    {
        var cpu = ReadCpuName();
        var threads = Math.Max(1, Environment.ProcessorCount);
        var ram = NativeMethods.GetTotalRamGb();
        var (gpu, vram, hasNvidia) = ReadGpu();
        return new HardwareInfo(cpu, threads, ram, gpu, vram, hasNvidia);
    }

    private static string ReadCpuName()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
            if (key?.GetValue("ProcessorNameString") is string name && !string.IsNullOrWhiteSpace(name))
            {
                return name.Trim();
            }
        }
        catch
        {
        }

        return "Unknown CPU";
    }

    private static (string Name, double VramGb, bool HasNvidia) ReadGpu()
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "nvidia-smi",
                Arguments = "--query-gpu=name,memory.total --format=csv,noheader,nounits",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return ("System graphics", 0, false);
            }

            var output = process.StandardOutput.ReadToEnd();
            if (!process.WaitForExit(2500))
            {
                try
                {
                    process.Kill(true);
                }
                catch
                {
                }

                return ("System graphics", 0, false);
            }

            var line = output.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            if (string.IsNullOrWhiteSpace(line))
            {
                return ("System graphics", 0, false);
            }

            var parts = line.Split(',');
            var name = parts[0].Trim();
            var vramMb = parts.Length > 1 && double.TryParse(parts[1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : 0;
            return (name, vramMb / 1024d, true);
        }
        catch
        {
            return ("System graphics", 0, false);
        }
    }
}
