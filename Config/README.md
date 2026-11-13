# 配置源表与生成

配置入口为 `Config/luban.conf`；`Config/Schema/` 定义字段和引用，`Config/Tables/*.csv` 是当前十二张表的唯一数据编辑源。新增表同时维护 schema、源表、生成校验及消费模块说明。不要手改生成 C#、二进制、客户端 JSON 快照或角色导入目录；未来改用 Excel 时，每张表仍只能保留一个权威编辑源。

## 当前源表

Foundation 的三张表由 `Schema/foundation.xml` 定义：

- `Tables/demo_ui.csv`：演示界面记录，记录 1 必须存在；停留秒数为 1–30，标题、消息与资源通过表内引用关联。
- `Tables/demo_localization.csv`：演示标题、按钮和业务状态的简体中文文本。
- `Tables/demo_assets.csv`：稳定资源地址与项目源文件路径；新增地址须与 YooAsset 收集配置一致。

CharacterLab 的两张表由 `Schema/characterlab.xml` 定义：

- `Tables/characterlab_characters.csv`：角色 ID、名称 key、显示顺序、`battleSkin` 与 `portraitSkin`。皮肤名显式配置；是否存在于对应骨骼由 Unity 资源验证和构建检查确认。
- `Tables/characterlab_messages.csv`：实验室中文界面、角色名、技能名和语音标签。文字语言与语音选择独立。

两组文案 key 都使用点分的小写名称，各段允许数字和下划线，例如 `foundation.title`、`lab.static_pose`、`character.ras.name`；生成器要求至少两个点分段。保留带参文案的格式占位符，业务代码引用稳定 key。

`Config/CharacterAssets/catalog.generated.json` 是角色导入器生成的资源目录与声音事件，不是手工配表源。导表把角色行和文案合入该目录，生成运行时 catalog；角色数量、ID、名称引用或必要皮肤字段不一致会失败。增加角色先按[角色资源通用规则](../docs/development/character-asset-import.md)扩展白名单、导入已批准依赖，再补齐角色源表。导表本身不读取 `D:/sevenepic`，也不触发外服追踪。

Battle 的七张表由 `Schema/battle.xml` 定义。英雄通过 `battle_heroes.csv` 的 `growthPresetId` 引用 `battle_growth_presets.csv`，并按英雄 ID 在 `battle_awakening_steps.csv` 配置觉醒。现有面板列是导表的验收锚点。其余四张为 `battle_skills.csv`、`battle_skill_effects.csv`、`battle_skill_enhancements.csv`、`battle_messages.csv`。有序效果表定义技能的阶段、操作、目标和参数。导表检查模板、觉醒序列、槽位、角色、演出、图标、文案、魂燃和强化序列。见[等级与站位](../docs/development/battle-growth.md)和[技能配表](../docs/development/battle-character-data.md)。

主动程序按 `impact`、`after_hit`、`after_attack` 顺序执行。被动槽使用 `target=passive`，由规则内核按开战、自身回合结束、受击后、友军攻击后或伤害计算阶段触发。开战和回合结束只接受自身必定获得的增益；受击阶段可配置自身净化、提高行动值或反击；伤害修正通过 `impact` 的 `damage_bonus`、`defense_penetration` 或 `damage_guard` 的 `damage_reduction` 配置。触发攻击使用其被动槽的倍率、演出和冷却。条件、目标、操作和强化类型随导表和共享执行器同步扩充，文案由稳定键关联。

`turns` 在 `apply_status` 行表示持续回合数。对带 `caster_has_resource` 条件的伤害修正行，它表示最低个人资源；受击触发的同名条件使用 `turns=0`，表示个人资源至少为 1。反击概率强化用 `counter_chance_bp`，屏障倍率强化用 `barrier_bp`，两者为直接相加的万分比。额外回合及个人资源回满的魂燃分别使用 `extra_turn`、`refill_resource`。

英雄的个人资源由 `resourceKind`（`none`、`focus`、`fighting_spirit`）、`resourceMaximum`、`resourceTurnGain`、`resourceOpeningGain`、`resourceAttackedGain` 和 `resourceAttackedSpend` 配置。开战增量在首次推进回合前写入，受击增减量每次权威攻击执行一次。无个人资源的英雄使用 `none` 并将五个数值字段设为 0。主动技能使用 `resourceCost`、`resourceGain`、`resourceGainWhenBuffed`；最后一项为有增益且非夹攻时的获取值，0 表示沿用基础获取值。导表检查每个资源值都落在英雄自身上限内，并校验被动槽不填写主动获取或消耗。

## 生成和检查

在仓库根目录使用 PowerShell 7：

```powershell
pwsh -File Tools/Config/Generate.ps1 -VerifyAccessors -SyncClient
pwsh -File Tools/Config/Generate.ps1 -VerifyAccessors -CheckClient
```

`-SyncClient` 更新快照；`-CheckClient` 用于 CI 或提交前检查快照与当前源表一致，两参数不能同时使用。未配置全局 PATH 时，把 `pwsh` 替换为 `& ./.tools/powershell/pwsh.exe`。

脚本使用固定 SHA-256 的 Luban v5.1.0，以 .NET 10 运行并允许其 .NET 8 入口跨主版本启动。首次运行需要下载锁定工具。Luban 的 `.7z` 包由项目内固定版本的 7-Zip reduced console 解压，官方地址与 SHA-256 记录于 `Tools/dependencies.lock.json`；按需下载至 `.tools/7zip-<版本>/7zr.exe` 并在每次使用前校验，无需全局安装。

Luban 严格校验范围、引用和主键；脚本继续检查文案 key、非空中文文本、项目资源路径及角色导入目录一致性。输出为 `.artifacts/config/{json,bin,csharp}`。C# 入口 `AstralOath.Config.Tables` 接收按表名打开 Luban `ByteBuf` 的委托。`-VerifyAccessors` 使用固定提交的官方运行时源码，逐表比较二进制读回与本次 JSON，并检查引用与共享成长公式的面板锚点。使用 `-SyncClient` 或 `-CheckClient` 时自动运行此验证。

## 客户端快照

`-SyncClient` 同步以下五份生成物。FoundationDemo 与 CharacterLab 使用各自 YooAsset 包；BattleScene 和 .NET 战斗入口共享生成快照，服务端构建时嵌入同一文件。

- `demo_ui.json` → `Assets/Game/Modules/FoundationDemo/Configs/demo-config.json`。
- `demo_localizationkeys.json` → `Assets/Game/Modules/FoundationDemo/Configs/demo-localization.json`。
- `characterlab-catalog-runtime.json` → `Assets/Game/Modules/CharacterLab/Configs/catalog.json`。
- `characterlab-localization-runtime.json` → `Assets/Game/Modules/CharacterLab/Configs/localization.json`。

- `battle-catalog.json` → `Assets/Game/Modules/BattleScene/Configs/battle-catalog.json`。

源表、schema、生成器及受影响的客户端快照应在同次修改中保持一致；Unity 已有 `.meta` 保留 GUID。新增配置消费方式时同步更新本说明与模块 README。配表回归入口 `pwsh -File Tests/Content/ConfigSmoke.ps1` 在独立副本检查引用、配表变更、非法时长及错误成长面板拒绝；角色皮肤、实际资源加载和显示另按[角色实验室手册](../docs/development/character-lab.md)验收。

## 代码清单

`Config/HotUpdate/assemblies.json` 与 `Config/HotUpdate/characterlab-assemblies.json` 是两套构建配置的代码清单，不参与 Luban 生成。前者用于 Foundation 签名更新，后者用于独立 CharacterLab 离线 Windows 测试包；不能互换入口、包名或母包归档。登记和发布边界见[热更与强更构建](../docs/development/hot-update.md)。
