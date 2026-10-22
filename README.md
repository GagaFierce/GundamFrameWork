# GFramework

Unity 2022.3 常用 C# 工具包。

## 安装与依赖

包名为 `com.gagafierce.gundamframework`。基础运行时依赖 TextMeshPro 3.0.7 和 uGUI 1.0.0；Addressables 不是必需依赖。安装 Addressables 后，`WFrameWork.ResLoad.Addressables` 与 `WFrameWork.Scene.Addressables` 可选程序集会启用。

项目开发时可通过 Package Manager 添加本地包。游戏仓库不应再复制 `Core` 到 `Assets/Plugins`，避免同一程序集被加载两次。

## 帧更新框架设计

面向输入、逻辑、物理、表现与网络循环，提供动态注册、可配置降频、分组暂停、生命周期和性能观测。Unity 宿主工程位于 `UnityProject~`，验证入口位于 `Validation~`。

## Input、Physics、UI 模块

模块按纯逻辑层、Unity 适配层和可选桥接程序集拆分，统一复用显式 `UnityFrameUpdateHost`。完整的游戏 UI 流程已迁入 `UnityProject~`，输入系统适配默认使用 Legacy API，可选 Input System 单独导入。
