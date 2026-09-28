# 逐步突破 README 已知限制

## Context
用户希望依据 `README.md:82-88` 逐步解除功能限制，而不是仅删除文档条目。目标是以小步可回归的方式完成软件层能力，再处理需要硬件与专有传输验证的能力。首阶段建议是陀螺仪/加速度计完整工厂校准，后续依次处理重映射、宏、设备共存与蓝牙音频/触觉。

## 调研依据与边界
- Mana 知识库未命中项目相关资料，以仓库源码和公开一手资料为依据。
- 本机具备 .NET SDK 9.0.304；`gh` 不可用，GitHub CLI 搜索未执行成功，已通过网页读取上游源码；当前无 Exa 工具。
- [Linux hid-playstation](https://github.com/torvalds/linux/blob/master/drivers/hid/hid-playstation.c)：DualSense `0x05` 校准报告共 41 字节，含陀螺仪零偏、正负量程、转速和加速度计正负量程。蓝牙 feature CRC 前缀为 `0xA3`。
- [SDL PS5 HID 驱动](https://github.com/libsdl-org/SDL/blob/main/src/joystick/hidapi/SDL_hidapi_ps5.c)：提供另一份成熟校准实现，但其 gyro 实时减零偏，与当前 Linux 实现不同。实施时必须明确选定一种算法并用对应测试固定，不能拼接两套算法。
- [HidSharp NuGet](https://www.nuget.org/packages/HidSharp)：现有依赖路线可继续复用，不因本任务无故升级或引入新驱动。
- [DSX 官方商店](https://store.steampowered.com/app/1812620/DSX/) 已宣称蓝牙音频/触觉 Beta；这说明不宜把限制写成硬件绝对不可能，但不构成本项目拥有公开可用实现的证据。

## 实施范围
本次批准后先实施第 1 阶段，完成代码、测试、诊断与文档更新。后续阶段按验收结果继续推进；新驱动安装、系统设备隐藏规则、外部写入和提交推送均不默认执行。

## 已核实的本地现状
- `src/DsxLite.Core/DualSense/DualSenseDevice.cs:59-95`：仅蓝牙读取校准，且只取三个 gyro bias；失败被静默忽略。USB 不读取。
- `src/DsxLite.Core/DualSense/DualSenseInputState.cs` 的六轴字段明确是 raw；`DualSenseReportParser.cs` 只做协议解码，应保持语义。
- `src/DsxLite.App/MainWindow.xaml.cs:437-446` 仅界面减 gyro bias；`VirtualXbox360.cs:44-50` 直接以 raw/1024 换算。1024 是上游规范化单位，不是原始传感器的常规每度计数，不能继续混用。
- `VirtualXbox360.Update` 是 App/CLI 共用映射入口；`Crc32.Compute(seed, data)`、`IsFullReport`、设备现有状态锁可复用。
- HidSharp 默认打开路径没有显式独占选项；公开 Windows 实现请求读写共享。实际与其他程序共存仍需真机测试，不能宣称已解除占用冲突。

## 分阶段路线
| 阶段 | 可交付内容 | 完成边界 |
|---|---|---|
| 1：六轴工厂校准 | USB/BT 校准读取、统一 °/s 与 g、失败状态、诊断、测试 | 软件验证与真机精度验证分开记录；保留原始数据 |
| 2：按键重映射 | Core 纯映射器、默认映射/交换/禁用/Edge 额外按键；每设备配置与 WPF 编辑器 | 配置采用稳定 ID、版本和原子保存；默认行为回归，补普通摇杆端点测试 |
| 3：有限宏 | 输入处理脱离 UI 33ms 定时器；每设备会话；按下/释放/延时的可取消宏 | 只输出虚拟手柄，不做任意脚本；假时钟测试；断开、停用、换配置、退出均释放按键；限制长度和队列 |
| 4：设备共存 | 明确共享读取、输出冲突和双输入诊断；按证据决定只读模式或输出仲裁 | 不强抢其他程序独占；HidHide 仅解决物理设备可见性，安装和规则变更另行确认 |
| 5：蓝牙 HD/音频 | 找到公开可用的协议/编码/分包方案，核对许可，做低强度 CLI 原型，再考虑 GUI | 分别验证 HD、扬声器、耳机；不能将普通震动冒充 HD；不能把 USB PCM 输出直接套到 BT；没有可验证方案则保留明确限制 |

## 第 1 阶段实施步骤
### 1. 先建立测试基线和纯校准模型
- 先执行现有测试记录实际数量和失败项，不依赖 README 的历史数字。
- 新建 `src/DsxLite.Core/DualSense/DualSenseCalibration.cs`，使用不可变模型与纯解析/换算函数；新建有单位的运动状态类型，不改变 raw short 字段含义。
- 以 Linux DualSense 算法为依据独立实现：gyro 分母为 `abs(plus-bias)+abs(minus-bias)`，分子为 `speedPlus+speedMinus`，直接得到 °/s，运行时不再减 bias；accel 中点按上游整数除法计算，转换为 g。
- 严格验证报告 ID/长度、BT feature CRC（`0xA3`）、正的转速合计、正的轴跨度及有限结果；所有 short 运算先提升到 int。任何读取、结构、CRC 或轴参数失败时，首阶段整组六轴降级并记录原因，不引入部分轴混合模式、不把降级当成功。
- 工厂参数有效时使用上述 Linux 算法；全部降级时使用 SDL 的明确名义比例：gyro `raw/64.0` °/s、accel `raw/8192.0` g，不使用失败数据中的 bias 或系数。该降级规则是独立的兜底，不混入有效工厂校准公式。常量名称明确区分 raw 比例与规范化比例。
- 从 `DualSenseCalibrationTests.cs` 的 RED 开始：正负端点、三轴顺序、非对称 gyro 端点、非零 bias、奇数 accel 跨度、负原始值、极值、截断/错 ID/坏 CRC、零/非法分母和整组降级。CRC 使用固定参考向量，不只用被测函数生成预期值。

### 2. 接入 USB/BT 读取和一致快照
- 修改 `DualSenseDevice.Open/ReadCalibration/ReadLoop`，USB 与 BT 都尝试读取 `0x05`，失败不阻止按键输入。
- 区分校准读取结果、校准可用程度与实际输入是否完整；BT 读取成功不等于已收到完整报告。
- 使用现有状态锁一次发布 raw 与对应运动数据的不可变快照；保留 `CurrentState` 兼容读取，增加统一快照入口，避免分次读取错配。
- 对 BT 简化报告明确标为“运动数据不可用”，不显示零值为成功校准，也不保留上一帧为当前数据。
- 将读取异常转成可观察的诊断状态和原因。测试用小型 feature-reader 委托/接口隔离硬件，不引入大规模 HID 抽象或生命周期重构。注意 HidSharp `GetFeature` 不直接返回实际长度，不能以缓冲区分配长度冒充设备返回长度验证。

### 3. 统一 GUI、CLI 与虚拟手柄消费路径
- `MainWindow.xaml.cs`：消费统一快照，移除 GUI 单独减 bias/固定比例；展示工厂校准、标称降级或不可用状态及 °/s、g 单位。
- 复用 `Localization.Get/Format` 和 `Resources/Strings.*.xaml` 的八语言机制，所有新增状态键齐全，不新增孤立硬编码提示。
- `VirtualXbox360.cs`：接收同帧运动数据；提取纯 gyro-to-stick 换算以便无 ViGEm 驱动测试；保留 500°/s 满偏契约和钳制。没有运动数据时回退物理右摇杆。
- 修正 raw/1024 后 gyro 手感会明显变化，这是单位修正的可观察行为，文档说明，不再额外除以 16 维持错误换算。普通摇杆端点问题留到映射阶段。
- `Program.cs`：正常输出使用同一物理量路径，增加 `--calibration` 诊断，显示连接/读取/校准/完整输入状态、raw 与物理量；支持明确选择设备，避免多手柄测试误选。该模式不主动驱动马达、音频或扳机。

### 4. 文档与复核
- 修改 `README.md` 与 `docs/DEVELOPMENT.md`：说明校准算法、降级策略、单位修正和诊断命令。仅按证据更新校准限制；不要删除未完成的蓝牙/映射/共存条目。
- README 的测试数量改为不易失真的描述或本次实测数量，区分合成数据测试和已验证硬件型号/连接方式。
- 实施前在 `.tmpfiles/` 整理简短 PRD、architecture、system_design、tech_doc、task_list；实现代码不放临时目录。完成后做代码审查，修复阻断问题，不自动提交或推送。

## 验证与验收
1. `dotnet build`、`dotnet test`，原有 `OutputReportTests` 偏移/CRC 断言必须继续通过。
2. `dotnet test --collect:"XPlat Code Coverage" --results-directory .tmpfiles/test-results`；新增纯校准/运动映射逻辑覆盖率至少 80%，报告整体覆盖率，不以新增模块达标冒充全仓达标。
3. Fake feature-reader 集成测试覆盖 USB/BT 请求 `0x05`、读取异常、无效数据、整组标称降级；合成 USB/BT 输入验证 raw、物理量与报告类型一致。纯 gyro-to-stick 测试覆盖方向、500°/s、超范围钳制及无运动数据回退。
4. 通过 CLI 真机验证：USB/BT 分别读取校准；静置观察 gyro；六面朝向验证加速度约 ±1g 和轴方向；同一姿态两种连接结果应一致；多手柄校准不得串用。精确角速度比例需要参考测量，手动转动仅验证大致方向/响应。
5. 启动 WPF 检查数值/单位/状态、八语言切换、设备切换、断开后旧数据不冒充当前值；有 ViGEm 时观察实际虚拟右摇杆响应。不安装驱动、不改设备隐藏规则。
6. 若无手柄或 ViGEm，完整完成可运行的软件测试，但明确真机验收未完成，不声称所有限制已突破；已有 HD/普通震动通路不应受到本阶段改动影响。

