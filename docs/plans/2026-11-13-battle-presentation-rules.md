# 战斗通用演出规则与 D 盘资料迁移

依据用户本轮指定的 B 站 BV16ZeU64E3Z 第一分 P，从3:08开始实际对照普通拉斯燃魂S2；视频发表于2026-09-19，本轮以用户指定片段为依据，不冒充最新客户端。现有 Unity/Spine/DOTween 足以读取原始镜头曲线，无需新增相机插件。

- [x] 镜头：BattleCinematicStage / 新镜头辅助类分离无震动基础镜头与临时震动；聚焦保留开局脚点构图，源坐标与朝向明确，地图可见时约束取景并补足远景覆盖，黑幕阶段允许源广角。原始 camera Spine 曲线播放、叠加及复位回归。
- [x] 位移：BattleSkillPresentation / BattlePreviewActor 以原 ANI_SCOPE 事件连接驱动 MOVE，包含未写 x 的竖向动作、style2接近、style3归位、影子地面轨迹、IDLE和释放。源CFX寿命和锚点统一，不加入角色ID特判。
- [x] 飘字：保留原 damage.fnt 的字形，按实际视频验证渐变及逐字弹跳；连击采用 combo.csb 真正引用的原图。显示总额按源 HIT 时间分配，最终和权威伤害一致，不让动画回调结算。反击/协攻动作独立累计边界、伤害/治疗隔离。
- [x] 回归：先运行新增失败测试，再实现；全量EditMode和图形PlayMode串行，真实九技能、敌我镜像、地图边界、取消恢复、多段总额、音频与飘字截帧。读取源数据或通过测试不等于全部角色还原通过。
- [x] 资料：C旧worktree的 .artifacts/.tools 复制到 D:/AstralOathData/legacy-worktree，逐文件哈希与源稳定性确认后移除源并建立junction。保留Git、源代码和用户改动。后续 TEMP/TMP、UPM、NuGet HTTP等由项目进程级环境指定到D，避免更改其它项目全局设置。
- [x] 文档与源资产哈希已同步，检查后使用中文提交；origin/main推送结果以远端提交及本轮交付为准。未建立足够证据的源语义列出具体缺口，不能用调参隐藏。

文件所有权：协调者负责镜头、场景收集与整合、公共文档和Git；位移代理负责BattleSkillPresentation/BattlePreviewActor及Motion测试；飘字代理负责BattleTurnHudFeedback/新反馈类与导入器、Damage测试；磁盘代理仅执行明确资料目录的迁移及报告。

本轮实际结果与限制见 [交付验收](../delivery/2026-11-13-battle-presentation.md)。回归完成指已列样本与行为检查，不代表所有源字段或原版逐帧一致。
