# GFramework

Unity 2022.3 常用 C# 工具包。

## 帧更新框架设计

面向输入、逻辑、物理、表现与网络循环，提供动态注册、可配置降频、分组暂停、生命周期和性能观测。纯 C# 核心初版已实现并通过独立测试；Unity 适配仍需在 Unity 2022.3 工程中验证。

- [离线阅读版](Documentation~/frame-update-framework.html)：带目录与文档切换，可直接在浏览器打开。
- [完整设计与 API](Documentation~/frame-update-framework.md)：架构、时序、契约、默认配置和扩展接入。
- [实施与验收清单](Documentation~/frame-update-implementation-plan.md)：阶段任务、验收矩阵、性能基准和完成定义。
- [核心测试说明](Tests~/FrameUpdate/README.md)：独立 .NET 测试、筛选和基准运行方式。
