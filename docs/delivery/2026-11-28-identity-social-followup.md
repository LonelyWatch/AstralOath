# R47资料、频道与召唤追加修订

本机Windows新包已完成，入口[D:/UnityProjects/AstralOath/.artifacts/windows-r47/Play-R47.exe](D:/UnityProjects/AstralOath/.artifacts/windows-r47/Play-R47.exe)。内容版本47，app仍0.1.27，复用R37基线`7445597f1ef645db8ef946c4964503fc`，没有强更。升级完成后按更新器提示退出并重开，下一进程加载新业务。

**用户最后明确本轮无需回归、由其验收。** 本轮未执行新Editor/Native功能回归、未捕获修后截图、未试听或观看修后连续特效，不声称视觉还原通过。下面的生成、编译、签名和服务就绪仅证明相应事实。

## 修改与依据

- 资料底板：原录像13秒与R46修前透底图对照定位，恢复原Dim、源蓝渐变与阴影的Linear覆盖，保留灰边。原图/修前在`.artifacts/identity-social-followup/profile/`，本轮修后图未拍。
- 公开身份：随机且唯一的永久十位UID，数据库范围/唯一约束及同事务分配；资料右上显示并复制`UID`，鉴权`GET /api/profile/uids/{uid}`支持后续查找。内部ID不作为公开编号回退；好友系统不在本轮新增。
- 频道：房间右栏新增公共频道tab，内嵌复用同一ChannelChat Session/Binder；登录级未读显示通用原数字角标，真正显示到底后清除，覆盖层关闭也重新判定。源32秒频道和原top_bar角标分别列在`.artifacts/identity-social-followup/chat/`，不把原片没有的角标捏称录到。
- 提示/按压：通用未完成提示进入0.18秒、退出0.16秒，取消/复用归BindingScope；这是项目时长参数，61.5秒参考仅为稳态。召唤信息、愿望单、资料/聊天等大面积关闭/阻挡区域不再递归压暗整板；小按钮保留反馈。
- 召唤：普通五星英雄/神器用源金色星光，只有五星光暗英雄用紫色星光且十连优先级更高；新增原CFX四份粒子定义、三张原贴图，沿用既有音轨，问号取消额外68%缩小。源轨与修前代码在`.artifacts/identity-social-followup/summon/`；修后连续时序、声音未验。

## 构建与本机发布事实

- 客户端产品源`b2641de7bda407f1b42dcaebc94310825a15e333`；发布准备修正源`7b4628d1d1e0d5f7aca0d1a9d1e53925149d3fd5`。原用户361项工作区改动保留，两份用户字体未提交或覆盖。
- 指定7份预制/演出资产生成退出0；已有GUID保留，未回拷镜像字体。最终HotWindows退出0，输出`.artifacts/b47-final/Win64/hot-v47-d05e04bc2be844dfa47a3237fe0a7595/`；未分发首轮未签名候选。
- 签名和逐payload完整性检查通过：108项2,353,885,286字节；R46→R47清单变化14项202,777,867字节（约193.38MiB），属于理论差量，不是实测传输。启动器、复用母包及输入哈希见`.artifacts/windows-r47/package-prepared.json`。未重复生成同母包zip。
- 独立R47后端DLL SHA256 `b88919fcaa9b5576aff740d31118275aa556be328356211865b897d9a9ee8e7f`；实际备份现有开发库后完整`migrate`退出0，20→21，UID初始化完整；MySQL PID37360保持，未清库/恢复主库。R47 Host PID36960及鉴权路由已就绪。记录`.artifacts/windows-r47/server/results/b6cc93f9199047dab4a16eeffcaee2b7.json`，不公开私密备份内容。
- 本机5305现提供已签名47：`.artifacts/identity-social-followup/feed47-final.json`。首次迁移清单规范化及内容源身份检查脚本的失败候选已保留，修正后完成，未终止未知进程。未部署生产。
- UID服务端683/683和最终6/6是在用户停止回归指令前已执行的结果；其余本轮客户端测试只保留源码，未执行。没有用旧R46画面证明R47。

## 文件与待验收

完整绝对路径清单：[changed-files.md](../../.artifacts/identity-social-followup/changed-files.md)。主要入口：`Assets/Game/Modules/{PlayerProfile,ChannelChat,Arena,Summon}/Scripts`、`Home/Scripts/SummonPresentation.cs`/`SummonParticles.cs`、`FoundationDemo/Scripts/FeatureNoticeBinder.cs`；对应编辑入口`Assets/Game/Editor/ArenaUiBuilder.*.cs`和`SummonPresentationBuilder.cs`。源导入器`Tools/{Profile,Summon}`；服务端`Server/Host/Foundation/PlayerPublicIdentity.cs`/`Migrations/021_player_public_identity.sql`、`Social`；本机准备与启动`Tools/LocalPlay/*R47*.ps1`。各模块README和通用规范同批更新。

未完成的是用户明确接手的实机效果验收：新包淡变、资料透射、未读双客户端、竞技场内嵌、金/紫星光连续过程和听感，以及首次升级/重开后的实际界面；影响是当前没有这些状态的修后实机证据。最小下一步为打开新入口，完成更新并重开后按相同操作验证。没有新增好友/小卡未录子页或骑士团玩法。包为现工作区本机试玩入口，依赖同目录和现有D盘工具，不称独立可移动正式安装器。
