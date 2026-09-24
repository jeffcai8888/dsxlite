# DsxLite 开发文档

本文面向贡献者，记录架构、DualSense 协议实现细节（含真机验证结论）和开发流程。

## 目录

- [架构总览](#架构总览)
- [DualSense 协议](#dualsense-协议)
- [HD 触觉](#hd-触觉)
- [音频转触觉 DSP 管线](#音频转触觉-dsp-管线)
- [线程模型](#线程模型)
- [开发流程](#开发流程)
- [扩展指南](#扩展指南)

## 架构总览

```
src/
  DsxLite.Core/       协议与硬件层(net9.0-windows)
    DualSense/        HID 协议:枚举、连接、输入解析、输出报告、扳机效果、CRC32
    Haptics/          HD 触觉:波形生成、WASAPI 输出、音频转触觉引擎、DSP
    ViGEm/            虚拟 Xbox 360 手柄封装
  DsxLite.App/        WPF 界面(代码后置 + DispatcherTimer 轮询,无 MVVM 框架)
  DsxLite.Cli/        命令行诊断/硬件测试工具
  DsxLite.Tests/      xUnit 单元测试(44 个)
```

依赖:

| 包 | 用途 |
|---|---|
| HidSharp 2.6.4 | HID 读写(USB/蓝牙) |
| NAudio 3.1.0 | WASAPI 音频输出(触觉)、环回捕获(音频转触觉) |
| Nefarius.ViGEm.Client 1.21.256 | 虚拟手柄(需要 ViGEmBus 驱动) |

分层原则:**App/CLI 不碰协议细节**,一切字节布局都在 Core。

## DualSense 协议

实现依据 [Linux 内核 hid-playstation 驱动](https://github.com/torvalds/linux/blob/master/drivers/hid/hid-playstation.c)(最权威)和 [nondebug/dualsense](https://github.com/nondebug/dualsense)。VID `0x054C`,PID `0x0CE6`(Edge 为 `0x0DF2`)。

### 输入报告

| 连接 | Report ID | 长度 | 公共数据偏移 |
|---|---|---|---|
| USB | `0x01` | 64 | 1 |
| 蓝牙(完整) | `0x31` | 78 | 2(末尾 4 字节 CRC32,种子 `0xA1`) |
| 蓝牙(截断) | `0x01` | 10 | 初始状态,读取特性报告 `0x05` 后切换到完整报告 |

公共输入结构关键偏移(相对公共数据起点):

| 偏移 | 内容 |
|---|---|
| 0-3 | 左右摇杆 XY |
| 4-5 | L2/R2 模拟量 |
| 7 | buttons0:低 4 位方向键 hat(0-7 顺时针,8 居中),bit4-7 = □✕○△ |
| 8 | buttons1:L1 R1 L2 R2 Create Options L3 R3 |
| 9 | buttons2:bit0 PS、bit1 触控板点击、bit2 静音;Edge 的 bit4-7 = Fn1/Fn2/左右拨片 |
| 15-26 | 陀螺仪、加速度计(int16 LE ×3) |
| 32-39 | 两个触控点(各 4 字节:contact+id、x 低 8 位、x 高 4 位 + y 低 4 位、y 高 8 位) |
| 52 | 电池:低 4 位电量(0-10),高 4 位充电状态(0 放电/1 充电/2 已满) |

解析器:`DualSenseReportParser.TryParse`。陀螺仪零偏来自特性报告 `0x05`(蓝牙连接时必须读取以解锁完整报告)。

### 输出报告

47 字节公共载荷。USB 前缀 `0x02`(共 48 字节);蓝牙包装为 `0x31` + 序号(高 4 位,0-15 循环)+ 魔数 `0x10` + 载荷 + CRC32(种子 `0xA2`,见 `Crc32.cs`)。

载荷关键偏移:

| 偏移 | 内容 |
|---|---|
| 0 | valid_flag0:bit0 兼容震动、bit1 震动通路选择、bit2/3 右/左扳机电机使能、bit5 扬声器音量、bit7 音频控制 |
| 1 | valid_flag1:bit0 静音键灯、bit2 灯条、bit4 玩家灯、bit7 audio_control2 |
| 2-3 | 右/左震动电机 |
| 5,7 | 扬声器音量、音频路由 |
| 8 | 静音键灯(0 灭/1 亮/2 呼吸) |
| 10-19 | **R2 扳机效果块**(模式 + 9 参数) |
| 20-29 | **L2 扳机效果块** |
| 37-38 | audio_control2、valid_flag2(bit1 灯条设置) |
| 41-46 | 灯条设置、LED 亮度、玩家灯 5 位掩码、RGB |

扳机效果模式(`TriggerEffects.cs`):`0x00` 关闭、`0x01` 连续阻力、`0x02` 分段阻力、`0x21` 触感反馈、`0x22` 枪械、`0x23` 振动、`0x25` 弓弦、`0x26` 马蹄、`0x27` 机械。参数范围见 `GetParamSpec`,工厂方法自动钳制。预设清单 `TriggerEffectPresets.All` 由 CLI 和 GUI 共用。

> **真机结论(重要)**:flag0 bit0/bit1 置位时执行器被锁定在兼容震动通路,**音频触觉被忽略**。启用 HD 触觉必须清零这两位(见 `MainWindow.OnHapticsChecked`)。两条通路互斥。

### 连接管理

`DualSenseDevice`:后台读线程(`HidStream.Read`,1s 超时轮询),`Disconnected` 事件;输出经 `UpdateOutput(mutate)` 串行化(锁 + 立即发送)。`DualSenseEnumerator` 按最大输入报告长度区分 USB(64)/蓝牙(78),过滤掉多余的 HID collection。

## HD 触觉

原理:USB 连接时手柄暴露 **4 声道音频渲染端点**("Wireless Controller")。声道 1/2 = 扬声器/耳机,**声道 3/4 = 左右音圈马达**。

实现(`DualSenseHapticsOutput`):

- 波形生成器 `HapticsWaveProvider` 输出 float 采样,声道 0/1 恒静音
- **混音格式必须字节级克隆设备 mix format**——索尼驱动对普通 WAVEFORMATEX float 返回 `E_INVALIDARG`(`ExactFormatFloatWrapper`);PCM 16/24/32 混音格式用 NAudio 转换器适配
- 共享模式优先;格式不可用回退独占模式 48k/16/4
- 启动时取消静音并拉满端点音量(**触觉幅度跟随音频音量**)
- 4 种波形 + 单次衰减脉冲(`PulseDecaySeconds` 可调)

## 音频转触觉 DSP 管线

```
WASAPI 环回捕获(任意渲染端点)
  → float 立体声(多声道取 0/1)
  → 双二阶 Butterworth 低通(截止 80-400Hz)
  → 可选左右联动(单声道混合)
  → 包络跟随(攻击/释放可调)+ 噪声门限
  → 波形模式:直出 × 增益;包络模式:包络 × 载波(40-200Hz)× 增益
  → 采样率不匹配时线性重采样
  → StereoRingBuffer(写:捕获线程;读:音频渲染线程,溢出丢最旧、欠载补零)
  → HapticsWaveProvider.ExternalSource
```

`AudioToHapticsEngine` 的所有参数(`Gain/CutoffHz/Mode/AttackMs/ReleaseMs/CarrierHz/GateThreshold/MonoMix`)运行时实时生效,设置器直接重配滤波器实例,无对象替换开销。

## 线程模型

| 线程 | 职责 | 同步 |
|---|---|---|
| UI | 控件、30Hz DispatcherTimer 刷新显示、喂虚拟手柄 | `device.CurrentState` 读锁保护的快照 |
| HID 读线程 | 输入报告解析 | 写快照加锁;`StateChanged`/`Disconnected` 事件(注意在线程上下文里,UI 需 `Dispatcher`) |
| WASAPI 捕获线程 | 音频转触觉 DSP | volatile 标量 + 滤波器原地重配 |
| WASAPI 渲染线程 | 触觉 PCM 输出 | `HapticChannelSettings` 不可变对象 volatile 引用交换;环形缓冲加锁 |
| 输出报告 | 任意线程调用 `UpdateOutput` | `_ioLock` 串行化 |

## 开发流程

```
dotnet build                # 构建
dotnet test                 # 44 个单元测试
dotnet run --project src/DsxLite.Cli -- --audio          # 音频端点诊断
dotnet run --project src/DsxLite.Cli -- --triggers       # 扳机效果硬件测试
dotnet run --project src/DsxLite.Cli -- --haptics        # HD 触觉脉冲测试
dotnet run --project src/DsxLite.Cli -- --haptics-probe  # 触觉通路组合探测
dotnet run --project src/DsxLite.Cli -- --a2h            # 音频转触觉(VU 表)
```

发布(参考 v1.0.0):

```
dotnet publish src/DsxLite.App -c Release -r win-x64 --self-contained -p:PublishSingleFile=true
git tag -a vX.Y.Z -m "..."; git push origin main vX.Y.Z
# GitHub Release 通过 REST API 创建并上传 zip
```

## 扩展指南

- **新增扳机效果**:在 `TriggerEffectMode` 加枚举值 → `GetParamSpec` 加参数元数据 → `TriggerEffectPresets.All` 加预设(GUI/CLI 自动出现)→ 补 `OutputReportTests` 用例
- **协议变更排查**:先用 CLI `--audio` / `--haptics-probe` 确认真机行为,再改 Core
- **改动协议布局后**:`OutputReportTests` 里的偏移断言是最快的回归保障

## 已知限制

- 蓝牙下无 HD 触觉(协议限制;DSX v3.2 用私有方案绕过,未公开)
- 陀螺仪仅零偏校准;虚拟手柄为固定映射(无按键重映射/宏/配置文件)
- 手柄独占:同一时间只能一个程序打开
