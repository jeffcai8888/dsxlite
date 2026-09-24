# AGENTS.md

DSX 的开源替代实现:PS5 DualSense 手柄 PC 工具。C# / .NET 9 / WPF。

- 协议与硬件层全部在 `src/DsxLite.Core`;App/CLI 不直接操作字节布局
- 完整开发文档(协议细节、线程模型、扩展指南):[docs/DEVELOPMENT.md](docs/DEVELOPMENT.md)
- 构建 `dotnet build`,测试 `dotnet test`;改协议后必须保持 `OutputReportTests` 的偏移断言通过
- 真机调试优先用 CLI:`--audio` / `--triggers` / `--haptics` / `--haptics-probe` / `--a2h`
- 关键真机结论:HD 触觉走音频端点声道 3/4,且 HID 输出报告 flag0 bit0/bit1 必须清零(否则执行器被锁在兼容震动通路);两者互斥
