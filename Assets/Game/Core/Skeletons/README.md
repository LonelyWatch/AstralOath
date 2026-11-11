# 稳定骨骼播放桥接

本目录独立 AOT 程序集 `AstralOath.Client.Skeletons`，静态引用隔离后的官方 Spine 2.1.25 / 3.8 core；无业务程序集引用，不装旧版 spine-unity。新增或修改本桥接能力需重新构建母包。

`SkeletonPlayerFactory.Create(source, parent)` 只根据 JSON 的 `skeleton.spine` 派发。source 包含标准 JSON、atlas 文本和以 atlas 页名为键的纹理映射。每实例独立解析数据、骨架和 AnimationState；不跨角色共享可变 runtime 状态。坐标单位由 source.Scale 控制，默认每像素 0.01 Unity 单位。

资源调用方必须保持纹理资产句柄的 BindingScope 存活。播放器拥有自己的 GameObject、Mesh、Material；Dispose 不释放借入的纹理。创建失败回滚自有对象；Dispose 可重复调用。所有 API 在 Unity 主线程调用。没有自动 Update、异步任务或全局注册，Binder 在自己的生命周期内调用 Tick，并在关闭时 Dispose。

TryGetBonePose 返回官方内核计算后的只读局部位置（已按 source.Scale 缩放）、局部角度和缩放，用于镜头表现；全局 Flip 不改变局部 pose，查无骨骼返回 false，释放后查询抛 ObjectDisposedException。Play 接受动画名、循环及混合秒数；Pause 只阻止 Tick；Stop 清除轨道并回到 setup pose。Seek 取消当前混合、从 setup pose 计算指定时刻且不触发动画事件；循环动画时间按 duration 取模。事件只用于表现，禁止结算战斗或发奖；回调切换播放、停止或释放后，不继续派发先前播放批次的剩余事件。

2.1 使用原生 RegionAttachment/MeshAttachment/SkinnedMeshAttachment 的世界顶点与 Flip 继承。3.8 使用原生 RegionAttachment/MeshAttachment 和 SkeletonClipping；骨架 DrawOrder 原样进入连续材质段，A/B/A 不合并成 A/A/B。Atlas 的裁切和 0/90 度旋转 UV 来自原生 attachment，再将 V 坐标转成 Unity 坐标。固定 3.8 core 的 RegionAttachment 不支持其它旋转角度，桥接对此显式抛出 NotSupportedException，避免静默错图。

`Resources/AstralOathSkeleton.shader` 是母包内置 URP unlit shader，支持 straight/PMA atlas、normal/additive/multiply/screen、slot/attachment tint 和 3.8 two-color tint。PMA 由调用方显式指定，不从纹理内容猜测。Atlas 纹理导入器负责过滤、wrap、色彩空间；桥接不修改借入纹理。Linear 项目中对 sRGB PMA 输入先取消 sRGB 预乘，再在线性空间重新预乘。PMA 输入应保留源 RGB，导入器关闭 alphaIsTransparency。2.1 官方格式只有 normal/additive slot，保留原格式语义。

本桥接不是 Canvas Graphic，Root 使用 MeshRenderer，需要场景 Camera，或由上层将相机 RenderTexture 显示到 UI。EditMode 测试验证动画/几何/生命周期/材质配置；shader 最终像素、真实资源画面及 Windows IL2CPP 播放另做验收，不以编译或合成测试代替。
