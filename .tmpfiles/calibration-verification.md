# 六轴校准首阶段验证（2026-09-28）

## 范围
本次未提交变更，0 个新增提交。第一阶段为工厂六轴校准、快照、GUI/CLI/ViGEm消费、只读诊断；未实现后续映射/宏/蓝牙媒体。

## 软件回归
- dotnet build --no-restore：成功，0错误；既有NAudio过时API和App nullable共5警告。
- dotnet test --no-restore --collect:"XPlat Code Coverage" --results-directory .tmpfiles/test-results：143/143通过，原基线49，新增94。
- Calibration/Axis/Motion/Snapshot/GyroStickMapper：行和分支100%；DualSenseDevice行81.19%、分支76.66%。Core整体行57.97%、分支47.09%，并未达到全仓80%目标。
- git diff --check：通过。
- code-review技能调用被权限规则阻止，未重试；主线程已人工检查Core和消费端差异。Core子任务报告独立检查未发现新增阻断项；不声称已运行完整审查技能。

## 运行时验证：USB读取与WPF显示/断开
**Verdict: PASS（限定标准DualSense USB数据通路；完整硬件验收仍有阻塞项）**

方法：真实CLI入口及独立发布WPF，Windows UI Automation点击用户可见连接/断开按钮，保存应用文本和截图。不调用内部业务函数替代界面。

1. `--calibration --samples 2`，返回0，真实设备报告 Factory、完整报告=True。
   - raw gyro=(-18,-5,-19)，accel=(-70,7934,1377)
   - °/s=(-1.095,-0.305,-1.158)，g=(-0.0084,0.9832,0.1681)
2. 🔍 非法负索引、零samples、重复samples、缺值、非数字、与--triggers混用，全部返回2和用法，不进入效果模式。
3. WPF点击连接后：
   ```text
   六轴：工厂校准
   陀螺 °/s  俯仰 -1.2 偏航 -0.3 翻滚 -1.2
   加速度 g X -0.01 Y 0.98 Z 0.17
   ```
4. 🔍 点击断开后显示“运动数据不可用”“已断开”，不保留旧六轴数值。

证据：`verify-cli-results.json`、`verify-gui-results.txt`、`verify-app-connected.png`。截图已捕获，但本轮模型不能查看图片，视觉排版未主张已验证；可访问控件文本已读取验证。

## 观察与限制
- PnP过滤未返回手柄，但实际HID枚举发现USB设备；以应用结果为准。
- 捕获端最初因Python输出编码出现乱码，设置PYTHONIOENCODING=utf-8后重跑正常，非应用乱码。
- 静置gyro约1°/s残差可见；本阶段不做动态零偏校准，不以静置自动扣除来混改Linux算法。
- 原有用户进程27528（publish/app）保留未关闭；所有本次启动的验证GUI进程已结束。
- 蓝牙、Edge、多手柄、六面朝向、已知角速度、实际ViGEm输出以及八语言逐一UI切换均未验收。八语言键完整性有自动测试。
- 当前只修复校准限制，不宣称全部README限制已突破。未安装驱动、未改设备隐藏/自启动/语言偏好、未提交推送。
