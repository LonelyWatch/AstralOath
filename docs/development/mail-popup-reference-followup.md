# 邮件确认框与拒绝提示复核（2026-11-27）

本次为用户指出的 R41 邮件后续修订。当前文档记录原版依据、修改前根因与可复现的源素材生成；客户端生成、图形/原生和分发结论由本次交付回执另列，本文不将源码完成写成原生验收通过。

## 实际查看的依据

用户原片 `Screenrecorder-2026-11-22-15-12-33-596.mp4`，文件名日期为 2026-11-22；原作发布日期、客户端版本与可见倍速未确认。本轮实际打开了 84.000 秒「全部领取」确认、94.000 秒「无法使用全部领取的邮件。」及 96.000 秒已无该提示的原帧，也打开了用户本次 `codex-clipboard-888a662c-f9b0-4bdc-a4c5-4f9c74047a23.png` 的修改前画面。

原帧位于主工作区 `.artifacts/mail-system-review/frames/{084.000-claimall-confirm,094.000-only-gm2-refusal,096.000-only-gm-refusal}.png`。原片确认按钮的上下边具有明显直线中段，R41 的金色取消与蓝色确认被画为椭圆；R41 上下白线也明显更亮。原版确有上下细线，不能改成完全无边框。

## 源节点与根因

只读源为 `D:/sevenepic/原始资源/wnd/msgbox.csb`；本轮提取结果为 `.artifacts/mail-ui-followup/popup-reference/reference-audit.json`，工具 `audit.py` 复用项目 `InspectDraftLayout.Csb` 并按公开 Cocos 字段提取 cap、scale9Size、scale9Enabled。源节点尺寸是作者布局数据，不等于录像运行时未经调整的最终高度。

- 背景源 `img/renew_popup_bg_2.png`，原纹理 256×410，节点 1280×390、opacity 216、cap `(0,30,256,350)`。当前 Common 已有 `SourceMessageBox.png` 用途副本，白色细边单独执行线性覆盖率校正。Mail 却直接使用另一份未经该校正的 `renew_popup_bg_2_alpha.png` 并白 tint，绕开了已有共用修复。例如源中心列第 4 行 `_alpha` 白线 alpha58，Common 副本为13，再应用源 node opacity216；不能直接使用原 alpha 在 Linear UGUI 中混合。
- 按钮源 `img/_btn_gold_r_2.png`、`img/_btn_gold_r_p_2.png`、`img/_btn_blue_r_3.png`、`img/_btn_blue_r_p_3.png`，均为 200×128。源节点 size388×128、scale `.53×.53`、cap `(100,64,1,1)`；Unity border 是 `(100,63,99,64)`。这组圆角按钮和装备蓝色直边按钮有不同用途。
- Mail 的 `Control` 只应用 Canvas 高度 1.5 换算（ppu2/3），没有保留节点 `.53` 的缩放。左右端帽合计298.5设计像素，超过控件宽度，被 UGUI 挤满整宽，原本应拉伸的中段消失，于是变成椭圆。正确的 design size 为 `388×128×.53×1.5 = 308.46×101.76`，对应 ppu `1/(.53×1.5)=1.2578616`。

## 共用生成入口

`Tools/Arena/ImportMessageBoxButtons.py` 只导入上述四张原 PNG 到 `Assets/Game/Common/Res/Textures/MessageBoxButtons/`，保持字节不变，记录源/输出 SHA、cap、节点尺寸及缩放。运行 `--check` 检查源哈希、导入 border、未压缩配置和清单。

`Assets/Game/Editor/ArenaUiBuilder.SourceMessageBox.cs` 提供 `BuildSourceMessageBoxControls()`，生成 `Common/Res/Prefab/Styles/RoundedBlue/ConfirmButton.prefab`、`Styles/RoundedGold/CancelButton.prefab` 及 `Common/Res/Prefab/SourceConfirmation.prefab`。两种按钮继承既有通用 button/label，确认使用已有 `UiAccept`（`event:/ui/msgbox/btn_yes`），取消使用已有 `UiCancel`；`FeedbackSound.Normal` 使控件使用自己配置的 AudioSource，避免改成普通 `btn_normal` 或重复播放。完整确认框嵌套这两种按钮并复用 `PaintSourceMessageBox`，业务只绑定 title/body/cancel/confirm/cancelLabel/confirmLabel。只使用已有 BindingMap 和基础组件，不引入新的运行时/AOT类型。

本轮源导入与 `--check` 已通过。Editor 生成、编译、普通点击/按下/窗口缩放、最终视听与原生打包尚由协调者进行；不可由源参数正确推断所有显示状态已正确。

## 拒绝提示音证据

对原片既存 12kHz 单声道音轨和当前 `Common/Res/Sounds/UiBlocked.wav` 重跑归一化交叉相关：93.0424 秒得 `.8740`，94.2584 秒得 `.8585`。该文件来源事件是 `event:/ui/popup/down_neg`，SHA256 `32e0836ea830ddaf44502b94c2468afcb09823a1c6b0a568b09cbaa35de3fbee`，与 Common 原音清单一致。算法与数据完整写入本轮 `reference-audit.json`，不会重复导入另一份音效。

这是波形定位，不是实际试听结论；`listeningVerified=false`。94 秒原帧可以看到蓝条、96 秒已经消失，但中间存在再次点击，因此两张静态图不能独立证明某一次提示的精确时长。排序蓝提示的具体原片过程仍缺参考；其自动消失和声音按用户本次明确要求接入通用提示，不写成已看到原作排序过程。
