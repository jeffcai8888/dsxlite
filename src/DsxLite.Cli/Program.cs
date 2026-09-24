using DsxLite.Core.DualSense;
using DsxLite.Core.Haptics;
using DsxLite.Core.ViGEm;

Console.OutputEncoding = System.Text.Encoding.UTF8;
Console.WriteLine("DsxLite CLI - DualSense 诊断工具");
Console.WriteLine();

if (args.Contains("--audio"))
{
    Console.WriteLine("系统中的音频输出设备:");
    foreach (string line in DualSenseHapticsOutput.DescribeRenderDevices())
        Console.WriteLine($"  {line}");
    Console.WriteLine();
    using var found = DualSenseHapticsOutput.FindAudioDevice();
    Console.WriteLine(found != null
        ? $"匹配到手柄音频设备: {found.FriendlyName}"
        : "未匹配到手柄音频设备(HD 触觉需要 USB 连接的手柄)。");
    return 0;
}

if (args.Contains("--a2h"))
{
    Console.WriteLine("音频转触觉:捕获系统默认输出设备,按 Ctrl+C 结束");

    DualSenseDevice? hidDevice = DualSenseEnumerator.FindAll().FirstOrDefault();
    if (hidDevice != null && hidDevice.Open())
    {
        hidDevice.UpdateOutput(o =>
        {
            o.EnableCompatibleVibration = false;
            o.EnableHapticsSelect = false;
        });
        Console.WriteLine("已切换手柄到音频触觉通路。");
    }

    using var haptics = new DualSenseHapticsOutput();
    try
    {
        haptics.Start();
    }
    catch (Exception ex)
    {
        Console.WriteLine($"启动失败: {ex}");
        return 1;
    }

    using var engine = new AudioToHapticsEngine(AudioToHapticsEngine.GetDefaultCaptureSource());
    engine.Start(haptics.Provider!.WaveFormat.SampleRate);
    haptics.Provider!.ExternalSource = engine;

    Console.WriteLine($"音源: {engine.CaptureDeviceName} → {haptics.DeviceName}");
    Console.WriteLine("播放一些声音(音乐/游戏),应能在手柄上感到低频触感。");
    Console.WriteLine();

    while (true)
    {
        int left = (int)(Math.Min(engine.LevelLeft, 1f) * 40);
        int right = (int)(Math.Min(engine.LevelRight, 1f) * 40);
        Console.Write($"\rL |{new string('#', left),-40}|  R |{new string('#', right),-40}|");
        Thread.Sleep(50);
    }
}

if (args.Contains("--haptics"))
{
    Console.WriteLine("HD 触觉测试:左右马达交替脉冲,按 Ctrl+C 结束");

    // Audio haptics require flag0 bits 0/1 cleared; best effort via the HID interface
    // (harmless if another app holds the device, power-on default is already cleared).
    DualSenseDevice? hidDevice = DualSenseEnumerator.FindAll().FirstOrDefault();
    if (hidDevice != null && hidDevice.Open())
    {
        hidDevice.UpdateOutput(o =>
        {
            o.EnableCompatibleVibration = false;
            o.EnableHapticsSelect = false;
        });
        Console.WriteLine("已切换手柄到音频触觉通路。");
    }

    using var haptics = new DualSenseHapticsOutput();
    try
    {
        haptics.Start();
    }
    catch (Exception ex)
    {
        Console.WriteLine($"启动失败: {ex}");
        return 1;
    }
    Console.WriteLine($"音频设备: {haptics.DeviceName}");
    HapticsWaveProvider provider = haptics.Provider!;
    provider.SetChannel(HapticSide.Left, new HapticChannelSettings { Frequency = 80 });
    provider.SetChannel(HapticSide.Right, new HapticChannelSettings { Frequency = 80 });
    var side = HapticSide.Left;
    while (true)
    {
        provider.TriggerPulse(side);
        Console.WriteLine($"脉冲: {(side == HapticSide.Left ? "左" : "右")}");
        side = side == HapticSide.Left ? HapticSide.Right : HapticSide.Left;
        Thread.Sleep(800);
    }
}

bool useVigem = args.Contains("--vigem");

List<DualSenseDevice> devices = DualSenseEnumerator.FindAll();
if (devices.Count == 0)
{
    Console.WriteLine("未找到 DualSense 手柄。请通过 USB 连接或蓝牙配对后重试。");
    return 1;
}

Console.WriteLine($"找到 {devices.Count} 个设备:");
for (int i = 0; i < devices.Count; i++)
    Console.WriteLine($"  [{i}] {devices[i].DisplayName}");

DualSenseDevice device = devices[0];
Console.WriteLine();
Console.WriteLine($"连接: {device.DisplayName}");

if (!device.Open())
{
    Console.WriteLine("打开设备失败(可能被其他程序占用,如 Steam / DS4Windows / DSX,请关闭后重试)。");
    return 1;
}

VirtualXbox360? vigem = null;
if (useVigem)
{
    try
    {
        vigem = new VirtualXbox360();
        vigem.Connect();
        Console.WriteLine("虚拟 Xbox 360 手柄已创建。");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"虚拟手柄创建失败(需要安装 ViGEmBus 驱动): {ex.Message}");
        vigem = null;
    }
}

Console.WriteLine("读取输入中,按 Ctrl+C 退出...");
Console.WriteLine();

if (args.Contains("--triggers"))
{
    RunTriggerTest(device);
    device.Dispose();
    return 0;
}

if (args.Contains("--haptics-probe"))
{
    RunHapticsProbe(device);
    device.Dispose();
    return 0;
}

using var exit = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    exit.Cancel();
};

device.Disconnected += (_, _) =>
{
    Console.WriteLine();
    Console.WriteLine("设备已断开。");
    exit.Cancel();
};

while (!exit.IsCancellationRequested && device.IsOpen)
{
    DualSenseInputState s = device.CurrentState;
    vigem?.Update(in s);

    string buttons = string.Join(' ', new[]
    {
        s.Cross ? "✕" : null, s.Circle ? "○" : null, s.Square ? "□" : null, s.Triangle ? "△" : null,
        s.L1 ? "L1" : null, s.R1 ? "R1" : null, s.L2Button ? "L2" : null, s.R2Button ? "R2" : null,
        s.Create ? "Create" : null, s.Options ? "Options" : null, s.L3 ? "L3" : null, s.R3 ? "R3" : null,
        s.PS ? "PS" : null, s.TouchpadClick ? "Touch" : null, s.MuteButton ? "Mute" : null,
        s.DPadUp ? "↑" : null, s.DPadDown ? "↓" : null, s.DPadLeft ? "←" : null, s.DPadRight ? "→" : null,
    }.Where(b => b != null));

    string line = s.IsFullReport
        ? $"LS({s.LeftStickX,3},{s.LeftStickY,3}) RS({s.RightStickX,3},{s.RightStickY,3}) " +
          $"LT {s.LeftTrigger,3} RT {s.RightTrigger,3} " +
          $"陀螺({s.GyroPitch,6},{s.GyroYaw,6},{s.GyroRoll,6}) " +
          $"电量 {s.BatteryPercent}%/{s.Battery} 按键: {buttons}"
        : $"[简化报告] LS({s.LeftStickX,3},{s.LeftStickY,3}) RS({s.RightStickX,3},{s.RightStickY,3}) " +
          $"LT {s.LeftTrigger,3} RT {s.RightTrigger,3} 按键: {buttons}";

    Console.Write($"\r{line.PadRight(Console.WindowWidth - 1)}");
    Thread.Sleep(16);
}

vigem?.Dispose();
device.Dispose();
return 0;

/// <summary>
/// 交互式自适应扳机测试:把每种效果依次应用到 L2 和 R2,按任意键切换,Q 结束。
/// </summary>
static void RunTriggerTest(DualSenseDevice device)
{
    Console.WriteLine("===== 自适应扳机测试 =====");
    Console.WriteLine("每个效果会同时应用到 L2 和 R2,请按下扳机感受差异。");
    Console.WriteLine();

    try
    {
        for (int i = 0; i < TriggerEffectPresets.All.Length; i++)
        {
            TriggerEffectPreset preset = TriggerEffectPresets.All[i];
            device.UpdateOutput(o =>
            {
                o.LeftTriggerEffect = preset.Make();
                o.RightTriggerEffect = preset.Make();
            });

            Console.WriteLine($"[{i + 1}/{TriggerEffectPresets.All.Length}] {preset.Name} — {preset.Hint}");
            Console.Write("    按任意键下一个,Q 结束: ");

            ConsoleKeyInfo key = Console.ReadKey(intercept: true);
            Console.WriteLine();
            Console.WriteLine();
            if (key.Key is ConsoleKey.Q)
                break;
        }
    }
    finally
    {
        device.UpdateOutput(o =>
        {
            o.LeftTriggerEffect = TriggerEffect.Off();
            o.RightTriggerEffect = TriggerEffect.Off();
        });
        Console.WriteLine("已复位扳机效果。");
    }
}

/// <summary>
/// 交互式 HD 触觉探测:逐步切换声道映射和 HID 音频路由配置,每步播放 80Hz 持续音,
/// 用户感受哪一步有震动,从而确定正确的配置组合。
/// </summary>
static void RunHapticsProbe(DualSenseDevice device)
{
    using var haptics = new DualSenseHapticsOutput();
    try
    {
        haptics.Start();
    }
    catch (Exception ex)
    {
        Console.WriteLine($"启动失败: {ex}");
        return;
    }
    Console.WriteLine($"音频设备: {haptics.DeviceName}");
    Console.WriteLine();

    HapticsWaveProvider provider = haptics.Provider!;

    void SetChannels(int left, int right)
    {
        provider.LeftOutputChannel = left;
        provider.RightOutputChannel = right;
    }

    void SetHid(bool compatibleVibration, bool hapticsSelect, bool audioRoute = false)
    {
        device.UpdateOutput(o =>
        {
            o.EnableCompatibleVibration = compatibleVibration;
            o.EnableHapticsSelect = hapticsSelect;
            o.EnableAudioControl = audioRoute;
            o.AudioControl = 0x30; // 路由到内置扬声器
            o.EnableSpeakerVolume = audioRoute;
            o.SpeakerVolume = 0x64;
            o.EnableAudioControl2 = audioRoute;
            o.AudioControl2 = 0x02; // 扬声器前置增益 +6dB
        });
    }

    var tone = new HapticChannelSettings
    {
        Waveform = HapticWaveform.Sine,
        Frequency = 80,
        Amplitude = 1.0,
    };
    var silence = new HapticChannelSettings();

    (string Desc, Action Apply)[] steps =
    [
        ("声道 3/4 + 默认 HID(flag0=0x03 兼容震动)", () => { SetChannels(2, 3); SetHid(true, true); }),
        ("声道 1/2 + 默认 HID", () => { SetChannels(0, 1); }),
        ("声道 3/4 + flag0 清零(关闭兼容震动/触觉选择)", () => { SetChannels(2, 3); SetHid(false, false); }),
        ("声道 1/2 + flag0 清零", () => { SetChannels(0, 1); }),
        ("声道 3/4 + flag0 清零 + 音频路由到扬声器/音量最大", () => { SetChannels(2, 3); SetHid(false, false, audioRoute: true); }),
        ("声道 1/2 + 同上音频路由", () => { SetChannels(0, 1); }),
        ("声道 3/4 + 默认 HID + 音频路由/音量最大", () => { SetChannels(2, 3); SetHid(true, true, audioRoute: true); }),
    ];

    Console.WriteLine("===== HD 触觉探测 =====");
    Console.WriteLine("每一步会播放 80Hz 持续音。拿起手柄感受,记住有震动的步骤编号。");
    Console.WriteLine();

    try
    {
        for (int i = 0; i < steps.Length; i++)
        {
            (string desc, Action apply) = steps[i];
            apply();
            provider.SetChannel(HapticSide.Left, tone);
            provider.SetChannel(HapticSide.Right, tone);

            Console.WriteLine($"[{i + 1}/{steps.Length}] {desc}");
            Console.Write("    正在输出…按任意键下一步: ");
            Console.ReadKey(intercept: true);
            Console.WriteLine();
            Console.WriteLine();

            provider.SetChannel(HapticSide.Left, silence);
            provider.SetChannel(HapticSide.Right, silence);
        }
    }
    finally
    {
        provider.SetChannel(HapticSide.Left, silence);
        provider.SetChannel(HapticSide.Right, silence);
        SetChannels(HapticsWaveProvider.LeftHapticChannel, HapticsWaveProvider.RightHapticChannel);
        SetHid(true, true);
        Console.WriteLine("已复位。请告诉我哪些步骤有震感。");
    }
}
