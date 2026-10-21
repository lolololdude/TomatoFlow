using TomatoFlow.Helpers;
using Microsoft.Win32;

namespace TomatoFlow.Services;

public static class StartupService
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "TomatoFlow";

    public static void Apply(bool enabled, bool minimized)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, true);
            if (key is null)
            {
                return;
            }

            if (enabled)
            {
                var path = Environment.ProcessPath;
                if (string.IsNullOrWhiteSpace(path))
                {
                    return;
                }

                var command = $"\"{path}\"" + (minimized ? " --minimized" : "");
                key.SetValue(ValueName, command);
            }
            else
            {
                key.DeleteValue(ValueName, false);
            }
        }
        catch (Exception ex)
        {
            Log.Error("Could not update Windows startup.", ex);
        }
    }
}
