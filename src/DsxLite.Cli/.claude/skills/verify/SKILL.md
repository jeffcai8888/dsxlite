---
name: verify
description: 验证 DsxLite CLI 校准诊断的实际运行与参数边界。
---

从仓库根目录运行：

1. `dotnet build src/DsxLite.Cli`，然后通过 `dotnet src/DsxLite.Cli/bin/Debug/net9.0-windows/DsxLite.Cli.dll` 驱动真实入口，不直接调用内部方法。
2. 运行 `--calibration --device 0 --samples 2`。有设备时应显示 Factory 或明确降级状态、完整报告、raw、°/s 和 g；没有设备应返回1并提示。
3. 分别传 `--device -1`、`--samples 0`、重复 `--samples`、缺失值、`--triggers` 冲突，均应返回2并提示用法，不进入效果模式。
4. 保存stdout、stderr和退出码到根目录 `.tmpfiles/`。Python捕获工具输出时设置 `PYTHONIOENCODING=utf-8`，避免终端二次转码误判乱码。
5. CLI实际枚举比 PnP 名称过滤更可靠；不要仅因系统查询无结果就断言没有手柄。

只读校准诊断不能发送效果报告；不以六轴合成测试或观察单一姿态代替六面/已知角速度精度验证。不自动驱动马达、安装ViGEm或改变其他应用。蓝牙需要真实蓝牙连接单独验收。
