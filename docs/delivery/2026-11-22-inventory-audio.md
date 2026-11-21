# 英雄与背包界面音效核对

2026-11-22 对齐用户提供的四段国服录屏。以下时间均为本地 MP4 的播放时间，录制日期取自文件名，公开发布日期和客户端版本未确认。观察的是菜单操作，未从操作间隔推定游戏倍速。

## 已对齐的操作

- **打开背包。** 11 月 17 日 22:28 录屏 00:02.639、00:14.564、00:42.383，顶栏背包打开时响 `ui/top_bar/inventory`。第一个位置与发行音频的归一化相关系数为 0.903。点击格子、职业或分类页签继续使用普通按钮音 `ui/btn_normal`，可对照同片 00:05.691–00:09.830 及 11 月 21 日 22:31 录屏 00:02.973–00:08.712。
- **快捷菜单与英雄页面。** 11 月 17 日 22:21 录屏 00:02.373、00:06.742 确认展开与收起分别为 `ui/top_bar/quick_menu_on` 和 `quick_menu_off`；进入英雄页、关闭菜单或点击外侧均经过同一收起路径。英雄页出现时另有 `SE_UI2_LOBBY_BASIC_SMALL_MENU_1.wav` 的声音，见同片 00:07.946 和 11 月 21 日 16:39 录屏 00:33.541。采用只含该采样的 `ui/menu/menu_1`，后一个时间点与发行音频的起音相关系数为 0.835。场景预读、刷新库存或换卡不会重播英雄入场声。
- **页面返回。** 顶栏返回、普通装备配戴和管理返回、神器配戴和强化返回使用 `ui/btn_back`。11 月 17 日 22:28 录屏 00:12.409、00:31.055 及 11 月 21 日 22:31 录屏 00:22.406–00:26.523 均可区分页面返回与弹窗中的普通点击。竞技场已核对的取消声仍使用 `ui/msgbox/btn_cancel`。
- **普通装备。** 11 月 21 日 16:39 录屏 01:18.738 是确认按钮声，01:18.928 才响 `ui/equip/equip_ring`，后者相关系数为 0.978。01:36.997、01:50.736、02:37.136 和 02:47.973 可再次辨认戒指配戴。01:45.020 是解除按钮声，01:45.168 是解除后的 `ui/equip_off`，相关系数为 0.723。客户端在服务端确认配戴或解除后播放对应声音，取消和失败保留界面原本的反馈。
- **神器。** 11 月 21 日 22:31 录屏 00:31.069 的确认和 00:31.217 的神器金属音可以分开。后者为 `ui/equip/equip_artifact`，相关系数 0.942，00:52.337、01:02.279、01:24.316 又出现相同采样。已穿详情的锁在 00:42.929、00:44.017、00:45.020、00:45.937 反复切换，两种状态都有 `ui/unit_detail/btn_lock` 的短响，相关系数达 0.982。此处单响，等待事务完成再播。锁定英雄本体是另一项操作，在 11 月 17 日 22:21 录屏 00:22.206/00:22.867 可区分 `btn_unlock` 和 `btn_lock`。

## 六种装备槽

原库 `sound/ui1.yml` 和 `sound/events.yml` 分别给出装备事件、采样和 GUID。戒指已有成交流程的录像验证。其余五槽按用户确认的原库依据接入，尚缺同版实机中这五种物品分别配戴成功的录音。

- 武器使用 `equip_weapon` 和原始采样 `SE_UI2_LOBBY_HERO_EQUIP_WEAPON.wav`。
- 头盔使用 `equip_helm` 和 `SE_UI2_LOBBY_HERO_EQUIP_HEAD.wav`。
- 护甲使用 `equip_armor` 和 `SE_UI2_LOBBY_HERO_EQUIP_ARMOUR.wav`。
- 项链使用 `equip_neck` 和 `SE_UI2_LOBBY_HERO_EQUIP_AMULET.wav`。
- 戒指使用 `equip_ring` 和 `SE_UI2_LOBBY_HERO_EQUIP_RING.wav`。
- 鞋使用 `equip_boot` 和 `SE_UI2_LOBBY_HERO_EQUIP_SHOE.wav`。

## 验证

将录像与原库音轨转为 12 kHz 单声道，对差分后的前 133 ms 做归一化互相关，再核对命中位置前后画面。英雄入场声另比对整段和后半段，区别共用起音的候选事件。发行使用 FMOD Studio 2.03.15 在原生 48 kHz 下重放事件得到的双声道 PCM。没有把录像的音乐、语音或环境声拷进客户端。[原事件、采样、帧数及 SHA-256](../../Assets/Game/Common/Res/Textures/ui-feedback-source.json)

独立 Unity 工程的初测有 14 项失败，包括旧镜像未含卡片滑动声。最终 `InventoryFeedbackAudioTests` 的 17 项通过，逐项打开实际启动场景，核对枚举、音频依赖、资源路径并读取非静音 PCM。已对 13 个新增事件再次渲染，逐字节相同。录像配对试听文件 `.artifacts/inventory-audio-r32/source-game-audition.wav` 依次包含戒指、神器、解除与锁，每组先录屏后发行原声；`wearer-audition.wav` 按武器、头盔、护甲、项链、戒指、鞋、神器、解除排列。

```powershell
.tools/dotnet/dotnet.exe build Tools/BattleScene/AudioProbe -c Release -o .artifacts/inventory-audio-tools
.tools/character-assets/Scripts/python.exe Tools/Common/ImportInventoryAudio.py --check
```

动作依据来自本机的 `Screenrecorder-2026-11-17-22-21-00-778.mp4`、`Screenrecorder-2026-11-17-22-28-01-55.mp4`、`Screenrecorder-2026-11-21-16-39-58-424.mp4` 和 `Screenrecorder-2026-11-21-22-31-26-469.mp4`。原库保留只读。逐项操作画面对照和 Unity 报告位于 `.artifacts/inventory-audio-r32/`。
