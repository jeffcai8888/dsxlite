using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using NAudio.CoreAudioApi;
using DsxLite.Core.DualSense;
using DsxLite.Core.Haptics;
using DsxLite.Core.ViGEm;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using CheckBox = System.Windows.Controls.CheckBox;
using ComboBox = System.Windows.Controls.ComboBox;
using Color = System.Windows.Media.Color;

namespace DsxLite.App;

public partial class MainWindow : Window
{
    private static readonly Brush IndicatorOff = new SolidColorBrush(Color.FromRgb(0xEE, 0xEE, 0xEE));
    private static readonly Brush IndicatorOn = new SolidColorBrush(Color.FromRgb(0x3B, 0x8E, 0xD0));

    // Indicator name -> localization key (null = the name itself is the label, e.g. symbols).
    private static readonly (string Name, string? LabelKey)[] IndicatorDefs =
    [
        ("✕", null), ("○", null), ("□", null), ("△", null), ("L1", null), ("R1", null),
        ("L2", null), ("R2", null), ("Create", null), ("Options", null), ("L3", null), ("R3", null),
        ("PS", null), ("Touchpad", "BtnTouchpad"), ("Mute", "BtnMute"),
        ("↑", null), ("↓", null), ("←", null), ("→", null),
        ("Fn1", null), ("Fn2", null), ("LPaddle", "BtnLeftPaddle"), ("RPaddle", "BtnRightPaddle"),
    ];

    /// <summary>List item wrapping a trigger preset with localized display text.</summary>
    private sealed record PresetView(TriggerEffectPreset Preset)
    {
        public string Name => Localization.Get(Preset.Name);
        public string Hint => Localization.Get(Preset.Hint);
    }

    private readonly Dictionary<string, Border> _indicators = new();
    private readonly Dictionary<CheckBox, int> _playerLedBoxes = new();
    private readonly DispatcherTimer _timer;
    private readonly VirtualXbox360 _virtualPad = new();

    private List<DualSenseDevice> _devices = [];
    private DualSenseDevice? _device;
    private readonly DualSenseHapticsOutput _haptics = new();
    private AudioToHapticsEngine? _a2h;
    private List<MMDevice> _a2hSources = [];

    private System.Windows.Forms.NotifyIcon? _trayIcon;
    private System.Windows.Forms.ToolStripMenuItem? _trayShowItem;
    private System.Windows.Forms.ToolStripMenuItem? _trayExitItem;
    private bool _exiting;
    private bool _balloonShown;
    private bool _languageChanging;

    public MainWindow()
    {
        InitializeComponent();
        AutoStartCheck.IsChecked = AutoStart.IsEnabled();
        InitializeTrayIcon();

        foreach ((string name, string? labelKey) in IndicatorDefs)
        {
            Border indicator = MakeIndicator(labelKey == null ? name : Localization.Get(labelKey));
            _indicators[name] = indicator;
            ButtonsPanel.Children.Add(indicator);
        }

        for (int i = 0; i < 5; i++)
        {
            var box = new CheckBox { Content = $"LED {i + 1}", Margin = new Thickness(0, 0, 10, 0) };
            box.Checked += OnPlayerLedChanged;
            box.Unchecked += OnPlayerLedChanged;
            _playerLedBoxes[box] = i;
            PlayerLedsPanel.Children.Add(box);
        }

        MuteLedCombo.SelectedIndex = 0;
        HapticsLeftWave.SelectedIndex = 0;
        HapticsRightWave.SelectedIndex = 0;
        HapticsEnable.IsEnabled = false;
        A2hEnable.IsEnabled = false;
        A2hCutoff.SelectedIndex = 1;
        A2hMode.SelectedIndex = 0;

        LanguageCombo.ItemsSource = Localization.Languages;
        LanguageCombo.DisplayMemberPath = "Name";
        LanguageCombo.SelectedValuePath = "Code";
        LanguageCombo.SelectedValue = Localization.Current;

        RebuildTriggerTestList();

        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
        _timer.Tick += OnTick;

        Localization.LanguageChanged += OnLanguageChangedApply;
        Loaded += OnLoaded;
        Closing += OnWindowClosing;
    }

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        RefreshDevices();
        if (App.StartInTray && _devices.Count > 0 && DeviceCombo.Items.Count > 0)
        {
            // Auto-started into the tray: connect the first controller silently.
            DeviceCombo.SelectedIndex = 0;
            OnConnectClicked(this, new RoutedEventArgs());
        }
    }

    // ---------- 语言 ----------

    private void OnLanguageChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_languageChanging || LanguageCombo.SelectedValue is not string code || code == Localization.Current)
            return;
        Localization.Apply(code);
    }

    private void OnLanguageChangedApply(object? sender, EventArgs e)
    {
        _languageChanging = true;
        LanguageCombo.SelectedValue = Localization.Current;
        _languageChanging = false;

        // Refresh content built in code (DynamicResource handles pure XAML).
        foreach ((string name, string? labelKey) in IndicatorDefs)
            if (labelKey != null)
                ((TextBlock)_indicators[name].Child).Text = Localization.Get(labelKey);

        ConnectButton.Content = Localization.Get(_device != null ? "Disconnect" : "Connect");
        _trayShowItem!.Text = Localization.Get("TrayShow");
        _trayExitItem!.Text = Localization.Get("TrayExit");
        RebuildTriggerTestList();
    }

    private void RebuildTriggerTestList()
    {
        TriggerTestList.ItemsSource = TriggerEffectPresets.All.Select(p => new PresetView(p)).ToList();
    }

    // ---------- 托盘 / 自启动 ----------

    private void InitializeTrayIcon()
    {
        _trayShowItem = new System.Windows.Forms.ToolStripMenuItem(Localization.Get("TrayShow"), null, (_, _) => RestoreFromTray());
        _trayExitItem = new System.Windows.Forms.ToolStripMenuItem(Localization.Get("TrayExit"), null, (_, _) => ExitFromTray());
        var menu = new System.Windows.Forms.ContextMenuStrip();
        menu.Items.Add(_trayShowItem);
        menu.Items.Add(_trayExitItem);

        _trayIcon = new System.Windows.Forms.NotifyIcon
        {
            Text = "DsxLite — DualSense PC",
            Icon = CreateTrayIcon(),
            Visible = App.StartInTray,
            ContextMenuStrip = menu,
        };
        _trayIcon.DoubleClick += (_, _) => RestoreFromTray();
    }

    private static System.Drawing.Icon CreateTrayIcon()
    {
        var bitmap = new System.Drawing.Bitmap(32, 32);
        using (System.Drawing.Graphics g = System.Drawing.Graphics.FromImage(bitmap))
        {
            g.Clear(System.Drawing.Color.FromArgb(0x3B, 0x8E, 0xD0));
            using var font = new System.Drawing.Font("Segoe UI", 13, System.Drawing.FontStyle.Bold);
            var format = new System.Drawing.StringFormat
            {
                Alignment = System.Drawing.StringAlignment.Center,
                LineAlignment = System.Drawing.StringAlignment.Center,
            };
            g.DrawString("DS", font, System.Drawing.Brushes.White, new System.Drawing.RectangleF(0, 0, 32, 32), format);
        }
        return System.Drawing.Icon.FromHandle(bitmap.GetHicon());
    }

    private void HideToTray()
    {
        Hide();
        if (_trayIcon == null)
            return;
        _trayIcon.Visible = true;
        if (!_balloonShown)
        {
            _balloonShown = true;
            _trayIcon.ShowBalloonTip(3000, "DsxLite",
                Localization.Get("TrayBalloon"), System.Windows.Forms.ToolTipIcon.Info);
        }
    }

    private void RestoreFromTray()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
        if (_trayIcon != null)
            _trayIcon.Visible = false;
    }

    private void ExitFromTray()
    {
        _exiting = true;
        Close();
        System.Windows.Application.Current.Shutdown();
    }

    private void OnAutoStartChanged(object sender, RoutedEventArgs e)
    {
        try
        {
            AutoStart.SetEnabled(AutoStartCheck.IsChecked == true);
        }
        catch (Exception ex)
        {
            StatusText.Text = Localization.Format("AutoStartFailed", ex.Message);
            AutoStartCheck.IsChecked = AutoStart.IsEnabled();
        }
    }

    private static Border MakeIndicator(string label) => new()
    {
        Padding = new Thickness(8, 3, 8, 3),
        Margin = new Thickness(2),
        CornerRadius = new CornerRadius(4),
        Background = IndicatorOff,
        Child = new TextBlock { Text = label, FontSize = 12 },
    };

    private void SetIndicator(string name, bool active)
    {
        Border border = _indicators[name];
        border.Background = active ? IndicatorOn : IndicatorOff;
        ((TextBlock)border.Child).Foreground = active ? Brushes.White : Brushes.Black;
    }

    private void RefreshDevices()
    {
        try
        {
            _devices = DualSenseEnumerator.FindAll();
        }
        catch (Exception ex)
        {
            _devices = [];
            StatusText.Text = Localization.Format("EnumFailed", ex.Message);
        }

        DeviceCombo.ItemsSource = _devices.Select(d => d.DisplayName).ToList();
        if (_devices.Count > 0)
            DeviceCombo.SelectedIndex = 0;
        StatusText.Text = _devices.Count > 0
            ? Localization.Format("DevicesFound", _devices.Count)
            : Localization.Get("NoDevices");
    }

    private void OnRefreshClicked(object sender, RoutedEventArgs e) => RefreshDevices();

    private void OnConnectClicked(object sender, RoutedEventArgs e)
    {
        if (_device != null)
        {
            DisconnectDevice();
            return;
        }

        if (DeviceCombo.SelectedIndex < 0 || DeviceCombo.SelectedIndex >= _devices.Count)
        {
            StatusText.Text = Localization.Get("SelectDevice");
            return;
        }

        DualSenseDevice device = _devices[DeviceCombo.SelectedIndex];
        if (!device.Open())
        {
            StatusText.Text = Localization.Get("OpenFailed");
            return;
        }

        _device = device;
        _device.Disconnected += OnDeviceDisconnected;
        ConnectButton.Content = Localization.Get("Disconnect");
        RefreshButton.IsEnabled = false;
        DeviceCombo.IsEnabled = false;
        StatusText.Text = Localization.Format("Connected", _device.DisplayName);
        RefreshHapticsAvailability();
        _timer.Start();
    }

    private void DisconnectDevice()
    {
        _timer.Stop();
        if (A2hEnable.IsChecked == true)
            A2hEnable.IsChecked = false;
        A2hEnable.IsEnabled = false;
        _haptics.Stop();
        HapticsEnable.IsChecked = false;
        HapticsEnable.IsEnabled = false;
        HapticsStatus.Text = Localization.Get("HapticsNeedUsb");
        if (_device != null)
        {
            _device.Disconnected -= OnDeviceDisconnected;
            _device.Dispose();
            _device = null;
        }
        ConnectButton.Content = Localization.Get("Connect");
        RefreshButton.IsEnabled = true;
        DeviceCombo.IsEnabled = true;
        BatteryText.Text = "";
        StatusText.Text = Localization.Get("Disconnected");
    }

    private void OnDeviceDisconnected(object? sender, EventArgs e) =>
        Dispatcher.BeginInvoke(DisconnectDevice);

    private void OnWindowClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (!_exiting)
        {
            // Close button hides to the tray; use the tray menu to quit.
            e.Cancel = true;
            HideToTray();
            return;
        }

        _timer.Stop();
        Localization.LanguageChanged -= OnLanguageChangedApply;
        if (_trayIcon != null)
        {
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
        }
        _a2h?.Dispose();
        foreach (MMDevice source in _a2hSources)
            source.Dispose();
        _haptics.Dispose();
        _virtualPad.Dispose();
        _device?.Dispose();
    }

    // ---------- 输入可视化 ----------

    private void OnTick(object? sender, EventArgs e)
    {
        if (_device is not { IsOpen: true })
            return;

        DualSenseInputState s = _device.CurrentState;

        UpdateStick(LeftStickCanvas, LeftStickDot, s.LeftStickX, s.LeftStickY);
        UpdateStick(RightStickCanvas, RightStickDot, s.RightStickX, s.RightStickY);
        L2Bar.Value = s.LeftTrigger;
        R2Bar.Value = s.RightTrigger;

        if (s.IsFullReport)
        {
            (short pb, short yb, short rb) = _device.GyroBias;
            MotionText.Text =
                $"{Localization.Get("MotionGyro")}  {Localization.Get("MotionPitch")} {(s.GyroPitch - pb) / DualSenseIds.GyroUnitsPerDegreeSec,7:F1}  " +
                $"{Localization.Get("MotionYaw")} {(s.GyroYaw - yb) / DualSenseIds.GyroUnitsPerDegreeSec,7:F1}  " +
                $"{Localization.Get("MotionRoll")} {(s.GyroRoll - rb) / DualSenseIds.GyroUnitsPerDegreeSec,7:F1}\n" +
                $"{Localization.Get("MotionAccel")}   X {s.AccelX / DualSenseIds.AccelUnitsPerG,6:F2}  " +
                $"Y {s.AccelY / DualSenseIds.AccelUnitsPerG,6:F2}  " +
                $"Z {s.AccelZ / DualSenseIds.AccelUnitsPerG,6:F2}";

            UpdateTouch(TouchDot1, s.Touch1);
            UpdateTouch(TouchDot2, s.Touch2);
            BatteryText.Text = Localization.Format("Battery", s.BatteryPercent, BatteryStateText(s.Battery));
        }

        SetIndicator("✕", s.Cross);
        SetIndicator("○", s.Circle);
        SetIndicator("□", s.Square);
        SetIndicator("△", s.Triangle);
        SetIndicator("L1", s.L1);
        SetIndicator("R1", s.R1);
        SetIndicator("L2", s.L2Button);
        SetIndicator("R2", s.R2Button);
        SetIndicator("Create", s.Create);
        SetIndicator("Options", s.Options);
        SetIndicator("L3", s.L3);
        SetIndicator("R3", s.R3);
        SetIndicator("PS", s.PS);
        SetIndicator("Touchpad", s.TouchpadClick);
        SetIndicator("Mute", s.MuteButton);
        SetIndicator("↑", s.DPadUp);
        SetIndicator("↓", s.DPadDown);
        SetIndicator("←", s.DPadLeft);
        SetIndicator("→", s.DPadRight);
        SetIndicator("Fn1", s.Fn1);
        SetIndicator("Fn2", s.Fn2);
        SetIndicator("LPaddle", s.LeftPaddle);
        SetIndicator("RPaddle", s.RightPaddle);

        if (_virtualPad.IsConnected)
            _virtualPad.Update(in s, GyroToStickCheck.IsChecked == true);

        if (_a2h is { IsRunning: true } engine)
        {
            int left = (int)(Math.Min(engine.LevelLeft, 1f) * 20);
            int right = (int)(Math.Min(engine.LevelRight, 1f) * 20);
            A2hStatus.Text = $"{Localization.Format("A2hCapturing", engine.CaptureDeviceName)}\n" +
                             $"L |{new string('█', left),-20}| R |{new string('█', right),-20}|";
        }
    }

    private static string BatteryStateText(BatteryState state) => state switch
    {
        BatteryState.Discharging => Localization.Get("BattDischarging"),
        BatteryState.Charging => Localization.Get("BattCharging"),
        BatteryState.Full => Localization.Get("BattFull"),
        _ => Localization.Get("BattUnknown"),
    };

    private static void UpdateStick(Canvas canvas, System.Windows.Shapes.Ellipse dot, byte x, byte y)
    {
        double range = canvas.Width - dot.Width;
        Canvas.SetLeft(dot, x / 255.0 * range);
        Canvas.SetTop(dot, y / 255.0 * range);
    }

    private void UpdateTouch(System.Windows.Shapes.Ellipse dot, TouchPoint point)
    {
        dot.Visibility = point.Active ? Visibility.Visible : Visibility.Collapsed;
        if (!point.Active)
            return;
        double rangeX = TouchCanvas.Width - dot.Width;
        double rangeY = TouchCanvas.Height - dot.Height;
        Canvas.SetLeft(dot, Math.Clamp(point.X / (double)DualSenseIds.TouchpadWidth, 0, 1) * rangeX);
        Canvas.SetTop(dot, Math.Clamp(point.Y / (double)DualSenseIds.TouchpadHeight, 0, 1) * rangeY);
    }

    // ---------- 输出控制 ----------

    private void ApplyOutput(Action<DualSenseOutputState> mutate)
    {
        if (_device is { IsOpen: true })
            _device.UpdateOutput(mutate);
    }

    private void OnRumbleChanged(object sender, RoutedPropertyChangedEventArgs<double> e) =>
        ApplyOutput(o =>
        {
            o.MotorLeft = (byte)RumbleLeft.Value;
            o.MotorRight = (byte)RumbleRight.Value;
        });

    private void OnLightbarChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        var color = Color.FromRgb((byte)LightR.Value, (byte)LightG.Value, (byte)LightB.Value);
        LightbarPreview.Fill = new SolidColorBrush(color);
        ApplyOutput(o =>
        {
            o.LightbarRed = color.R;
            o.LightbarGreen = color.G;
            o.LightbarBlue = color.B;
        });
    }

    private void OnLightbarSetupChanged(object sender, RoutedEventArgs e) =>
        ApplyOutput(o => o.LightbarSetup = LightbarOffCheck.IsChecked == true ? (byte)2 : (byte)0);

    private void OnPlayerLedChanged(object sender, RoutedEventArgs e) =>
        ApplyOutput(o =>
        {
            byte mask = 0;
            foreach ((CheckBox box, int index) in _playerLedBoxes)
                if (box.IsChecked == true)
                    mask |= (byte)(1 << index);
            o.PlayerLeds = mask;
        });

    private void OnMuteLedChanged(object sender, SelectionChangedEventArgs e) =>
        ApplyOutput(o => o.MuteLed = (byte)Math.Max(MuteLedCombo.SelectedIndex, 0));

    private void OnLeftTriggerEffect(object? sender, TriggerEffect effect) =>
        ApplyOutput(o => o.LeftTriggerEffect = effect);

    private void OnRightTriggerEffect(object? sender, TriggerEffect effect) =>
        ApplyOutput(o => o.RightTriggerEffect = effect);

    // ---------- 扳机快速测试 ----------

    private void OnTriggerTestSelected(object sender, SelectionChangedEventArgs e)
    {
        if (TriggerTestList.SelectedItem is not PresetView view)
            return;
        if (_device is not { IsOpen: true })
        {
            StatusText.Text = Localization.Get("ConnectFirstTrigger");
            TriggerTestList.SelectedIndex = -1;
            return;
        }

        ApplyOutput(o =>
        {
            o.LeftTriggerEffect = view.Preset.Make();
            o.RightTriggerEffect = view.Preset.Make();
        });
        StatusText.Text = Localization.Format("TriggerApplied", view.Name, view.Hint);
    }

    private void OnTriggerTestReset(object sender, RoutedEventArgs e)
    {
        TriggerTestList.SelectedIndex = -1;
        ApplyOutput(o =>
        {
            o.LeftTriggerEffect = TriggerEffect.Off();
            o.RightTriggerEffect = TriggerEffect.Off();
        });
        StatusText.Text = Localization.Get("TriggerReset");
    }

    // ---------- HD 触觉 ----------

    private void RefreshHapticsAvailability()
    {
        using var audioDevice = _device is { Connection: ConnectionType.Usb }
            ? DualSenseHapticsOutput.FindAudioDevice()
            : null;

        if (audioDevice != null)
        {
            HapticsEnable.IsEnabled = true;
            A2hEnable.IsEnabled = true;
            HapticsStatus.Text = Localization.Format("HapticsAvailable", audioDevice.FriendlyName);
            RefreshA2hSources();
        }
        else
        {
            HapticsEnable.IsEnabled = false;
            A2hEnable.IsEnabled = false;
            HapticsStatus.Text = _device is { Connection: ConnectionType.Bluetooth }
                ? Localization.Get("HapticsBtUnsupported")
                : Localization.Get("HapticsNoDevice");
        }
    }

    private void RefreshA2hSources()
    {
        foreach (MMDevice source in _a2hSources)
            source.Dispose();
        _a2hSources = AudioToHapticsEngine.ListCaptureSources();

        var names = new List<string> { Localization.Get("SystemDefault") };
        names.AddRange(_a2hSources.Select(d => d.FriendlyName));
        A2hSourceCombo.ItemsSource = names;
        A2hSourceCombo.SelectedIndex = 0;
    }

    private void OnHapticsChecked(object sender, RoutedEventArgs e)
    {
        try
        {
            _haptics.Start();
            // Audio haptics share the actuators with the classic rumble path: flag0
            // bits 0/1 must be cleared or the controller ignores the audio channels.
            ApplyOutput(o =>
            {
                o.EnableCompatibleVibration = false;
                o.EnableHapticsSelect = false;
            });
            PushHapticsSettings();
            RumbleGroup.IsEnabled = false;
            HapticsStatus.Text = Localization.Format("HapticsRunning", _haptics.DeviceName);
        }
        catch (Exception ex)
        {
            HapticsEnable.IsChecked = false;
            HapticsStatus.Text = Localization.Format("StartFailed", ex.Message);
        }
    }

    private void OnHapticsUnchecked(object sender, RoutedEventArgs e)
    {
        if (A2hEnable.IsChecked == true)
            A2hEnable.IsChecked = false; // stops the audio engine first

        bool wasRunning = _haptics.IsRunning;
        _haptics.Stop();
        if (wasRunning)
        {
            ApplyOutput(o =>
            {
                o.EnableCompatibleVibration = true;
                o.EnableHapticsSelect = true;
            });
            RumbleGroup.IsEnabled = true;
            HapticsStatus.Text = Localization.Get("HapticsStopped");
        }
    }

    private void OnHapticsSettingsChanged(object sender, RoutedEventArgs e) => PushHapticsSettings();

    private void PushHapticsSettings()
    {
        HapticsWaveProvider? provider = _haptics.Provider;
        if (provider == null)
            return;
        provider.PulseDecaySeconds = HapticsPulseDecay.Value / 1000.0;
        provider.SetChannel(HapticSide.Left, ReadHapticChannel(HapticsLeftWave, HapticsLeftFreq, HapticsLeftAmp));
        provider.SetChannel(HapticSide.Right, ReadHapticChannel(HapticsRightWave, HapticsRightFreq, HapticsRightAmp));
    }

    private static HapticChannelSettings ReadHapticChannel(ComboBox wave, Slider freq, Slider amp) => new()
    {
        Waveform = (HapticWaveform)Math.Max(wave.SelectedIndex, 0),
        Frequency = freq.Value,
        Amplitude = amp.Value / 100.0,
    };

    private void OnHapticsLeftPulse(object sender, RoutedEventArgs e) =>
        _haptics.Provider?.TriggerPulse(HapticSide.Left);

    private void OnHapticsRightPulse(object sender, RoutedEventArgs e) =>
        _haptics.Provider?.TriggerPulse(HapticSide.Right);

    // ---------- 音频转触觉 ----------

    private void OnA2hChecked(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!_haptics.IsRunning)
                HapticsEnable.IsChecked = true; // starts the stream and switches the HID path
            if (!_haptics.IsRunning)
                throw new InvalidOperationException(Localization.Get("HapticsNotStarted"));

            MMDevice source = A2hSourceCombo.SelectedIndex > 0 && A2hSourceCombo.SelectedIndex <= _a2hSources.Count
                ? _a2hSources[A2hSourceCombo.SelectedIndex - 1]
                : AudioToHapticsEngine.GetDefaultCaptureSource();

            _a2h = new AudioToHapticsEngine(source);
            PushA2hSettings();
            _a2h.Start(_haptics.Provider!.WaveFormat.SampleRate);
            _haptics.Provider!.ExternalSource = _a2h;
            HapticsManualGrid.IsEnabled = false;
            A2hStatus.Text = Localization.Format("A2hCapturing", _a2h.CaptureDeviceName);
        }
        catch (Exception ex)
        {
            _a2h?.Dispose();
            _a2h = null;
            A2hEnable.IsChecked = false;
            A2hStatus.Text = Localization.Format("StartFailed", ex.Message);
        }
    }

    private void OnA2hUnchecked(object sender, RoutedEventArgs e)
    {
        if (_haptics.Provider != null)
            _haptics.Provider.ExternalSource = null;
        _a2h?.Dispose();
        _a2h = null;
        HapticsManualGrid.IsEnabled = true;
        A2hStatus.Text = "";
    }

    private void OnA2hSettingsChanged(object sender, RoutedEventArgs e) => PushA2hSettings();

    private void PushA2hSettings()
    {
        if (_a2h == null)
            return;
        _a2h.Gain = (float)(A2hGain.Value / 100.0);
        _a2h.CutoffHz = new[] { 80, 160, 250, 400 }[Math.Clamp(A2hCutoff.SelectedIndex, 0, 3)];
        _a2h.Mode = (AudioToHapticsMode)Math.Max(A2hMode.SelectedIndex, 0);
        _a2h.MonoMix = A2hMonoMix.IsChecked == true;
        _a2h.GateThreshold = (float)(A2hGate.Value / 100.0);
        _a2h.AttackMs = (float)A2hAttack.Value;
        _a2h.ReleaseMs = (float)A2hRelease.Value;
        _a2h.CarrierHz = (float)A2hCarrier.Value;
    }

    private void OnHapticsPulseDecayChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_haptics.Provider != null)
            _haptics.Provider.PulseDecaySeconds = HapticsPulseDecay.Value / 1000.0;
    }

    // ---------- 虚拟手柄 ----------

    private void OnVirtualPadChecked(object sender, RoutedEventArgs e)
    {
        try
        {
            _virtualPad.Connect();
            VirtualPadStatus.Text = Localization.Get("VirtualStarted");
        }
        catch (Exception ex)
        {
            VirtualPadStatus.Text = Localization.Format("VirtualFailed", ex.Message);
            VirtualPadCheck.IsChecked = false;
        }
    }

    private void OnVirtualPadUnchecked(object sender, RoutedEventArgs e)
    {
        _virtualPad.Dispose();
        VirtualPadStatus.Text = Localization.Get("VirtualStopped");
    }
}
