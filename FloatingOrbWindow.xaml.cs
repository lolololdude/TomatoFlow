using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using TomatoFlow.Helpers;
using TomatoFlow.Models;
using TomatoFlow.Services;

namespace TomatoFlow;

public partial class FloatingOrbWindow : Window
{
    private readonly SettingsService _settings;
    private readonly DispatcherTimer _pulseTimer;
    private readonly DispatcherTimer _dragTimer;
    private readonly ScaleTransform _orbScale = new(1, 1, 43, 43);
    private DictationState _state = DictationState.Idle;
    private double _phase;
    private bool _pressed;
    private bool _dragging;
    private double _dragStartX;
    private double _dragStartY;
    private double _originLeft;
    private double _originTop;
    private double _level;

    public FloatingOrbWindow(SettingsService settings)
    {
        _settings = settings;
        InitializeComponent();
        OrbBorder.RenderTransform = _orbScale;
        SourceInitialized += OnSourceInitialized;
        Loaded += OnLoaded;
        MouseLeftButtonDown += OnMouseLeftButtonDown;
        MouseLeftButtonUp += OnMouseLeftButtonUp;
        Root.ContextMenu = CreateMenu();

        _dragTimer = new DispatcherTimer(DispatcherPriority.Input)
        {
            Interval = TimeSpan.FromMilliseconds(15)
        };
        _dragTimer.Tick += (_, _) => PollDrag();

        _pulseTimer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(33)
        };
        _pulseTimer.Tick += OnPulseTick;
    }

    public event EventHandler? ToggleRequested;
    public event EventHandler? OpenRequested;
    public event EventHandler? HideRequested;

    public void ApplyBranding(AppSettings settings)
    {
        var logo = ImageLoader.Load(settings.LogoPath, 180);
        if (logo is not null)
        {
            OrbLogo.Source = logo;
            OrbLogo.Visibility = Visibility.Visible;
            TomatoGlyph.Visibility = Visibility.Collapsed;
        }
        else
        {
            OrbLogo.Source = null;
            OrbLogo.Visibility = Visibility.Collapsed;
            TomatoGlyph.Visibility = Visibility.Visible;
        }
    }

    public void SetLevel(float level)
    {
        _level = Math.Clamp(level, 0, 1);
    }

    public void SetState(DictationState state)
    {
        _state = state;
        var accent = Brush("AccentBrush");
        var danger = Brush("DangerBrush");
        var success = Brush("SuccessBrush");

        StatusDot.Fill = state switch
        {
            DictationState.Recording => danger,
            DictationState.Transcribing => accent,
            _ => success
        };
        StatusText.Text = state switch
        {
            DictationState.Recording => "LISTENING",
            DictationState.Transcribing => "TOMATO IS TYPING",
            _ => "TOMATO FLOW"
        };
        RingOne.Stroke = state == DictationState.Recording ? danger : accent;
        RingTwo.Stroke = state == DictationState.Recording ? danger : accent;
        OrbGlow.Color = state == DictationState.Recording ? Color.FromRgb(255, 77, 77) : Color.FromRgb(255, 90, 60);

        if (state == DictationState.Transcribing)
        {
            var spin = new DoubleAnimation(0, 360, TimeSpan.FromSeconds(1.1))
            {
                RepeatBehavior = RepeatBehavior.Forever
            };
            TomatoGlyph.RenderTransformOrigin = new Point(0.5, 0.5);
            TomatoGlyph.RenderTransform = new RotateTransform();
            TomatoGlyph.RenderTransform.BeginAnimation(RotateTransform.AngleProperty, spin);
        }
        else
        {
            TomatoGlyph.RenderTransform = Transform.Identity;
        }
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        var handle = new WindowInteropHelper(this).Handle;
        NativeMethods.MakeNoActivate(handle);
        var source = HwndSource.FromHwnd(handle);
        source?.AddHook((IntPtr _, int msg, IntPtr _, IntPtr _, ref bool handled) =>
        {
            if (msg == NativeMethods.WmMouseActivate)
            {
                handled = true;
                return new IntPtr(NativeMethods.MaNoActivate);
            }

            return IntPtr.Zero;
        });
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        var settings = _settings.Settings;
        var workArea = SystemParameters.WorkArea;
        if (settings.OrbLeft > -90000 && settings.OrbTop > -90000
            && settings.OrbLeft < workArea.Right && settings.OrbTop < workArea.Bottom)
        {
            Left = settings.OrbLeft;
            Top = settings.OrbTop;
        }
        else
        {
            Left = workArea.Right - Width - 26;
            Top = workArea.Bottom - Height - 26;
        }

        ApplyBranding(settings);
        _pulseTimer.Start();
    }

    private void OnPulseTick(object? sender, EventArgs e)
    {
        _phase += _state == DictationState.Recording ? 0.24 : 0.08;
        var pulse = (Math.Sin(_phase) + 1) / 2;
        RingOne.Opacity = 0.22 + (pulse * 0.55);
        RingTwo.Opacity = 0.5 - (pulse * 0.4);

        var targetScale = 1 + (_state == DictationState.Recording ? pulse * 0.07 : pulse * 0.025) + (_level * 0.05);
        _orbScale.ScaleX += (targetScale - _orbScale.ScaleX) * 0.25;
        _orbScale.ScaleY += (targetScale - _orbScale.ScaleY) * 0.25;
        OrbGlow.BlurRadius = 22 + (pulse * 10) + (_level * 14);
    }

    private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left)
        {
            return;
        }

        _pressed = true;
        _dragging = false;
        _originLeft = Left;
        _originTop = Top;
        var cursor = ScreenCursor();
        _dragStartX = cursor.X;
        _dragStartY = cursor.Y;
        _dragTimer.Start();
        e.Handled = true;
    }

    private void OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left)
        {
            return;
        }

        EndDrag();
    }

    private void PollDrag()
    {
        if (!_pressed)
        {
            _dragTimer.Stop();
            return;
        }

        if ((NativeMethods.GetAsyncKeyState(NativeMethods.VkLeftButton) & 0x8000) == 0)
        {
            EndDrag();
            return;
        }

        var cursor = ScreenCursor();
        var dx = cursor.X - _dragStartX;
        var dy = cursor.Y - _dragStartY;
        if (!_dragging && (Math.Abs(dx) > 4 || Math.Abs(dy) > 4))
        {
            _dragging = true;
        }

        if (!_dragging)
        {
            return;
        }

        Left = Clamp(_originLeft + dx, SystemParameters.VirtualScreenLeft - Width + 60,
            SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - 60);
        Top = Clamp(_originTop + dy, SystemParameters.VirtualScreenTop,
            SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - 60);
    }

    private void EndDrag()
    {
        if (!_pressed)
        {
            return;
        }

        _dragTimer.Stop();
        var wasDragging = _dragging;
        _pressed = false;
        _dragging = false;

        if (wasDragging)
        {
            SavePosition();
            return;
        }

        ToggleRequested?.Invoke(this, EventArgs.Empty);
    }

    private Point ScreenCursor()
    {
        if (!NativeMethods.GetCursorPos(out var point))
        {
            return new Point(_dragStartX, _dragStartY);
        }

        var dpi = VisualTreeHelper.GetDpi(this);
        return new Point(point.X / dpi.DpiScaleX, point.Y / dpi.DpiScaleY);
    }

    private static double Clamp(double value, double min, double max)
    {
        return Math.Min(Math.Max(value, min), Math.Max(min, max));
    }

    private void SavePosition()
    {
        _settings.Settings.OrbLeft = Left;
        _settings.Settings.OrbTop = Top;
        _settings.Save();
    }

    public void ResetPosition()
    {
        var workArea = SystemParameters.WorkArea;
        Left = workArea.Right - Width - 26;
        Top = workArea.Bottom - Height - 26;
        SavePosition();
    }

    private ContextMenu CreateMenu()
    {
        var background = new SolidColorBrush(Color.FromRgb(28, 14, 11));
        var foreground = new SolidColorBrush(Color.FromRgb(255, 244, 236));
        var menu = new ContextMenu
        {
            Background = background,
            Foreground = foreground,
            BorderBrush = new SolidColorBrush(Color.FromRgb(110, 43, 34)),
            Padding = new Thickness(4)
        };

        menu.Items.Add(MenuItem("Open Tomato Flow", (_, _) => OpenRequested?.Invoke(this, EventArgs.Empty)));
        menu.Items.Add(MenuItem("Start or stop dictation", (_, _) => ToggleRequested?.Invoke(this, EventArgs.Empty)));
        menu.Items.Add(new Separator());
        menu.Items.Add(MenuItem("Hide the tomato", (_, _) => HideRequested?.Invoke(this, EventArgs.Empty)));
        menu.Items.Add(MenuItem("Reset tomato position", (_, _) => ResetPosition()));
        return menu;
    }

    private static MenuItem MenuItem(string header, RoutedEventHandler click)
    {
        var item = new MenuItem
        {
            Header = header,
            Padding = new Thickness(12, 6, 12, 6)
        };
        item.Click += click;
        return item;
    }

    private Brush Brush(string key)
    {
        return TryFindResource(key) as Brush ?? Brushes.Tomato;
    }
}
