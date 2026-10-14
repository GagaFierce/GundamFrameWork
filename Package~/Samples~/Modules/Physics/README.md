# Physics module sample

1. 创建一个带 `Rigidbody` 的对象并添加 `RigidbodyFixedStepBehaviour`，设置 Velocity。
2. 创建空 GameObject，添加 `PhysicsSampleBootstrap`，把刚体组件拖到 `Mover`，把一个 Transform 拖到 `Ray Origin`。
3. Play 后运动由共享 Physics loop 的 EveryStep/Required 参与者执行；射线使用固定的 `RaycastHit[]` 缓冲。

