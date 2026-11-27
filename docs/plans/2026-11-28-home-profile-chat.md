# 主页资料、公共频道与召唤清晰度 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 按用户录像实现主页资料和公共频道、统一未完成功能提示，并修正现有召唤文字/渲染清晰度及邮件公共文字。

**Architecture:** 客户端PlayerProfile与ChannelChat独立，Foundation拥有登录会话与窗口组合；Common保存通用提示素材预制，普通View/Binder留在既有热更Foundation，避免增加AOT类型。服务器Social模块内Profile/ChannelChat各自存储与端点，沿既有MySQL/Bearer/NDJSON，不复用房间成员聊天或引入新中间件。服务端权威、事务审计、幂等和客户端取消/迟到保护沿现有规则。

**Tech Stack:** Unity2022.3.62f3、UGUI/GameText/GameButton、HybridCLR、.NET10、MySQL8.4。

**Spec:** [用户要求、已看原帧、缺失参考与工程取舍](../development/home-profile-chat-reference.md)。取舍由工程协调者记录，不冒充用户选择。

## Global Constraints

- main主工作区，保留会话前361项改动；D:/sevenepic只读，资料/截图/构建只写D。
- 制作前实际查看并出示对应参考；缺失子页/过程先索取，受影响部分保持待办，继续独立工作。
- 预制仅BindingMap及基础组件；Foo/FooBinder普通C#；窗口由WindowCoordinator/独立BindingScope管理。
- Common不引用业务；仅协调者修改Packages/ProjectSettings、生成集成、Git、服务和分发。
- 协议/配置源为入口，服务端审计不含昵称全文、问候语、聊天正文、搜索词和凭据。
- 优先兼容当前R37母包签名热更，实际检查后才判断强更；变更与README/日志同批提交。

## Review Focus

- 自己/他人身份混淆：别人只能读允许公开的资料，不能改名、问候语或隐私。
- 旧重名与并发改名：保留旧数据，两个账号抢同一新名字只可一人成功。
- 关窗/切频道时迟到响应：旧资料、旧消息和请求不能回写新窗口/频道。
- 幂等与断线：聊天/资料写入原requestId重试不重复、冲突不伪造成功；明文不进审计。
- 4K/窄窗/重复打开：仅大图自适应，不拉伸卡片，不泄漏相机、RT、事件和音源。

### Task 0: 公共确认文字校正（根代理）

**Files:** `Assets/Game/Editor/ArenaUiBuilder.SourceMessageBox.cs`、Common对应材质/预制、`Tests/EditMode/MailPrefabTests.cs`。

**Interfaces:** 保留SourceConfirmation既有title/body/cancel/confirm契约及资源GUID。

- [x] 原版与43实际测色/墨面积，确认43颜色与字重不通过。
- [x] 新增回归实际RED4/8，保留日志；修源灰180、常规字重及细白描边，生成原预制。
- [ ] 复拍正确灰度候选，避免预先线性化导致117灰的失败；最终Edit/Play/Native均按当前源重验。
- [ ] 同步Common/Mail README、交付/状态/日志；由协调者按中文提交。

### Task 1: Social权威数据（待正式分配）

**Files:** 新`Server/Host/Social/`的PlayerProfileStore/ChannelChatStore、端点/DTO，下一编号迁移、`Server/Tests/*Social*Tests.cs`；根集成Program与README。

**Interfaces:** AddSocial/MapSocial；`GET /api/profile/me`、`/api/profile/players/{playerId}`；`POST /api/profile/nickname/check`；`POST /api/profile/me/actions`带requestId/expectedVersion/kind；`GET /api/chat/preferences`及`POST /api/chat/preferences/actions`；频道1..9999的`GET/POST /api/chat/channels/{channel}/messages`和`GET .../events`。realm来自认证，不能接受伪造主体。

- [ ] 先写隔离MySQL失败回归：本人/他人权限、50字/昵称规则、旧重名、并发认领、幂等/冲突、隐私遮蔽、跨realm/channel隔离、重启历史、游标/长轮询取消和审计无正文。
- [ ] 最小实现Profile与Chat独立事务和偏好；暂无权威Rank/战绩时available=false，不能造原作排名。
- [ ] 同源测试及真实两账号读写通过；不由代理迁移或替换正在使用的本地主服务。
- [ ] 根审阅/集成/提交与发布，保留原数据库和安全启动身份链。

### Task 2: 可复用资料页与左上卡（参考齐全部分先行）

**Files:** 新`Assets/Game/Modules/PlayerProfile/{Scripts,Res}`、专用生成器/测试；Home左上卡与Foundation集成由根拥有。

**Interfaces:** `PlayerProfile : UIBaseView`/`PlayerProfileBinder`，`PlayerProfileApi`读上述profile路由；`OpenAsync(playerId, BindingScope)`按响应isSelf/canEdit切换同一预制。对外提供更新摘要事件，不在Home引用该业务模块。

- [ ] 实际查看13秒详情、4秒昵称、18秒问候语及源CSB；制作前补准确资源清单，保留源切片。
- [ ] 先测试自己可编辑/他人不可写、隐私、过期请求/取消/重开及纯文本标签；再实现昵称查重/保存、50字问候语、隐私、ID复制。
- [ ] 左上卡绑定当前权威摘要，名字/头像改变后同步；不把参考账号Rank4/经验29.3%写成项目真实数据。
- [x] 用户明确排除录屏未见的小卡功能；代表/辅助英雄、边框、个性卡子页不在本轮，入口灰态并开通用提示，不继续等待或拼临时子页。
- [ ] 原版/修前/修后及真实两账号/窗口尺寸验收后提交。

### Task 3: 公共聊天客户端（根及后续分配）

**Files:** 新`Assets/Game/Modules/ChannelChat/{Scripts,Res}`、生成器/测试；Foundation会话/顶栏入口集成。

**Interfaces:** `ChannelChat : UIBaseView`/`ChannelChatBinder`，`ChannelChatApi`、登录级`ChannelChatSession`；默认channel1。玩家点击以`Action<string>`交给Foundation开同一资料页，不反向依赖Profile模块。

- [ ] 实际查看32秒抽屉、37.5收藏、42.5/45.5通知和源CSB；实际消息行参考待补，先完成已看清框架和后端契约。
- [ ] 先测同频道双账号实时可见、不同频道隔离、切换/关窗/重连、历史去重、长文本与纯文本、偏好保存、发送幂等。
- [ ] 实现频道1..9999、历史/NDJSON、表情和用户消息；关闭页面不串改登录session，退出销毁网络。
- [ ] 骑士团入口只开通用未完成提示；按用户要求开放公共频道，不静默复制原视频10级禁发。
- [ ] 两实际客户端与持久化重开验收，未确认原UI差异单列，不称完整还原。

### Task 4: Home菜单与通用未完成提示（根）

**Files:** Common新增`FeatureNotice.prefab`/普通View/Binder、Home生成器/窗口/菜单、Foundation协调器、本地化源及测试。

**Interfaces:** 通用提示接受调用方解析后的title/message和所属scope；Common不包含业务功能枚举。Home声明导航能力与Action回调，由Foundation映射现有功能。

- [ ] 实际看原主页右侧/更多菜单、61.5秒条件横板及源素材；布局和未完成项清单先记录。
- [ ] 先测灰态仍可点击开提示、提示声/关闭/重复打开与页面取消；现有背包/英雄/召唤/邮件/模拟战导航继续可用。
- [ ] 未实现入口保留原样式并置暗，统一提示「功能尚未完成」；不伪造进度解锁条件或开放功能。
- [ ] 1080/4K/窄窗、连续导航及原音路径实际验收后提交。

### Task 5: 召唤清晰度与文字（待正式分配）

**Files:** `Home/Scripts/HeroRosterPortrait.cs`、`Summon/Scripts/SummonWindowBinder.cs`、召唤专用生成器/材质、Play检查及模块README。

**Interfaces:** 原片可见呼吸/眨眼，七人页使用原有animated路径播放idle并自适应实际投影；不增加无必要新静态参数，其他静态列表预算保持。窗口变化重绘源骨骼，隐藏暂停，Dispose释放RT/相机/监听。

- [ ] 原图/当前字体、源图片/导入/实际RT先测；480²静态RT为已定位问题，背景压缩若无证据不猜测。
- [ ] 先写实际RT不足/窗口变化/释放回归，再实现七人页显式自适应；观察4K内存与纹理尺寸上限。
- [ ] 名字/未获得/愿望单/按钮分别按新录像量字号、常规斜体、描边与灰度，只修现有池，不伪造新卡池。
- [ ] 原生4K/1080当前产品源截图与像素/性能验收，再提交。

### Task 6: 集成、验收和签名分发（仅根）

- [ ] 所有代码/资产与README/日志同步、独立审阅；`node Tools/check-docs.mjs --staged`通过后只提交拥有的文件。
- [ ] 当前母包兼容检查、签名构建，实际Native邮件/资料/聊天/导航/召唤与暖缓存检查，原声试听边界明确。
- [ ] 提供原版/修前/修后图录、文件位置、真实通过/失败/未执行/参考阻塞；源码、截图和包同版本，旧入口受控直达新内容。
- [ ] 保留用户已有改动/程序、主MySQL身份；只关闭本轮自行启动且身份匹配的隔离验收进程。

## 11/28当前执行检查点（不是最终验收）

- 已完成最小原素材导入、Profile/Home/Chat/CommonNotice及Root组合；联合Editor53/53和Play14/14。资料昵称/人物/问候三处自行比图失败后已修并生成新六图；记录区缺源背景/分隔与Divider过亮另有真实RED2，继续按源图/CSB补正。
- Backend677/677，隔离QA已迁移018/019/020并运行。主服未切，R44后端助手离线24/24，备份/隔离恢复/主库迁移尚未执行。
- 第二次独立审查发现客户端own-send回执可能跳过未读取他人消息；真实RED预期after1实际after3，正在改独立服务器读取游标，不能用旧数据层11绿遮盖此P1。
- R37兼容检查实际失败于两个被裁剪的BCL调用：Comparer.Create和MediaType getter，正用相同语义的已保留路径修订，须重验；不能据此预先称已兼容或立即构建强更。
- Native25状态+12召唤连续帧驱动与Limited桌面runner已准备但未执行；截图不代替人工试听。最终产品源pin/签名44/Feed/旧入口/主服升级及关机仍待根完成。
