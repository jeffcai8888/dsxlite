# DsxLite — DualSense PC 工具

一个 DSX(Paliverse)的开源替代实现雏形,让 PS5 DualSense 手柄在 PC 上发挥完整功能。

> 开发者文档(架构、协议细节、线程模型、扩展指南):[docs/DEVELOPMENT.md](docs/DEVELOPMENT.md)

## 功能

- **输入实时显示**:摇杆、扳机、按键(含 DualSense Edge 的 Fn/拨片)、陀螺仪、加速度计、触控板双触点、电量
- **自适应扳机**:9 种效果模式(连续阻力、分段阻力、触感反馈、枪械、振动、弓弦、马蹄、机械等),参数可调
- **震动马达**:大/小马达独立控制
- **灯条 / 玩家指示灯 / 静音键灯**
- **虚拟 Xbox 360 手柄**:通过 ViGEmBus 把 DualSense 映射为 XInput 手柄,支持陀螺仪映射右摇杆
- **多手柄**:设备列表逐个连接/断开,点选切换"活跃"手柄(输入显示与输出控制的目标);虚拟 Xbox 360 手柄每个已连接手柄一个实例;HD 触觉按 USB 实例 ID 匹配到手柄各自的音频端点;自启动时自动连接全部手柄
- **后台运行**:关闭窗口最小化到系统托盘(托盘菜单退出);可选开机自启动(注册表 Run 键,自启动时隐藏到托盘并自动连接手柄)
- **多语言**:简体中文、繁體中文、English、日本語、Français、Español、Português、Deutsch;首次启动跟随系统语言(不支持的语言回退英语),可在界面右上角切换并自动记忆
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

单元测试(xUnit)覆盖扳机效果、USB/蓝牙输出布局、CRC32、六轴校准/降级、输入快照和陀螺仪虚拟摇杆映射；实际用例数量以 `dotnet test` 输出为准：

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

## 六轴工厂校准（第一阶段）

USB 和蓝牙连接均尝试读取 `0x05` 工厂校准报告，统一提供角速度（°/s）与加速度（g）。界面、CLI 和虚拟手柄使用同一帧的校准结果，原始输入数据仍保留。

- 使用 Linux `hid-playstation` 的 DualSense 量程公式；陀螺仪 bias 用于计算比例，不在运行时额外扣除。
- 蓝牙校准报告验证 `0xA3` CRC；读取失败或参数无效时，整组六轴退回标称估算（gyro raw/64、accel raw/8192），界面显示降级，提示中提供诊断原因。
- 简化蓝牙报告没有运动数据，不能将其零值当作校准成功。
- **行为变化**：旧实现错误地将 raw 陀螺仪数据除以 1024。现已统一物理单位，陀螺仪映射仍以 **500°/s 对应右摇杆满偏**，因此手感会比旧版明显灵敏。

只读诊断（不发送灯光、震动、扳机或音频效果；设备索引来自本次枚举）：

```sh
dotnet run --project src/DsxLite.Cli -- --calibration --device 0 --samples 20
```

默认输出 100 次、间隔 100ms；`--samples` 范围 1–10000。等待完整运动报告最多 5 秒，超时或断开返回失败。此模式不能和 `--triggers`、`--haptics` 等效果模式混用。

合成数据测试用于验证算法、错误处理和协议布局，**不代表真机精度验收**。2026-09-28 已在标准 DualSense USB 连接下验证 CLI 工厂校准读取、完整运动报告，以及 WPF 连接显示/断开清除；未验证蓝牙、Edge、多手柄和实际 ViGEm 输出。静置读数仍可存在约 1°/s 的角速度残差，工厂量程校准不等于动态零偏校准。真机还需测试六面重力方向与重连；精确角速度比例需要参考测量。

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
- 六轴已接入工厂量程校准；失败时仅提供标称估算，未实现用户静置零偏校准或姿态融合，USB/蓝牙与不同型号的真机精度仍需逐项验收
- 尚未实现按键重映射与宏(虚拟手柄为固定映射)
- 手柄独占:同一时间只能有一个程序打开手柄
