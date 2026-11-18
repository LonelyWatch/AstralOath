# 原版召唤演出与音频对照

以 2026-11-18 19:54 的 2608×1200 用户录屏为参照。资源源为本机只读目录 D:/sevenepic/原始资源。资产清单与 SHA-256 位于 Summon/Res/Presentation/source-manifest.json，Unity 从 SummonPresentation.asset 引用转换结果。整段演出以已经提交的服务端结果控制。切换窗口和跳过不影响抽取结果。

## 素材与流程

- 开场采用 gacha_effect_1_blue.cfx、gacha_effect_1_yellow1.cfx 和 gacha_effect_1_violet1.cfx。使用原布局的十一层骨骼，包括两位引导角色 n_bibleo、n_biblika、书本、翻页、光柱和背景分层。
- 教堂及雕像使用 gacha_effect_2_b_blue／yellow／violet 配 gacha_effect_2_blue／yellow／violet。普通、金、紫各自取原版动画通道。职业雕像使用独立的 gacha_effect_2_class 与 gacha_eff_2_figure。原版测试分层指定 z=35，叠于大殿之后与爆光之前。六职业和神器各有原版雕像动画。原版颜色提升的青转金、青转紫、金转紫三组也有导入记录，基础流程按已确定稀有度选取通道。
- 结果采用 gacha_effect_3_b 的 2.4 秒入场与循环，英雄分用 character_bone、portrait_bone，神器使用 artifact_bone，各与 gacha_effect_3_f 前置光效合成。英雄立绘和神器插图沿对应原版骨骼轨迹移入，英雄缩小骨骼沿 character_small 入场，循环保持站立。在现有 Windows 画面将演出视口匹配至约 1440 个原始动画坐标单位高，按当前窗口宽高比扩展可见面积。跳过时把 3_b 置于循环起点，保持结算原版斜角教堂背景。
- gacha_eff_3_intro_1.scsp 的 pivot 旋转、缩放时间轴各重复一次。转换器仅对这份资源接受二进制相同的那对重复记录，继续校验其余偏移、动画名和曲线。用 Unity 的 Spine 2.1 承担显示。
- 原包中另有带粒子追加的 yellow2／violet2 以及月光和假升变体。月光专池和可选升色演出随对应模式另验，目前启用普通、金、紫三套同名原版骨骼演出。录屏中的五星揭晓 Kika 对话插入尚未接入，也未核实该片段的独立语音映射。

## 原版音频核对

- 原版音效事件由 sound/events.yml 确认 GUID 和所属 bank。用项目 FMOD 的 WAVWRITER_NRT 输出 48 kHz 双声道。活动取得语音从原版获取事件对应的日语样本表导出，当前已收录本地英雄目录的 30 名角色，保留夏绿蒂 voc_c1009_gc_1 别名。未收录的英雄没有替代台词。所有录音可由源码和原始 bank 重建，客户端热更无需内置完整新 bank。
- 原版现代音效事件位于 event:/ui/gacha_new/gocha_effect_*。01 的 normal、rare、special 对应三个开场。02 是从法阵切入教堂的过渡，03 是雕像，04 的 normal、rare、special 各是对应稀有度的揭晓光效。05 在汇总页出现时触发。神器专门使用 event:/ui/gacha/summon_result_artifact，复用原版神器揭晓音效。翻卡、卡片准备及首次取得采用独立原版事件。
- 对用户录屏的本地音轨重采样至 12 kHz，按前 0.79 秒音频片段的一阶差分作归一化匹配，并用每 0.5 秒画面核对。02 在约 100.43、150.05、190.90 秒匹配。03 在约 103.63、110.18、115.77、121.18、153.18 秒匹配，归一化相关约 0.86–0.87。普通 04 在 105.97、112.54 秒约 0.84，金色 04 在 118.18 秒约 0.74，紫色 04 在 123.59 秒约 0.51。05 在 129.29 和 155.15 秒匹配汇总转场，相关约 0.63。开场提前叠加过渡音，雕像到稀有度爆光采用实录约 2.36 秒间隔。神器单独揭晓音和两段原版背景音乐的所属事件已验证，给定录屏未提取到同样强的波形证据。
- 原版 BGM 来自 bgm4.bank 的 summon_title 与 summon_efx。背景音乐使用单独流式通道，退出召唤释放。结果跳过同时停止残留音效。

## 录屏中的额外变体

- 五星英雄对话。录屏 121.18 秒为雕像音，123.20 秒右侧仍无 Kika，123.40 秒从右侧入画，123.80–124.00 秒可见头像与「你的缘分会让你变得更特别。」原始包 effect/n_biblika.atlas 中的 lika_* 人物素材对应这一角色，配套 effect/n_biblika.scsp 只有开场翻书动画。spani/biblika_node.atlas 为空。已查找已解包的布局、故事资源名、FMOD events.yml 和各 bank 索引，尚未定位这段独立动作与专用配音。录屏只证实该次五星英雄揭晓触发，对所有五星、仅首次取得、不同卡池或随机触发仍缺独立证据。
- 金色和紫色开场粒子。原包 gacha_effect_1_yellow2.cfx（SHA-256 前 12 位 1365dea46dac）与 gacha_effect_1_violet2.cfx（a40b7daed81e）在 x=-50、y=-30、z=55、1,000 ms 处挂同名颜色的 *_pat.particle。第二段 gacha_effect_2_yellow_pat.cfx（06c2d93b7e5d）和 gacha_effect_2_violet_pat.cfx（37b0a8493a26）各在 y=-200、z=60 挂对应粒子，独立文件未指定延时。原版 gacha_effect_2_b_test.cfx 在 3,700 ms 延时加入金色粒子。粒子定义引用原版 particle/flash.sct、particle/zodiac_star1.sct 和 particle/pati_03.sct。当前基础流程使用原版 *_yellow1、*_violet1 和骨骼爆光，以上额外发射器及与正式流程的触发关系尚未接入和验证。
- 假升变体。三组可变色骨骼的 CFX 分别为 gacha_effect_2_blue_yellow、gacha_effect_2_blue_violet 和 gacha_effect_2_yellow_violet，SHA-256 摘要分别以 b281de52de2d、97ab6412edf6、736bd66e9bbd 开头。另有同色强化文件 2_yellow_yellow 与 2_violet_violet。gocha_effect_04_fake_rare、04_fake_special、04_fake_special2 已从原版 FMOD bank 导出。gacha_addeff_02_fake.cfx 的两层原版旋转星光位于 z=102，由 gacha_addeff_02 的 ray_ball 区域播放，原始重复参数是 13 次、间隔 40–80 ms，播放倍率 0.7。同资源的 special 文件位于 z=52／53，重复 24 次。录屏和只读数据尚不足以确认各卡池触发规则，该部分尚未接入基础流程。

## 概率 UP 页技能预览

- 录屏在约 6.0–14.5 秒显示梅铃的三技能。立绘先淡出，随后依次显示龙形特效、小人动作、手绘特写、收尾爆光和战场远景，最后回到立绘。左侧卡池列表和右下召唤按钮在播放期间持续可见。约 30–39 秒和 48–54 秒切换其他角色时可看到同类演出，出货后的蓝色教堂英雄揭晓则保持立绘与小人。
- 原版梅铃技能由 stagept/c_meilin_skill_03.stg 指向 cut/fhd/sk_c1191_3.webp。兔女郎黑手党优芬妮由 stagept/c_yupine_a02_skill_03.stg 指向 sk_c7016_3_1.webp 与 sk_c7016_3_2.webp。原包的 cinema 视频中未找到当期逐英雄召唤预览，项目当前活动池英雄为 c1024 伊赛丽亚。
- 当前伊赛丽亚预览从原版 stagept/c_iseria_skill_03.stg（SHA-256 前 12 位 b03e4543232d）、model/iseria.timeline（82ad7ed4807f）、cut/fhd/sk_c1024_3.webp（69e2d04cb5c1）及其摄像机、粒子、贴图、骨骼依赖生成。沿用现有技能演出引擎和单体目标规则。渲染输出 960×540、30 fps、436 帧。手绘特写在 2.9336 秒开始，持续 1.67 秒。最后一击在约 9.87 秒，展示保留回到战场的画面，11.208 秒切回立绘。客户端图集使用 24 fps、269 帧，现有母包即可读取。
- 音频从原版 event:/skill/c_iseria_skill_03（GUID aeefb2c6-0f86-4ee3-a848-b4db99372674）与 event:/voc/skill/c_iseria_skill_03（GUID 24e5dc35-73bc-4171-8e4f-ffd4d45334c6）混录，沿用 FMOD 事件起点和内部时间轴。原始 bank 为 sfx1.bank（6a01d3cded5e）、voc.event.bank（1f2ddf6409e6）和 ja/voc1.audio_ja.bank（0ce45d4f6ad1）。技能音效事件原长 10.820 秒，样本使用日语 voc_c1024_skill3_1_1、voc_c1024_skill3_1_2。打包音频为 48 kHz 双声道 538,000 帧，尾端淡出 100 ms，实测峰值 −2.40 dBFS，削波样本为零。
- 现有演出引擎仍会为 3 处未确认定位的特效绑定主目标，并报告 iseria_skill_03_final_front 的 Fire 动作未映射。默认移动曲线和跳跃轨迹沿用项目复原结果。录屏内的梅铃及其他卡池仅用于视觉结构对照。

从 Tools/Summon/SummonFeaturedSkillCapture.cs 复制导出入口到独立 Unity 工程 Assets/Game/Modules/BattleScene/Editor，确保独立工程中的已生成 BattleScene.unity 包含伊赛丽亚技能图。指定 ASTRAL_SUMMON_SKILL_FRAMES 为存在的临时目录，用 Unity 的 `-batchmode -quit -executeMethod AstralOath.Client.BattleScene.Editor.SummonFeaturedSkillCapture.Run` 图形模式生成帧。主工作区的 .artifacts/summon-presentation/Unity-iseria-s3-final.log 保留采集结果。AudioProbe 的一次参数组可用 `|` 分隔两个事件路径、两个带花括号的 GUID 和三个 bank 路径。输出按 11.208333 秒裁剪，并对末尾 100 ms 淡出。

## 验收命令

从主工作区运行 Tools/Summon/test_native_summon.py 校验三个基础色和原版揭晓分组。先构建 Tools/Summon/AudioProbe/AudioProbe.csproj，再运行 Tools/Summon/ImportSummonPresentation.py --check 核对原始素材与导入结果。Unity 生成入口为 AstralOath.Editor.SummonPresentationBuilder.Generate。Windows Unity 图形测试按录屏 1564×720 的等比例视口验证三种颜色开场与教堂、职业雕像、英雄和神器的入场位移、拉斯骨骼与结算背景、跳过清理。三类职业雕像与关闭该层的画面相比，各有 14,323／13,230／12,739 个像素的 RGB 总偏差至少为 15。截图、无雕像对照和 Unity 日志保存在主工作区 .artifacts/summon-presentation。尚需实机完成整段交互节拍、扬声器听感及性能验收。
