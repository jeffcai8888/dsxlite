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

if (args.Contains("--haptics"))
{
    Console.WriteLine("HD 触觉测试:左右马达交替脉冲,按 Ctrl+C 结束");
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
