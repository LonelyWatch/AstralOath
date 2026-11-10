# UIBaseView 与预制体 Binder

业务界面类继承普通 C# `UIBaseView`，不挂在预制体上。预制体根节点保留通用 `BindingMap`，按 key 保存 GameObject 或 Component 引用；Lua Binder 继续管理可热更新的界面业务。

## 项目通用命名约束

以预制体的文件名（不含 `.prefab`）为唯一命名依据，文件名与 C# 类型名大小写一致。对 `InventoryWindow.prefab`：

- View 文件为 `Scripts/InventoryWindow.cs`，类型为 `InventoryWindow : UIBaseView`，不另加 View 后缀。
- 业务 Binder 名为 `InventoryWindowBinder`，当前 Lua 文件为 `Lua/InventoryWindowBinder.lua.txt`，导出契约为 `Lua/InventoryWindowBinder.exports.txt`。`.lua.txt` 是 Unity TextAsset 导入后缀，不属于 Binder 名。
- 业务 View 重写 `PrefabPath` 并由 `UIViewRegistry` 注册。资源地址可继续采用 `inventory/main-window` 等稳定地址，不要求等于物理文件路径。

此约束适用于项目所有业务 UI 的预制体、View 和 Binder。通用 `UIBaseView`、`UIViewRegistry`、`BindingMap` 及加载/桥接工具保留各自职责名称；静态美术预制体不因此新增业务脚本。重命名时同步调用点、生成器、资源收集路径、Lua 契约和测试，并移动已有 `.meta` 保留 GUID。业务脚本仍不挂到预制体上。

## 制作界面

1. 在 Unity 中双击预制体，进入 Prefab Mode。在根节点添加 `BindingMap`，或用子对象 Hierarchy 右侧的 `+` 自动创建根 Binder。
2. 对需要使用的节点点击 `+`，绑定到最近的 Binder。已有 Binder 的普通场景实例也支持此操作。嵌套 Binder 各自管理自己的子树。
3. 选择 Binder 根节点。Inspector 的绑定项每行展示 key、对象引用、类型选择和删除按钮，没有逐项折叠。类型可选 GameObject 或对象上的具体组件。
4. 修改 key 为稳定业务名称，例如 `title`、`confirm`。同名节点自动生成不同 key；手工重复 key、缺失对象和外部引用仍需修正。
5. Hierarchy 的 `−` 移除对应对象的绑定。Inspector 删除单个绑定项。操作支持 Unity Undo/Redo；保存预制体以持久化修改。

Hierarchy 的增删按钮仅在编辑状态显示，运行模式下不修改绑定。普通场景中不属于任何 Binder 的对象不显示按钮；制作新界面时先进入 Prefab Mode。

## 编写与注册 View

```csharp
using AstralOath.Client.Common;

public sealed class InventoryWindow : UIBaseView
{
    // YooAsset 的稳定资源地址；对应预制体必须加入资源收集配置。
    public override string PrefabPath => "inventory/main-window";

    public GameText Title => Bindings.Require<GameText>("title");
    public GameButton Confirm => Bindings.Require<GameButton>("confirm");

    public override void Validate()
    {
        base.Validate();
        _ = Title;
        _ = Confirm;
    }
}
```

在模块启动处注册 `registry.Register<InventoryWindow>()`，然后使用 `await registry.CreateAsync<InventoryWindow>(assets, parent, owner)`。注册表从 View 记录 `PrefabPath`，统一经 `IAssetService` 加载预制体、实例化并检查 Binder。`parent` 是 UI 容器；`owner` 是拥有该实例及资源的 `BindingScope`。窗口暂时关闭用 `Hide()`，重新打开可复用实例；销毁拥有者时释放实例和资源。

`UIBaseView` 提供显示和组件访问；订阅、按钮行为、网络请求等继续由窗口控制器/Lua 层组织，并用当前窗口的 BindingScope 取消与解绑。不要把业务类重新挂到预制体上，也不要把层级名称当作运行时查找路径。

## 演示与内容兼容性

现有示例统一为 `FoundationWindow.prefab`、`FoundationWindow.cs` 和 `FoundationWindowBinder.lua.txt`，导出契约为 `FoundationWindowBinder.exports.txt`。View 的地址仍是 `foundation/foundation-window`，Binder 地址仍是 `foundation/demo-binder`。演示场景 `Assets/Game/Modules/FoundationDemo/Scenes/FoundationDemo.unity` 在启动时动态加载窗口；场景只记录 UI 父节点。预制体不挂 `FoundationWindow`。

新窗口预制体进入 Foundation 的资源收集和签名候选验证。本次内容 schema 升为 **2**，协议仍是 1；旧 schema 1 的演示发布物没有该预制体，不能混用。重新构建资源后使用 `Tools/Content/Package-Unity.ps1`（默认 schema 2）生成大于历史发布版本的新整数版本与新目录；schema 升级不重置发布版本号，不能覆盖已有清单。调用通用 `Release.ps1 -Action Verify` 验证时加 `-SchemaVersion 2`。这次没有更换正式更新地址或部署远端资源。

## 预制体解耦改造验证（2026-11-11）

- Unity 2022.3.62f3：EditMode 35/35、PlayMode 6/6 通过，覆盖绑定、Undo/Redo、预制体持久化、取消与释放、激活前校验及布局保留。
- Windows IL2CPP 构建通过，实际 Player 加载界面并收到本地服务器回显（序号 105）。本次未重新验证 Android 构建。
- Inspector 已实际目检为单行展示。Hierarchy 增删有编辑器行为测试；用户正在操作预览窗口，未继续进行人工点击验收。
- ReleaseSmoke、真实 Windows YooAsset 资源包的 schema 2 签名与验签通过（8 项载荷、5 个 bundle）；Content 行为测试 10/10 通过。本地来源路径支持 TMP shader 的空格及中文，发布载荷路径的限制保持不变。
- 独立代码审查通过。本次未部署云主机或发布正式更新资源。

## 命名统一复验（2026-11-11）

基类统一为 `UIBaseView`，示例 View/Binder 按预制体名同步重命名，保留四组资产 GUID、资源地址与内容 schema 2。新增命名契约检查后 EditMode 36/36、PlayMode 6/6 通过，后者启用本地服务器回显；模块验证器 4/4 通过，依赖检查与独立代码审查通过。此次仅命名调整，未重新构建 Windows/Android Player。
