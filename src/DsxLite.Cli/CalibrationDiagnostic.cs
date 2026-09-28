using DsxLite.Core.DualSense;

namespace DsxLite.Cli;

internal static class CalibrationDiagnostic
{
    private const int DefaultSamples = 100;
    private const int MaximumSamples = 10000;
    private const int SampleIntervalMs = 100;
    private static readonly TimeSpan InputTimeout = TimeSpan.FromSeconds(5);

    public static int Run(string[] args)
    {
        if (!TryReadOptions(args, out int deviceIndex, out int samples))
        {
            Console.Error.WriteLine("用法: --calibration [--device 非负索引] [--samples 1..10000]");
            return 2;
        }

        List<DualSenseDevice> devices = DualSenseEnumerator.FindAll();
        for (int i = 0; i < devices.Count; i++)
            Console.WriteLine($"[{i}] {devices[i].DisplayName}  {devices[i].DevicePath}");
        if (deviceIndex >= devices.Count)
        {
            Console.Error.WriteLine("所选设备不存在。请连接手柄并检查设备索引。");
            return 1;
        }

        using DualSenseDevice device = devices[deviceIndex];
        if (!device.Open())
        {
            Console.Error.WriteLine("无法打开所选设备，请检查连接与占用情况。");
            return 1;
        }
        Console.WriteLine($"校准诊断: {device.DisplayName} / {device.Connection}，不发送效果输出。");
        return ReadSamples(device, samples);
    }

    internal static bool TryReadOptions(string[] args, out int deviceIndex, out int samples)
    {
        deviceIndex = 0;
        samples = DefaultSamples;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < args.Length; i++)
        {
            string option = args[i];
            if (!seen.Add(option)) return false;
            if (option == "--calibration") continue;
            if (option is not ("--device" or "--samples") || ++i >= args.Length ||
                !int.TryParse(args[i], out int value)) return false;
            if (option == "--device")
            {
                if (value < 0) return false;
                deviceIndex = value;
            }
            else
            {
                if (value is < 1 or > MaximumSamples) return false;
                samples = value;
            }
        }
        return seen.Contains("--calibration");
    }

    private static int ReadSamples(DualSenseDevice device, int samples)
    {
        using var exit = new CancellationTokenSource();
        ConsoleCancelEventHandler cancel = (_, e) => { e.Cancel = true; exit.Cancel(); };
        EventHandler disconnected = (_, _) => exit.Cancel();
        Console.CancelKeyPress += cancel;
        device.Disconnected += disconnected;
        try
        {
            var timer = System.Diagnostics.Stopwatch.StartNew();
            while (device.CurrentSnapshot.Motion is null && timer.Elapsed < InputTimeout &&
                   !exit.IsCancellationRequested)
                exit.Token.WaitHandle.WaitOne(SampleIntervalMs);

            if (device.CurrentSnapshot.Motion is null)
            {
                PrintSample(device.CurrentSnapshot);
                Console.Error.WriteLine("未收到完整运动报告（超时或设备断开），不能完成传感器验证。");
                return 1;
            }
            for (int i = 0; i < samples && !exit.IsCancellationRequested; i++)
            {
                DualSenseInputSnapshot snapshot = device.CurrentSnapshot;
                PrintSample(snapshot);
                if (snapshot.Motion is null)
                {
                    Console.Error.WriteLine("完整运动报告已丢失，诊断未完成。");
                    return 1;
                }
                if (i + 1 < samples) exit.Token.WaitHandle.WaitOne(SampleIntervalMs);
            }
            return exit.IsCancellationRequested ? 1 : 0;
        }
        finally
        {
            Console.CancelKeyPress -= cancel;
            device.Disconnected -= disconnected;
        }
    }

    private static void PrintSample(DualSenseInputSnapshot snapshot)
    {
        DualSenseInputState raw = snapshot.Raw;
        Console.WriteLine($"校准={snapshot.Calibration.Status} 原因={snapshot.Calibration.FailureReason} " +
                          $"完整报告={raw.IsFullReport}");
        if (snapshot.Motion is not { } motion)
        {
            Console.WriteLine("运动数据不可用。");
            return;
        }
        Console.WriteLine($"raw gyro=({raw.GyroPitch},{raw.GyroYaw},{raw.GyroRoll}) " +
                          $"accel=({raw.AccelX},{raw.AccelY},{raw.AccelZ})");
        Console.WriteLine($"°/s=({motion.GyroPitchDps:F3},{motion.GyroYawDps:F3},{motion.GyroRollDps:F3}) " +
                          $"g=({motion.AccelXG:F4},{motion.AccelYG:F4},{motion.AccelZG:F4})");
    }
}
