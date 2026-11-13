# 头顶 Buff 边缘复核

## 现象与修复

对比用户提供的两张截图。E7 参考中护盾图案与圆角有连续过渡。当前 Unity 截图中同一护盾和加速图案有重复像素列、斜线台阶与硬圆角。两张图的画面来源和缩放比例不同，无法由截图单独断定 E7 原客户端的采样设置。

本地原图 `buff/stic_speed_up.png`、`stic_protect.png`、`stic_att_up.png` 和 `stic_debuf_impossible.png` 的非透明最外圈为黑色，蓝色笔画在内部。28×28 状态图在本项目设计基准为 37.8×37.8，另乘以动态投影比例。前一轮的点采样保留了每个原像素的硬轮廓，此处会让斜线与圆角产生肉眼可见的锯齿。Unity 双线性会在相邻像素之间插值。 [Unity 2022.3 双线性采样](https://docs.unity3d.com/2022.3/Documentation/ScriptReference/FilterMode.Bilinear.html)

导入器仅对 `Hud/Statuses` 下的 15 个图标和 10 个回合字形使用双线性。PNG 字节、透明度转换、布局、角色缩放和状态逻辑保持既有实现；原始 PNG 与生成 PNG 的哈希均沿用原清单。局部采样覆盖继续禁用压缩与 mipmap。等级字形和行动条详情仍使用点采样。

## 验证入口

- `Tools/BattleScene/ImportStatusHud.py --check` 校验 25 张状态 PNG、来源清单和平台采样设置。
- `BattleStatusIconRenderingTests` 在显卡上渲染非整数倍的护盾图案，并检查内部颜色过渡、白底上的黑底亮度与透明圆角。
- `BattleUnitPresentationIntegrationTests` 在战斗场景验证状态图案、回合数字、角色移动缩放和可见性。截图输出目录为 D 盘主工程 `.artifacts/buff-edges`。

Unity 2022.3.62f3 完整 EditMode 465 项、定向 PlayMode 4 项通过。最终对照为同一战斗测试的相同局部等比放大，保存于 .artifacts/buff-edges/buff-before-after.png。Windows IL2CPP 和 Android 包未重建。
