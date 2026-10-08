# Input module sample

1. 在空场景创建空 GameObject，添加 `InputSampleBootstrap`。
2. Play 后按 Space 查看 `Gameplay.Jump` 的逻辑状态，按 Tab 获取/释放高优先级 UI context。
3. 该示例使用 Legacy Input，不需要 Input System；输入采样通过共享 FrameUpdate Host 的 Input loop 完成。

