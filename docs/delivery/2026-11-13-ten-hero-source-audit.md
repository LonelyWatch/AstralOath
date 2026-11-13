# 十角色扩充的演出与 HUD 来源核对

本轮按普通形态角色 ID 升序补齐 `c1002`、`c1003`、`c1004`、`c1006`、`c1007`、`c1008`、`c1009`，与现有 `c1001`、`c1005`、`c1062` 组成十角色。源图与模型标识保留原样，关联源文件、哈希和转换记录以 CharacterPresentation 导入清单为准。下列源核对基于只读 `D:/sevenepic/原始资源`，Unity 实测另见本轮交付记录。十角色最终导入清单含 923 个唯一地址和 30 张图，图引用均可解析。包含 `spani` 骨骼在内，共有 308 个需安装为演出资产的唯一源地址，另加 3 张速度线纹理。

## 普通形态的模型和技能图

- `c1002` 赛西莉亚，`model/cecilia.scsp`，`c_cecilia_skill_new_01` 到 `_03`。日语技能事件映射支持选用 `skill_new` 分支。
- `c1003` 萝季，`model/rose.scsp`，`c_rose_skill_01` 到 `_03`。
- `c1004` 席可，`model/silk.scsp`，`c_silk_skill_01` 到 `_03`。
- `c1006` 绮世，`model/kise.scsp`，`c_kise_skill_01` 到 `_03`。
- `c1007` 维德瑞，`model/wildred.scsp`，`c_wilderd_skill_01` 到 `_03`。模型和图的两种拼写均来自源文件。
- `c1008` 亚敏，`model/arming.scsp`，`c_aming_skill_01` 到 `_03`。
- `c1009` 夏绿蒂，`model/charlotte.scsp`，`c_charlotte_skill_01`、`c_charlotte_skill_02` 和 `c_charlotte_skill_03_05`。旧 `_03` 包含 `skill3_2_{rate}`、`sk_10110_3_*` 和零 HIT，固定 `_01` 到 `_05` 分支分别有 2、4、6、8、15 个 HIT。项目使用完整 `_05` 分支，实际技能动画 `skill3_2_5`，Cut-in 为 `sk_c1009_3_1`、`sk_c1009_3_2`。该选择不表示实现旧图全部 rate 变化。

只读解码确认七个战斗模型均为 Spine 2.1.27，均有 `b_idle` / `idle` 与 `top` 骨骼。上述 21 张图引用的主动画名均在对应模型中。由图内 EFFECT 和 HIT 递归追踪到 33 个 CFX，包含 48 个 Spine primitive 和 20 个 particle primitive，均属于既有资源类型。这一统计仅计图内根与其 CFX 后继，同模型 timeline 独立收集依赖。

## 新源节点和时间窗口

- 亚敏 S2 的 `ONEVENT skill2/eff_on` 只在同形态 `model/arming.timeline` 的 `timeline.default.skill2.frames` 中声明，时间 1130 ms。该事件启动 `aming_skill_02_new_pillar.cfx`，其七条起点连接分别延迟 891、1091、1291、1491、1691、1891、2013 ms。按源时间，七段 HIT 落在 2.021、2.221、2.421、2.621、2.821、3.021、3.143 s。必须将同模型 timeline 的命名事件参与 STG 事件窗口解析。
- 同模型 timeline 需按每条已排定 ANI 的片段名和起点取帧。绮世 S1/S2 的开场片段是传送，主体斩击、摇镜和回程效果分属后续片段。夏绿蒂 `_05` 的充能特效分属 `skill3_1` 与 `skill3_2_5`。赛西莉亚 S3 的第二段长枪特效在 `skill3_2`。源 timeline `locationBone` 指向绑定模型的 `root`、`weapon` 等骨骼，不能直接当作 STG 群体目标定位。
- 萝季 S1 前进和回位都有 `jump` 0.200 s，`landing` 缺失，`dust` 各在 .5333 / .5666 s，与对应片段结尾重合。夏绿蒂 S1 前进 `jump` .400 s，结尾 .7666 s，同模型 timeline 和骨骼均无 landing。以对应片段尾作为缺失落地事件的回退时限应保留诊断。独立 HIT 连接存在的其他空 `hit` 监听不应导致重复播命中。
- 席可 S1/S2 的 SHOT 节点 `source` 均为空、`time` 为 100 ms。S1 从 ANI 起点延迟 366 ms 启动 SHOT，再从 SHOT 起点延迟 100 ms 触发 HIT。源图仍含单独 EFFECT 和骨骼，SHOT 的空地址不能用猜测的弹道替代。
- 赛西莉亚 S2 `PPEFFECT #28` 通过 `when=2` 连接 `RANDOM_VALUE #27`，字段为 `name=u_range`、`duration=200`、`floating_type=true`、`upper=50`。范围字段可核对，原引擎采样频率与混合方式仍待证据。
- 绮世 S3 的 NODE_ANI 引用 `spani/kise_m_skill_03_node1.scsp`、`spani/kise_m_skill_03_node2.scsp` 和 `spani/kise_m_skill_03_target_node.scsp`。三个文件均有 `root → node` 的平移动画。节点指定 `screen_pivot=true`、`offset_y=216`，target 节点另带 `0,flying_damage,1`。源 LAYOUT 起手 `away_x=2000`，目标表现由节点路径控制。通用 NODE_ANI 播放使用这些原骨骼的位移、旋转和缩放，目标只移动渲染子节点并保持队形差值。播放使用演出源时钟，隐藏的目标在指定路径进入镜头，目标反应轨优先于默认受击。`disable_restore` 保留姿势到下一条同身体路径或动作结束；替换不让旧轨恢复新轨拥有的姿势，结束和中断恢复原局部姿势。屏幕坐标采用相机视口中心加源偏移的重建，精确锚点和配色叠加需与原作进一步对照。其他原图还存在 `target_node=76`、自定义 `pivot` 和类型 0 反应轨，本轮验证范围是当前三条绮世路径。
- COLOR_BLEND 出现在绮世 S3 和亚敏 S3。绮世源图分别区分 self 与 target，并给出持续、短闪和 STOP，亚敏 S3 另有 `blend_target=55`、进出各 300 ms。目标位的整数编码和色彩合成语义应按源证据独立记录。

## 锚点证据与边界

`MOVE31` 与同一窗口的 `CAM31` 见亚敏 S3。`MOVE41` 与 `CAM41` 见席可 S3、赛西莉亚 S3、绮世 S2 和夏绿蒂 S2，部分角色另有 `x=-79.2/-200/-72` 等独立偏移。夏绿蒂 S3 固定 `_05` 同窗使用 `CAM12` 和 `MOVE42`，后者带 `x=-50,y=-50`。已有项目 CAM 映射把 31 解释为施法者和主目标之间，把 41/42 解释为接受目标组锚点。

维德瑞 S1 的 `EFFECT12` 引用 `wildred_sk1eff_target.cfx` 并写 `locationParam=target`。夏绿蒂 S2 `EFFECT41` 用三组不同 X/Y 偏移的爆炸，同场 `EFFECT42` 是 `darkfire_pati`。这些字段支持扩展现有主目标、目标组锚点与偏移逻辑。源包内未找到足以区分 41 与 42 的精确规则说明，使用相同群中心属于当前项目近似。不能从 CAM 的现有映射倒推全部 MOVE、CFX 及镜像字段的原引擎定义。

## HUD 导入和校验

`Tools/BattleScene/ImportRosterHud.py` 读取 `Config/Tables/battle_heroes.csv` 与 `battle_skills.csv`，以 `iconAsset`、角色 ID、元素和职业收集源 HUD 图。职业资源别名是 `thief → assassin`、`soul_weaver → manauser`。默认只读来源为 `D:/sevenepic`。先编辑 CSV 并执行此工具，再运行导表，以便资产路径校验使用实际文件。对已有清单项若源哈希改变，须先核对素材版本。

本轮新增 21 张技能图、14 张角色头像、风属性图、盗贼和射手职业图，共 38 张原 PNG。`Skills`、`Faces` 与 HUD 根目录沿用现有路径，新增 Sprite meta 的 GUID 按仓库路径生成，既有 meta 保留。HUD `source-manifest.json` 记录原始路径、SHA-256、尺寸和 alpha 边界。全部新增图已逐字节比对；现有 `PreparePreviewArt.py` 校验通过，覆盖 137 项 HUD 素材、12 层森林和 3 张镜头纹理。十角色 CSV 完成后执行 `ImportRosterHud.py --check`，完整引用集 58/58 通过。
