using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using DsxLite.Core.DualSense;
using DsxLite.Core.Haptics;
using DsxLite.Core.ViGEm;

namespace DsxLite.App;

public partial class MainWindow : Window
{
    private static readonly Brush IndicatorOff = new SolidColorBrush(Color.FromRgb(0xEE, 0xEE, 0xEE));
    private static readonly Brush IndicatorOn = new SolidColorBrush(Color.FromRgb(0x3B, 0x8E, 0xD0));

    private readonly Dictionary<string, Border> _indicators = new();
    private readonly Dictionary<CheckBox, int> _playerLedBoxes = new();
    private readonly DispatcherTimer _timer;
    private readonly VirtualXbox360 _virtualPad = new();

    private List<DualSenseDevice> _devices = [];
    private DualSenseDevice? _device;
    private readonly DualSenseHapticsOutput _haptics = new();

    public MainWindow()
    {
        InitializeComponent();

        foreach (string name in new[]
                 {
                     "✕", "○", "□", "△", "L1", "R1", "L2", "R2",
                     "Create", "Options", "L3", "R3", "PS", "触控板", "静音",
                     "↑", "↓", "←", "→",
                     "Fn1", "Fn2", "左拨片", "右拨片",
                 })
        {
            Border indicator = MakeIndicator(name);
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
        TriggerTestList.ItemsSource = TriggerEffectPresets.All;

        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
        _timer.Tick += OnTick;

        Loaded += (_, _) => RefreshDevices();
        Closing += OnWindowClosing;
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
            StatusText.Text = $"枚举设备失败: {ex.Message}";
        }

        DeviceCombo.ItemsSource = _devices.Select(d => d.DisplayName).ToList();
        if (_devices.Count > 0)
            DeviceCombo.SelectedIndex = 0;
        StatusText.Text = _devices.Count > 0
            ? $"找到 {_devices.Count} 个手柄"
            : "未找到 DualSense,请连接 USB 或完成蓝牙配对后点击刷新";
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
            StatusText.Text = "请先选择设备";
            return;
        }

        DualSenseDevice device = _devices[DeviceCombo.SelectedIndex];
        if (!device.Open())
        {
            StatusText.Text = "打开失败:设备可能被 Steam / DS4Windows / DSX 占用,请关闭后重试";
            return;
        }

        _device = device;
        _device.Disconnected += OnDeviceDisconnected;
        ConnectButton.Content = "断开";
        RefreshButton.IsEnabled = false;
        DeviceCombo.IsEnabled = false;
        StatusText.Text = $"已连接 {_device.DisplayName}";
        RefreshHapticsAvailability();
        _timer.Start();
    }

    private void DisconnectDevice()
    {
        _timer.Stop();
        _haptics.Stop();
        HapticsEnable.IsChecked = false;
        HapticsEnable.IsEnabled = false;
        HapticsStatus.Text = "USB 连接后可用";
        if (_device != null)
        {
            _device.Disconnected -= OnDeviceDisconnected;
            _device.Dispose();
            _device = null;
        }
        ConnectButton.Content = "连接";
        RefreshButton.IsEnabled = true;
        DeviceCombo.IsEnabled = true;
        BatteryText.Text = "";
        StatusText.Text = "已断开";
    }

    private void OnDeviceDisconnected(object? sender, EventArgs e) =>
        Dispatcher.BeginInvoke(DisconnectDevice);

    private void OnWindowClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        _timer.Stop();
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
                $"陀螺 °/s  俯仰 {(s.GyroPitch - pb) / DualSenseIds.GyroUnitsPerDegreeSec,7:F1}  " +
                $"偏航 {(s.GyroYaw - yb) / DualSenseIds.GyroUnitsPerDegreeSec,7:F1}  " +
                $"翻滚 {(s.GyroRoll - rb) / DualSenseIds.GyroUnitsPerDegreeSec,7:F1}\n" +
                $"加速度 g   X {s.AccelX / DualSenseIds.AccelUnitsPerG,6:F2}  " +
                $"Y {s.AccelY / DualSenseIds.AccelUnitsPerG,6:F2}  " +
                $"Z {s.AccelZ / DualSenseIds.AccelUnitsPerG,6:F2}";

            UpdateTouch(TouchDot1, s.Touch1);
            UpdateTouch(TouchDot2, s.Touch2);
            BatteryText.Text = $"电量 {s.BatteryPercent}% ({BatteryText_(s.Battery)})";
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
        SetIndicator("触控板", s.TouchpadClick);
        SetIndicator("静音", s.MuteButton);
        SetIndicator("↑", s.DPadUp);
        SetIndicator("↓", s.DPadDown);
        SetIndicator("←", s.DPadLeft);
        SetIndicator("→", s.DPadRight);
        SetIndicator("Fn1", s.Fn1);
        SetIndicator("Fn2", s.Fn2);
        SetIndicator("左拨片", s.LeftPaddle);
        SetIndicator("右拨片", s.RightPaddle);

        if (_virtualPad.IsConnected)
            _virtualPad.Update(in s, GyroToStickCheck.IsChecked == true);
    }

    private static string BatteryText_(BatteryState state) => state switch
    {
        BatteryState.Discharging => "放电中",
        BatteryState.Charging => "充电中",
        BatteryState.Full => "已充满",
        _ => "未知",
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
        if (TriggerTestList.SelectedItem is not TriggerEffectPreset preset)
            return;
        if (_device is not { IsOpen: true })
        {
            StatusText.Text = "请先连接手柄再测试扳机效果";
            TriggerTestList.SelectedIndex = -1;
            return;
        }

        ApplyOutput(o =>
        {
            o.LeftTriggerEffect = preset.Make();
            o.RightTriggerEffect = preset.Make();
        });
        StatusText.Text = $"扳机效果:{preset.Name} — {preset.Hint}";
    }

    private void OnTriggerTestReset(object sender, RoutedEventArgs e)
    {
        TriggerTestList.SelectedIndex = -1;
        ApplyOutput(o =>
        {
            o.LeftTriggerEffect = TriggerEffect.Off();
            o.RightTriggerEffect = TriggerEffect.Off();
        });
        StatusText.Text = "扳机效果已复位";
    }

    // ---------- HD 触觉 ----------

    private void RefreshHapticsAvailability()
    {
        if (_device is { Connection: ConnectionType.Usb } && DualSenseHapticsOutput.FindAudioDevice() != null)
        {
            HapticsEnable.IsEnabled = true;
            HapticsStatus.Text = "已检测到手柄音频设备,可启用 HD 触觉";
        }
        else
        {
            HapticsEnable.IsEnabled = false;
            HapticsStatus.Text = _device is { Connection: ConnectionType.Bluetooth }
                ? "HD 触觉需要 USB 连接(蓝牙下手柄不暴露音频通道)"
                : "未找到手柄音频设备";
        }
    }

    private void OnHapticsChecked(object sender, RoutedEventArgs e)
    {
        try
        {
            _haptics.Start();
            PushHapticsSettings();
            HapticsStatus.Text = $"HD 触觉运行中:{_haptics.DeviceName}";
        }
        catch (Exception ex)
        {
            HapticsStatus.Text = $"启动失败:{ex.Message}";
            HapticsEnable.IsChecked = false;
        }
    }

    private void OnHapticsUnchecked(object sender, RoutedEventArgs e)
    {
        _haptics.Stop();
        if (HapticsEnable.IsEnabled)
            HapticsStatus.Text = "HD 触觉已停止";
    }

    private void OnHapticsSettingsChanged(object sender, RoutedEventArgs e) => PushHapticsSettings();

    private void PushHapticsSettings()
    {
        HapticsWaveProvider? provider = _haptics.Provider;
        if (provider == null)
            return;
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

    // ---------- 虚拟手柄 ----------

    private void OnVirtualPadChecked(object sender, RoutedEventArgs e)
    {
        try
        {
            _virtualPad.Connect();
            VirtualPadStatus.Text = "虚拟 Xbox 360 手柄已连接,游戏中即可使用。";
        }
        catch (Exception ex)
        {
            VirtualPadStatus.Text = $"创建虚拟手柄失败:{ex.Message}(请先安装 ViGEmBus 驱动,详见 README)";
            VirtualPadCheck.IsChecked = false;
        }
    }

    private void OnVirtualPadUnchecked(object sender, RoutedEventArgs e)
    {
        _virtualPad.Dispose();
        VirtualPadStatus.Text = "虚拟手柄已断开。";
    }
}
