using System.Windows;
using TomatoFlow.Helpers;
using TomatoFlow.Models;
using Drawing = System.Drawing;
using WinForms = System.Windows.Forms;

namespace TomatoFlow.Services;

public sealed class TrayService : IDisposable
{
    private readonly WinForms.NotifyIcon _notifyIcon;
    private readonly WinForms.ToolStripMenuItem _dictationItem;
    private readonly WinForms.ToolStripMenuItem _floatingItem;
    private readonly WinForms.ToolStripMenuItem _adsItem;
    private readonly WinForms.ToolStripMenuItem _removeBundledItem;
    private readonly WinForms.ToolStripMenuItem _pasteItem;
    private readonly WinForms.ToolStripMenuItem _copyItem;
    private Drawing.Icon? _icon;

    public TrayService()
    {
        _icon = LoadAppIcon();
        _dictationItem = new WinForms.ToolStripMenuItem("Start dictation", null, (_, _) => Dispatch(() => ToggleDictationRequested?.Invoke(this, EventArgs.Empty)));
        _floatingItem = new WinForms.ToolStripMenuItem("Floating tomato") { CheckOnClick = false };
        _adsItem = new WinForms.ToolStripMenuItem("Tomato ads") { CheckOnClick = false };
        _removeBundledItem = new WinForms.ToolStripMenuItem("Remove bundled ads", null, (_, _) => Dispatch(() => RemoveBundledAdsRequested?.Invoke(this, EventArgs.Empty)));
        _floatingItem.Click += (_, _) => Dispatch(() => FloatingOrbChanged?.Invoke(this, !_floatingItem.Checked));
        _adsItem.Click += (_, _) => Dispatch(() => AdsChanged?.Invoke(this, !_adsItem.Checked));
        _pasteItem = new WinForms.ToolStripMenuItem("Paste into active app", null, (_, _) => Dispatch(() => OutputModeChanged?.Invoke(this, OutputMode.Paste))) { CheckOnClick = false };
        _copyItem = new WinForms.ToolStripMenuItem("Copy to clipboard", null, (_, _) => Dispatch(() => OutputModeChanged?.Invoke(this, OutputMode.Clipboard))) { CheckOnClick = false };

        var outputMenu = new WinForms.ToolStripMenuItem("Output mode");
        outputMenu.DropDownItems.Add(_pasteItem);
        outputMenu.DropDownItems.Add(_copyItem);

        var menu = new WinForms.ContextMenuStrip
        {
            BackColor = Drawing.Color.FromArgb(28, 14, 11),
            ForeColor = Drawing.Color.FromArgb(255, 244, 236),
            Renderer = new DarkMenuRenderer(),
            ShowImageMargin = false
        };
        var openItem = new WinForms.ToolStripMenuItem("Open Tomato Flow", null, (_, _) => Dispatch(() => OpenRequested?.Invoke(this, EventArgs.Empty))) { Font = new Drawing.Font("Segoe UI", 9f, Drawing.FontStyle.Bold) };
        menu.Items.Add(openItem);
        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add(_dictationItem);
        menu.Items.Add(_floatingItem);
        menu.Items.Add(_adsItem);
        menu.Items.Add(_removeBundledItem);
        menu.Items.Add(outputMenu);
        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add(new WinForms.ToolStripMenuItem("Exit Tomato Flow", null, (_, _) => Dispatch(() => ExitRequested?.Invoke(this, EventArgs.Empty))));

        _notifyIcon = new WinForms.NotifyIcon
        {
            Icon = _icon,
            Text = "Tomato Flow",
            ContextMenuStrip = menu,
            Visible = true
        };
        _notifyIcon.DoubleClick += (_, _) => Dispatch(() => OpenRequested?.Invoke(this, EventArgs.Empty));
    }

    public event EventHandler? OpenRequested;
    public event EventHandler? ToggleDictationRequested;
    public event EventHandler? ExitRequested;
    public event EventHandler<bool>? FloatingOrbChanged;
    public event EventHandler<bool>? AdsChanged;
    public event EventHandler? RemoveBundledAdsRequested;
    public event EventHandler<OutputMode>? OutputModeChanged;

    public void Update(AppSettings settings, DictationState state)
    {
        _floatingItem.Checked = settings.ShowFloatingOrb;
        _adsItem.Checked = settings.AdsEnabled;
        _removeBundledItem.Enabled = AdFolders.BundledCount > 0;
        _pasteItem.Checked = settings.OutputMode == OutputMode.Paste;
        _copyItem.Checked = settings.OutputMode == OutputMode.Clipboard;
        _dictationItem.Text = state == DictationState.Recording ? "Stop dictation" : "Start dictation";
        _notifyIcon.Text = state switch
        {
            DictationState.Recording => "Tomato Flow - recording",
            DictationState.Transcribing => "Tomato Flow - transcribing",
            _ => "Tomato Flow - ready"
        };
    }

    public void Balloon(string title, string message, bool warning = false)
    {
        try
        {
            _notifyIcon.BalloonTipTitle = title;
            _notifyIcon.BalloonTipText = message;
            _notifyIcon.BalloonTipIcon = warning ? WinForms.ToolTipIcon.Warning : WinForms.ToolTipIcon.Info;
            _notifyIcon.ShowBalloonTip(3500);
        }
        catch
        {
        }
    }

    public void UpdateIcon(bool recording)
    {
        try
        {
            _notifyIcon.Icon = recording ? CreateStatusIcon(Drawing.Color.FromArgb(255, 77, 77)) : _icon;
        }
        catch
        {
        }
    }

    private static Drawing.Icon? LoadAppIcon()
    {
        try
        {
            var resource = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/tomatoflow.ico"));
            if (resource?.Stream is not null)
            {
                return new Drawing.Icon(resource.Stream);
            }
        }
        catch
        {
        }

        try
        {
            var path = Environment.ProcessPath;
            return path is null ? Drawing.SystemIcons.Application : Drawing.Icon.ExtractAssociatedIcon(path) ?? Drawing.SystemIcons.Application;
        }
        catch
        {
            return Drawing.SystemIcons.Application;
        }
    }

    private static Drawing.Icon CreateStatusIcon(Drawing.Color dotColor)
    {
        using var bitmap = new Drawing.Bitmap(32, 32);
        using var graphics = Drawing.Graphics.FromImage(bitmap);
        graphics.SmoothingMode = Drawing.Drawing2D.SmoothingMode.AntiAlias;
        using var background = new Drawing.SolidBrush(Drawing.Color.FromArgb(255, 24, 12, 9));
        graphics.FillEllipse(background, 1, 1, 30, 30);
        using var ring = new Drawing.Pen(Drawing.Color.FromArgb(255, 255, 90, 60), 2f);
        graphics.DrawEllipse(ring, 2, 2, 28, 28);
        using var dot = new Drawing.SolidBrush(dotColor);
        graphics.FillEllipse(dot, 20, 2, 11, 11);
        return Drawing.Icon.FromHandle(bitmap.GetHicon());
    }

    private static void Dispatch(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            action();
        }
        else
        {
            dispatcher.Invoke(action);
        }
    }

    public void Dispose()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _icon?.Dispose();
        _icon = null;
    }

    private sealed class DarkMenuRenderer : WinForms.ToolStripProfessionalRenderer
    {
        public DarkMenuRenderer() : base(new DarkColorTable())
        {
            RoundedEdges = false;
        }

        protected override void OnRenderItemText(WinForms.ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = e.Item?.Enabled == true
                ? Drawing.Color.FromArgb(255, 244, 236)
                : Drawing.Color.FromArgb(120, 116, 106);
            base.OnRenderItemText(e);
        }

        protected override void OnRenderToolStripBorder(WinForms.ToolStripRenderEventArgs e)
        {
            using var pen = new Drawing.Pen(Drawing.Color.FromArgb(43, 37, 24));
            e.Graphics.DrawRectangle(pen, 0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1);
        }
    }

    private sealed class DarkColorTable : WinForms.ProfessionalColorTable
    {
        public override Drawing.Color ToolStripDropDownBackground => Drawing.Color.FromArgb(28, 14, 11);
        public override Drawing.Color ImageMarginGradientBegin => Drawing.Color.FromArgb(28, 14, 11);
        public override Drawing.Color ImageMarginGradientMiddle => Drawing.Color.FromArgb(28, 14, 11);
        public override Drawing.Color ImageMarginGradientEnd => Drawing.Color.FromArgb(28, 14, 11);
        public override Drawing.Color MenuBorder => Drawing.Color.FromArgb(43, 37, 24);
        public override Drawing.Color MenuItemBorder => Drawing.Color.FromArgb(242, 193, 78);
        public override Drawing.Color MenuItemSelected => Drawing.Color.FromArgb(33, 29, 20);
        public override Drawing.Color MenuItemSelectedGradientBegin => Drawing.Color.FromArgb(33, 29, 20);
        public override Drawing.Color MenuItemSelectedGradientEnd => Drawing.Color.FromArgb(33, 29, 20);
        public override Drawing.Color SeparatorDark => Drawing.Color.FromArgb(43, 37, 24);
        public override Drawing.Color SeparatorLight => Drawing.Color.FromArgb(43, 37, 24);
    }
}
