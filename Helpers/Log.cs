namespace TomatoFlow.Helpers;

public static class Log
{
    private static readonly object Gate = new();

    public static void Info(string message)
    {
        Write("INFO", message);
    }

    public static void Error(string message, Exception? exception = null)
    {
        Write("ERROR", exception is null ? message : $"{message}{Environment.NewLine}{exception}");
    }

    private static void Write(string level, string message)
    {
        try
        {
            AppPaths.EnsureCreated();
            lock (Gate)
            {
                File.AppendAllText(AppPaths.LogFile, $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}{Environment.NewLine}");
            }
        }
        catch
        {
        }
    }
}
