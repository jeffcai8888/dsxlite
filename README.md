# DsxLite — DualSense PC 工具

一个 DSX(Paliverse)的开源替代实现雏形,让 PS5 DualSense 手柄在 PC 上发挥完整功能。

> 开发者文档(架构、协议细节、线程模型、扩展指南):[docs/DEVELOPMENT.md](docs/DEVELOPMENT.md)

## 功能

- **输入实时显示**:摇杆、扳机、按键(含 DualSense Edge 的 Fn/拨片)、陀螺仪、加速度计、触控板双触点、电量
- **自适应扳机**:9 种效果模式(连续阻力、分段阻力、触感反馈、枪械、振动、弓弦、马蹄、机械等),参数可调
- **震动马达**:大/小马达独立控制
- **灯条 / 玩家指示灯 / 静音键灯**
- **虚拟 Xbox 360 手柄**:通过 ViGEmBus 把 DualSense 映射为 XInput 手柄,支持陀螺仪映射右摇杆
- **HD 触觉(仅 USB)**:向手柄的 4 声道 USB 音频端点推送 PCM 波形(后两声道驱动左右音圈马达),支持正弦/方波/脉冲/噪声、频率与强度调节、单次脉冲。注意:HD 触觉与兼容震动马达互斥(手柄同一时间只走一条通路),启用 HD 触觉时普通震动会暂停
- **音频转触觉(仅 USB)**:WASAPI 环回捕获系统声音,经低通滤波(80-400Hz 可选)和增益后实时驱动音圈马达;可选"波形(质感)"或"包络(冲击)"模式,左右马达跟随立体声声像。可调参数:增益、低通截止、噪声门限(低于阈值不震)、包络攻击/释放时间、载波频率、左右联动(单声道混合)
- **USB 和蓝牙**两种连接方式(蓝牙自动读取校准报告以启用完整数据;蓝牙同样支持自适应扳机)

## 项目结构

```
src/
  DsxLite.Core/   DualSense HID 协议层 + ViGEm 封装(net9.0)
  DsxLite.App/    WPF 图形界面(net9.0-windows)
  DsxLite.Cli/    命令行诊断工具
```

## 构建与运行

需要 .NET 9 SDK:

```
dotnet build
dotnet run --project src/DsxLite.App    # 图形界面
dotnet run --project src/DsxLite.Cli    # 命令行(实时打印输入)
dotnet run --project src/DsxLite.Cli -- --vigem     # 同时输出到虚拟手柄
dotnet run --project src/DsxLite.Cli -- --triggers  # 自适应扳机硬件测试
```

## 测试

单元测试(xUnit,24 个用例)覆盖扳机效果参数钳制、模式字节、USB/蓝牙输出报告布局和 CRC32:

```
dotnet test
```

真机验证自适应扳机(需要连接手柄):`--triggers` 模式会把 9 种效果依次同时应用到 L2 和 R2,按任意键切换、`Q` 结束,退出时自动复位:

```
dotnet run --project src/DsxLite.Cli -- --triggers
```

HD 触觉(需 USB 连接):

```
dotnet run --project src/DsxLite.Cli -- --haptics         # 左右马达交替脉冲
dotnet run --project src/DsxLite.Cli -- --haptics-probe   # 声道/路由组合探测(调试用)
dotnet run --project src/DsxLite.Cli -- --a2h             # 音频转触觉(带 VU 电平显示)
```

经真机探测确认:触觉由后两声道(3/4)驱动,且 HID 输出报告 flag0 的兼容震动位(bit0/1)必须清零,否则执行器被锁定在兼容震动通路、忽略音频数据。启用 HD 触觉时代码会自动清除这两个位。

## 前置条件

- **手柄连接**:USB 直连,或蓝牙配对(长按 PS + Create 进入配对模式)
- **占用冲突**:Steam 输入、DS4Windows、DSX 会独占手柄,使用前请关闭
- **虚拟手柄(可选)**:安装 [ViGEmBus](https://github.com/nefarius/ViGEmBus/releases) 驱动;未安装时其余功能不受影响

## 协议参考

实现依据公开的逆向工程资料:

- Linux 内核 [hid-playstation](https://github.com/torvalds/linux/blob/master/drivers/hid/hid-playstation.c) 驱动(输入/输出报告布局、CRC32 签名)
- [nondebug/dualsense](https://github.com/nondebug/dualsense)(HID 报告描述符)
- DS4Windows 的自适应扳机效果模式定义

USB 输出报告为 `0x02` + 47 字节载荷;蓝牙输出报告为 `0x31` + 序号 + `0x10` 标签 + 载荷 + CRC32(种子 `0xA2`)。扳机效果块位于载荷偏移 10(R2)和 20(L2),各占 10 字节。

## 已知限制

- **蓝牙下无 HD 触觉**:蓝牙连接时手柄不向 Windows 暴露音频通道,HD 触觉仅 USB 可用(协议层的自适应扳机、普通震动、灯条等在蓝牙下正常)
- 蓝牙下音频(耳机口/扬声器)未实现
- 陀螺仪/加速度计仅应用零偏校准,未做满量程校准
- 尚未实现按键重映射与宏(虚拟手柄为固定映射)
- 手柄独占:同一时间只能有一个程序打开手柄
