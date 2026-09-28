# 首阶段：六轴工厂校准

## PRD
USB/蓝牙均读取校准，GUI/CLI/虚拟手柄共享同帧物理量。失败保留输入并公开标称降级。没有传感器报告时不显示有效六轴数据。本阶段不实现映射、宏、共存和蓝牙媒体。

## architecture
协议与换算位于 Core；DualSenseCalibration 为不可变解析/系数模型，DualSenseInputSnapshot 一次发布 raw、Motion 与校准状态；App/CLI 不处理协议偏移。

## system_design
启动读线程前尝试特性报告0x05。状态锁保护快照发布。USB不验feature CRC，BT使用0xA3。失败整组六轴降级。读取接缝使用委托，不重构所有HID生命周期。

## tech_doc
gyro dps=raw*(speedPlus+speedMinus)/(abs(plus-bias)+abs(minus-bias))；不额外扣gyro bias。accel g=(raw-midpoint)*2/(plus-minus)，midpoint沿用整数除法。标称回退raw/64 dps、raw/8192 g。500dps对应虚拟摇杆满偏。保留raw字段语义。

## task_list
1. 基线49测试通过（存在NAudio过时API警告）。
2. RED→GREEN：纯校准、读取、快照、gyro映射。
3. 集成GUI/CLI、八语言与设备选择诊断。
4. 构建、覆盖率、审查、可用硬件验收、文档。

## 验收边界
合成报告验证不代表硬件精度。无硬件/ViGEm时记录未验证；不安装驱动、不修改隐藏规则、不提交推送。
