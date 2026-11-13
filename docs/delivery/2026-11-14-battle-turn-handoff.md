# 战斗回合交接帧时复测

2026-11-14，D 盘主工程，Unity 2022.3.62f3 图形 PlayMode。玩家录屏的两段原作视频用于对照动作和 UI 时序，其中第二段梅赛德丝约 11–12 秒、29 秒附近可见同一人开始下一回合时回位、扬尘和技能组恢复。录像显示 ×1；客户端版本未确认。

## 已测结果

- `BattleTurnHandoffPerformanceTests` 走真实玩家 S1 按钮和目标选择，等待敌方 AI 提交及演出后显示下一回合。计数以显示角色变化或 Busy→空闲区分。测量涵盖 8 次回合交接、4 条玩家命令和 4 条敌方命令。
- 相邻 PlayerLoop 帧时间用 Stopwatch，业务帧用 `Astral.Battle.Frame`，交接刷新用 `Astral.Battle.Refresh`。回合交接相邻帧 2.30–9.90 ms，业务帧 0.886–7.162 ms，HUD/回合刷新 0.460–1.297 ms。该批约 41,397 帧 P95 为 2.431 ms，最高 30.550 ms。环境为未限制帧率的批处理 PlayMode，结果包含批处理调度，不能推算玩家实机帧率。
- 证据见 `.artifacts/ten-hero-PlayMode-inspection-pass4.log` 中 `PERF TurnHandoff`。无数百毫秒交接刷新，当前数据不支持把准备动画或 HUD 刷新认定为 CPU 卡顿根因。本轮保留已有演出调度和回合交接，增加测量入口，Windows IL2CPP 与 Android 真机未测。
## 梅赛德丝回合准备扬尘

- `model/mercedes.scsp` 的 `b_idle_ready` 在 0.5333 秒触发 Spine `dust` 事件。该事件由当前回合的准备动作触发一次，实际播放点以模型事件为准。 `model/mercedes.timeline` 的 1093 毫秒音效轨未提供参数。
- 选用源 `effect/dust.scsp`，其中含 `dust_landing/dust_landing2` 贴图和原始颜色时间线。源 `dust.atlas` 别名指向 `effect.atlas`，复用已入库对应贴图页。通过 `Tools/BattleScene/ImportTurnEntryEffects.py --check` 校验来源与转换结果。事件参数为空，扬尘资源的对应关系来自事件同名和落地贴图证据，客户端映射表仍未确认。
- 战斗初始化阶段创建并预热一个播放器。落地位置取该单位当前影子，位置采样后留在世界空间。暂停冻结效果，重来和退出释放订阅及播放器。后续导入角色先检查准备动作中是否存在同名事件；相同事件可复用，其他事件须核对其对应的原始演出依赖。

- `native-final` 实景回归确认事件前无扬尘、事件后网格位于影子、暂停冻结、重复 Ready 不重播、下一回合复用和重来销毁。该批另测 8 次交接，约 41,577 帧，P95 为 2.436 ms，交接帧 2.276–9.197 ms，最慢业务帧 6.756 ms，刷新 0.470–1.162 ms。以上同为无帧率限制的 Editor 图形批次。

- 行动条保留固定排序规则，重复使用排序列表。折叠时仅更新可见头像与进度目标，详情行在展开时一次刷新到最新快照。详情开启后仍跟随变化。此改动移除普通回合交接中隐藏详情的文字、图标和状态行重排。

- 折叠详情优化后同样复测八次交接，交接帧 1.957–9.064 ms，刷新 0.305–0.632 ms，约 42,183 帧 P95 为 2.400 ms。样本只能说明此机器、此阵容的 Editor 批次差异。证据见 .artifacts/ten-hero-PlayMode-native-visual.log 的 PERF TurnHandoff。
