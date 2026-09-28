---
name: verify
description: 通过 Windows UI Automation 验证 DsxLite WPF 的连接、校准状态和断开。
---

从仓库根目录构建并独立发布：`dotnet build`；`dotnet publish src/DsxLite.App -c Debug --no-build -o .tmpfiles/verify-app`。

- 只启动独立目录内的 DsxLite.App.exe，记录PID；不要结束已有用户进程。
- PowerShell加载 UIAutomationClient/UIAutomationTypes，用 ProcessId 条件定位窗口，读取控件树。通过连接按钮的 InvokePattern 点击，不能用内部反射调用窗口方法冒充UI验证。
- 初始显示“运动数据不可用”；连接真实USB手柄后应显示“工厂校准”或明确降级，运动单位 °/s、g。点击“断开”后应清除旧数据。
- 用 PIL ImageGrab 或 Windows 截屏保存窗口图片到 `.tmpfiles/`，同时保存 UI Automation 可访问文本和操作日志。截图若无法由当前模型读取，不声称视觉布局已验证。
- 验证完先点断开，再清理本次启动的测试进程。关闭按钮只是隐藏托盘，不会退出程序。
- 不默认点击效果/音频/虚拟手柄开关、不修改用户语言偏好、注册表或设备隐藏规则。六面姿态、蓝牙、双手柄和ViGEm必须分开记录实测范围。
