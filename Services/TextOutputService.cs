using System.Runtime.InteropServices;
using System.Windows;
using TomatoFlow.Helpers;
using TomatoFlow.Models;

namespace TomatoFlow.Services;

public sealed record OutputResult(bool Copied, bool Pasted, bool TargetLost);

public sealed class TextOutputService
{
    public async Task<OutputResult> DeliverAsync(
        string text,
        OutputMode mode,
        IntPtr targetWindow,
        bool restoreClipboard,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return new OutputResult(false, false, false);
        }

        IDataObject? previous = null;
        if (restoreClipboard)
        {
            previous = TryGetClipboard();
        }

        var copied = TrySetClipboard(text);
        if (!copied)
        {
            return new OutputResult(false, false, false);
        }

        if (mode == OutputMode.Clipboard)
        {
            return new OutputResult(true, false, false);
        }

        var targetLost = targetWindow == IntPtr.Zero || !NativeMethods.IsWindow(targetWindow) || IsOwnWindow(targetWindow);
        if (targetLost)
        {
            return new OutputResult(true, false, true);
        }

        try
        {
            NativeMethods.FocusWindow(targetWindow);
            await Task.Delay(140, cancellationToken);
            if (NativeMethods.GetForegroundWindow() != targetWindow)
            {
                return new OutputResult(true, false, true);
            }

            NativeMethods.SendPaste();
            await Task.Delay(180, cancellationToken);
            if (restoreClipboard && previous is not null)
            {
                TrySetClipboard(previous);
            }

            return new OutputResult(true, true, false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Log.Error("Could not paste transcript.", ex);
            return new OutputResult(true, false, true);
        }
    }

    private static bool IsOwnWindow(IntPtr window)
    {
        try
        {
            NativeMethods.GetWindowThreadProcessId(window, out var processId);
            return processId == (uint)Environment.ProcessId;
        }
        catch
        {
            return false;
        }
    }

    private static bool TrySetClipboard(string text)
    {
        for (var attempt = 0; attempt < 6; attempt++)
        {
            try
            {
                Clipboard.SetText(text);
                return true;
            }
            catch (COMException)
            {
                Thread.Sleep(40);
            }
            catch (Exception ex)
            {
                Log.Error("Clipboard write failed.", ex);
                Thread.Sleep(40);
            }
        }

        return false;
    }

    private static bool TrySetClipboard(IDataObject data)
    {
        for (var attempt = 0; attempt < 4; attempt++)
        {
            try
            {
                Clipboard.SetDataObject(data, true);
                return true;
            }
            catch
            {
                Thread.Sleep(40);
            }
        }

        return false;
    }

    private static IDataObject? TryGetClipboard()
    {
        try
        {
            return Clipboard.GetDataObject();
        }
        catch
        {
            return null;
        }
    }
}
