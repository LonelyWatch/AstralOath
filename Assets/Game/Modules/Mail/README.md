# Mail

11/28 R44候选重新生成共享顶栏ChatEntry嵌套；Mail不拥有公共聊天业务，Foundation只在当前Mail opening scope绑定该入口。主页面全树扫描排除缓存Mail portal，关Mail会取消由它打开的Chat，重开只绑定一次；实际Play行为回归通过。共享确认文字用源灰180、常规字重/.06细轮廓；43纯白/过重被用户否定，失败图保留，不能因功能通过声称视觉一致。联合Editor53/53、Play14/14；Native44/包结果另验。[本轮上下文](../../../../docs/development/home-profile-chat-reference.md)

11/28后续用户复核明确43说明过白、按钮过粗，因此43下文仅是功能/构建阶段历史。当前44文字候选在Common共享预制中统一修为RGB180说明、同号内容和.06细白描边；实拍核心灰180、按钮255。当前MailPrefab8/8与邮件Play7/7通过，Native44尚待集成验证，不计入其他组件的测试数量。失败的117灰候选保留，不能把预先linear转换写作通用规则。[本轮现状](../../../../docs/delivery/2026-11-28-mail-ui-followup.md)

## 邮件UI修订（11/28内容43交付）

42实际原生14状态行为通过，但按钮描边因编译shader变体缺失未过视觉终验，失败包/图保留。产品源824d49ee的内容43显式收集SVC，实际DX11 vertex/fragment均保留四种OUTLINE组合，Native1080/4K各14状态及关键帧实际复核通过；数字字体补初始化特殊`_`字形，沿用原font/atlas/material GUID且警告消失，角标仍只显示数字。Editor43/43、Play7/7；旧41/42入口普通Limited实际启动43通过。33母包文件不变，暖重开新增Yoo业务payload0；详情见本轮回执。人工试听、Native排序2秒完整视听、书签国服逐字/单独Native近照及权威恢复计时未完成，不称全部原效果还原。

提示复用嵌套Common/GameToast及GameToastBinder，2秒关闭、替换重新计时、页面取消立即隐藏。排序只播放一次普通声；领取拒绝复用UiBlocked（原片93.042/94.258波形匹配），关闭仍沿OneShots淡出。确认复用Common/SourceConfirmation完整预制与源圆角按钮，保存后重载核对四处中文及UiAccept/UiCancel，禁止用装备直边样式替代。顶栏角标按top_bar.csb固定锚点、九宫格和原Dinmed数字；零隐藏，一位31/两位38原设计宽，三位48是项目容量扩展。

附件使用原物品卡底板及方形物品底，卡内容固定480×375，GM礼物480×285；源图自带约31–35px阴影内缩，框体单独外扩留白，图标/文本不能盖框。定位跟随所点附件左侧，边缘安全约束只改变位置/整体缩放。显示原分类与描述，行动力持有量取摘要；未提供权威恢复计时不编造倒计时。书签说明来自原日文DB的项目翻译，未核实国服逐字文案；未知物品不再以名称×领取数量冒充描述。[来源、修前修后与验收](../../../../docs/delivery/2026-11-28-mail-ui-followup.md)。

## 内容41系统交付历史（UI最新为上述43）

产品739ec080复用R37/app0.1.27母包，实际IL2CPP/D3D11的1080p与4K各14状态、GM自发、独立HTTP生产者事件通知、领取真实资源、礼包一次回执两阶段、历史与原页重开通过；前后摘要完整保留。API修后受影响Edit20/20，后端645/645，普通启动助手通过。38/39线上复杂字段解析和40诊断摘要漏写失败均保留，不把旧候选当交付。最终入口/准确文件/原版对照与未完成内容见[本机交付](../../../../docs/delivery/2026-11-27-mail-system.md)。267ms粒子、业务倍率/绝对同步、聚合排序差异与试听仍列边界；下文Editor历史不代替最终原生。

## 职责、地址与接入

独立热更业务模块，仅引用Core/Common、TMP和UGUI。`MailWindow : UIBaseView` / `MailWindowBinder`为普通C#；MailWindow.prefab、MailEntry.prefab只挂BindingMap和基础控件，不挂业务组件。Foundation拥有登录scope和页面返回，Mail不反向引用Foundation/Home。

- `MailWindow.Address = mail/window`；`MailEntry.Address = mail/header-entry`。Entry绑定button/count/counter/audio，audio为原btn_mail的AudioSource，按钮FeedbackSound=Silent。
- `new MailApi(Uri endpoint, Func<CancellationToken,Task<string>> bearer)`；可注入HttpMessageHandler用于真实transport边界测试。协议使用后端camelCase契约，奖励规则唯一源为`Config/Mail/catalog.json`，客户端不决定奖励或重随机。
- `new MailSession(IMailApi)`由每次登录持有。公开Summary、Changed、Invalidated、Claimed、Uncertain及RefreshAsync/ObserveAsync/ListAsync/ClaimAsync/ClaimAllAsync/RetryPendingAsync。退出账号Dispose，不能跨登录重用。
- `new MailWindowBinder(MailWindow view, BindingScope scope, ILocaleService locale, MailSession session, Action close, Func<MailClaimReceipt,CancellationToken,Task> reconcile)`后调用InitializeAsync(token)。同一个缓存View可以每次新建Binder/scope；关闭不Dispose登录session。

## 收信与一致性

收件/历史按服务器结果展示。普通信直接领；GM先正文、点击收信才提交；打开正文/附件不减数。批领只提交确认时的deliveryIds快照，取消不POST；GM由服务端排除。奖励仅由权威回执显示，特殊gmGift同一次claim先封面、点击再道具，不第二次发奖。

页面取消只结束等待；已dispatch请求由登录token继续。IO/超时/5xx等不确定结果触发一次Uncertain，保留原requestId。Claimed包含完整affectedKinds供登录级库存/钱包校准，页面关闭后仍触发。明确4xx移除已知失败意图且不触发Uncertain。重新开页先RetryPendingAsync，不依赖旧邮件行还可见；账号退出清理全部意图。

mailbox.version、energy.revision和balances.revision分别单调合并；同邮箱版本可接受较高资源revision，新邮箱版本不能倒写旧余额。旧成功回执缺balances或revision为0时不覆盖已有新资源。首次摘要落后已知事件版本直接丢弃；serverUtc只向前更新。金币/天空石/行动力显示真实余额，未实现的PVP旗帜不造固定数字。

HTTP20秒、事件心跳失活40秒；事件重连补摘要。NDJSON在累积第4097字符时中断，不先缓存整条无换行消息。迟到列表/正文有版本及tab/read意图generation守卫，切tab取消旧Read；关闭取消网络等待、移除监听/行实例/动画订阅。所有自由标题/正文richText=false，长正文可滚动。固定文案集中MailLabels稳定key，未完成的实现状态不放玩家提示。

### 协议 JSON 与嵌套礼包（11/27 原生修复）

Native候选39实际返回HTTP200且预览基础字段有效，但`JsonUtility`解析后的`mail/totalRewards`均为null，证据见 `.artifacts/mail-system-review/native-mail/runs/diagnostic39/player.log`。同源Mono对真实后台能解析，却产生递归`MailReward.children`的Unity TypeTree深度告警；因此Mono通过和AOT成员兼容检查都不足以证明该Native序列化桥接成立。

MailApi与开发GM Mail API现统一沿用现有SummonApi已经Native验证的`JsonConvert.SerializeObject(object)` / `JsonConvert.DeserializeObject<T>(string)`，复用项目已安装及R37母包保留的Newtonsoft.Json预编译插件，没有新增库、版本、AOT或第三方改动。写请求、读普通响应/稳定错误码及事件行均使用同一路径；保持默认`TypeNameHandling.None`，不启用类型名构造或放宽库默认深度界。无效JSON包装为固定文案IOException，不携带正文片段。HTTP8Mi字符、20秒、NDJSON4096字符、40秒心跳、取消及`dispatched`时机均保持原语义，`[NonSerialized] dispatched`不写进协议。

服务器允许礼包递归到depth8，客户端保留原`MailReward.children`完整树及服务端另行flatten的rewards，没有用一层leaf模型截掉嵌套信息。`GmMailWireTests`用JsonConvert写入合法八层bundle→末层137书签的HTTP fixture，覆盖玩家list/read/claim及GM preview/receipt，逐层核对children、末层数量、复合余额及无Unity TypeTree告警。旧实现实际RED2/2为零告警断言遇到2/3条原生TypeTree告警，叶数据在该Mono环境仍保留，不能表述为Mono丢树；Native39字段全null是独立实际失败。日志和XML在 `.artifacts/mail-system-review/gm-tests/wire-depth-{red,green}.{log,xml}`；最终Native内容40由协调者构建并验收，Editor结果不能替代原生闭环。

## 原素材与生成

运行前初始化Tools/ProjectEnvironment.ps1。使用项目`.tools/character-assets/Scripts/python.exe Tools/Mail/ImportMailAssets.py`最小导入，D:/sevenepic只读；音频输入是本次原声审计导出的4条FMOD WAV，需保留`.artifacts/mail-reference/1122-audio/sources`。来源/原始哈希/输出哈希、尺寸、切片见`Res/mail-source.json`。当前为62张原PNG、2400×1080原WebP解码、2张原SCT粒子贴图解码、原标题24帧图集、单项蓝条64帧图集及4条原音；不含整目录导入。

- 邮件胶囊绿按钮cap(30,12,92,35)→Unity(30,14,30,12)，不同于装备直边。正文_box_popup cap(44,44,38,140)→(44,48,48,44)；行_box_r_list边21；正文_bar_24 cap(22,4,3,17)并使用原蓝色(42,70,116)。确认金/蓝按钮200×128原cap(100,64,1,1)，不能按152×61绿色按钮切片。
- GM蓝龙npc1010_s、送货员npc1004_s；紫金邮件礼物箱为实际查看后确认的item/sp_promotion_package.png，棕红sp_7th_giftbox不是该封箱。头像使用已有圆框，头像内缩避免盖框。
- 第一阶段插画准确源为story/bg/img_event_3000days_1.webp；原框img/_frame_s.png。保留完整原图，在UI的217×123参考viewport等比放大裁切，不拉扁、不以纯色代替。源图/框的确认不意味着服务端授予了插画解锁；本项目该状态为礼包presentation。
- 标题原源uieff_msgbox_title的SCSP/CFX曲线由现有Spine38离线采样成24帧30fps图集，1024×192单帧、4×6排列4096×1152。明确ImportOverride、RGBA32、sRGB=false、平台max4096；Unity测试实际重导后检查GPU尺寸/格式。运行时RawImage与既有UiTouchPulse材质，不增加Spine运行时依赖。单项原蓝条按item_get_panel.cfx / item_get_panel_eff.cfx实际指向的_1版Spine3.8 intro/loop及intro动画采样，保留SkeletonClipping；64帧30fps、768×192单帧、4×16排列3072×3072，显式RGBA32/max4096。所有采样时间与源链哈希由同一导入器生成到mail-source，不依赖手留的临时脚本。
- 原ui_reward_popup_pati_blue_copy的3个emitter与原zodiac_star1/pati_03贴图接入UGUI；参数由导入器生成，不手改Generated数据。随机种子及显示单位缩放为项目复现参数，不能称逐粒子与录像一致。源音沿已有UiAudioPlayback AudioClip入口，关窗0.20秒淡出，没有扩Common/AOT音效枚举。

`AstralOath.Editor.ArenaUiBuilder.GenerateMail`先生成Common的SingleItemReward、MailEntry，再MailWindow；协调者全Generate额外重建各页嵌套顶栏。新增meta由Unity生成并保留，不重建旧GUID。

### 通用单项奖励蓝条与源星粒子（11/28）

单项奖励现在嵌套 `Assets/Game/Common/Res/Prefab/SingleItemReward.prefab`，原外部诊断键 `singleReward/singleEffect/singleIcon/singleQuantity/singleKind/singleText` 保留；`singleEffect` 改为UGUI `Image`，不能继续直接写RawImage.uvRect。预制只引用Common用途资源和现有基础控件，普通 `SingleItemReward : UIBaseView` / `SingleItemRewardBinder` 留在已登记Mail热更程序集，Common没有反向业务引用或新增AOT组件。基座仍用原64帧30fps、768×192格、3072×3072图集及UiTouchPulse材质，显示1024×256；源图集字节原样移到Common/Res/SingleItemReward，原GUID `2541e29b5dbb58759159550eb674bb7f` 保留。Binder创建稳定000..063索引的临时Sprite矩形，不创建新Texture；关闭owner销毁这些Sprite。SourceAdditive.shader/.mat同步移到Common保留GUID与内容，礼包原粒子只改材质路径，不替换成单项粒子。

用 `Tools/Mail/ImportSingleItemRewardAssets.py` 最小重导，或全量Mail导入器自动调用它。编辑入口是只读原 `effect/item_get_panel_eff.cfx` / `effect/item_get_panel_pati1_2.particle`，专用导入器同时生成Common审计JSON和Mail `SingleItemRewardSource.Generated.cs`，禁止手改Generated。运行用既有Newtonsoft解析生成常量，不依赖JsonUtility反射复杂热更字段。来源、哈希、SCT解码、PMA像素依据、原字段与源到设计.68倍率均在 `Common/Res/SingleItemReward/source-manifest.json`。

本次实际查看原055截图及54–57秒连续36张12fps取帧：约54.417蓝闪、54.833–55.083星点密集、55.167–55.583散开并消退。源CFX在267ms后将粒子放到y35/z8；两个emitter的 `burningType=1` 是一次burst max50+30，不能把duration=-1误当成持续50/30每秒刷星。寿命为.7±.5，速度/角度/出生位置/大小/径向40±39和2500±2000、切向0±200、gravity等字段按原值使用。原五点 `colorBlends` 列顺序是time/alpha/red/green/blue，按每个粒子的归一寿命分段插值；不是礼包的近似颜色或RGBA猜测。128×128原zodiac_star1.sct解码后12356个半透明像素全部R=G=B=A，证明为中性白PMA；因此复用已有SourceAdditive，不再次乘纹理alpha或猜改shader。所有星点是UGUI Image，原方差由局部固定种子1122抽样，120Hz固定步进保证不同Canvas帧率不改变轨迹积分；不能把随机星的每个位置称为与录像逐点相同。

接入：`SingleItemReward.ForInstance(root)`，`new SingleItemRewardBinder(view, owner)`；required bindings为effect/Image、icon/Image、quantity/kind/text/GameText、particles/RectTransform（原80个Image池）。`Play(Sprite icon,string quantity,string kind,string text)`重置本次表现；`Update(float elapsed)`接Play后的累计unscaled秒，非delta；`Stop()`清场并隐藏；owner关闭Dispose解绑并释放临时Sprite。只读 `Duration` 为64/30，`ActiveParticleCount/IsPlaying`仅供表现与诊断。动画不发奖、不写网络、不自行播放另一份音效，根组合层仍按真实权威领取回执和原音入口调用。

专用四项Unity真实RED在 `.artifacts/home-social-visual-fix/reward/prefab-red.xml` / `prefab-red.log`：缺通用预制、源数据或普通类型，4/4失败且无编译错误，之后才实现。回归覆盖Common依赖/80Image、源delay/burst/曲线/加速度、图集GUID/64帧/外6键及delay前0→burst80→最大寿命后0、重播/Stop/owner清理。源取帧、PMA像素、修改前无粒子截图依据在同reward目录。当前新生成预制、联合GREEN、连续表现截图、Native及R37真实AOT兼容仍待协调者执行，不能由源码或PMA检查推为视觉通过；音频未改，实际听感未试听。

## 实际验证与证据边界

- 原片文件名日期2026-11-22，版本/原作发布日期/可见倍速未知。已实际查看原版收件、历史、正文、附件、批领确认/取消、两阶段礼包、单项提示、空箱及连续联系图。源封面与框调查为`.artifacts/mail-system-review/cover-reference/located/README.md`；紫金箱候选与精确源亦已实际查看。
- 独立.NET10同源Session行为现8/8通过，保留每次真实RED→GREEN：旧GET防回退、同request重试、关页保留回执、Claimed、energy/balances独立revision、Uncertain+脱离旧行重试、首次落后事件摘要NRE。命令`.tools/dotnet/dotnet.exe run --project .artifacts/mail-system-review/session-tests/MailSessionTests.csproj`，各阶段txt在同目录。
- Unity预制最初2/2实际RED（缺真实预制）后GREEN。最后联合Edit15/15实际通过，含真实HttpMessageHandler丢回执/4xx/NDJSON与实际GPU导入断言；该次回执`.artifacts/mail-system-review/tests/client-Edit.xml`。该次包含首次旧摘要回归。最后布局/数量适配另有下述实际图形验证；Root并行集成与Native仍需最终联合验收。
- 受控图形完整流程已实际通过一次：5封→批领取消→5→2 GM→仅GM提示→正文/附件保留下层→同次封面/2-5-5→1→行动力→0→历史GM再看不写入。1080p、4K、4:3截图已经生成并实际查看；属于客户端受控权威响应，不能代替真实数据库或Native。迟到Read在Unity有效RED后修复，最后Play2/2已绿（`tests/client-Play.xml`）；奖励数量曾因小框省略，已新增实际字形可见断言并真RED1/2→GREEN2/2，回执`tests/reward-quantity-red.xml`。单项蓝条另录70连续帧与实测Time.unscaledTime，已实际看联系图，静默视频已编码/完整解码：after/single-bluebar-editor-silent.mp4、single-bluebar-times.csv；不是Native录像，不假定采样间隔为30fps。
- 可打开原版/修改前基线/修改后图录为`.artifacts/mail-system-review/client-comparison.html`；修改前没有邮件页面。截图after目录会随最终联合回归更新，签名包/原生/分发状态由协调者交付文档给出，不能从旧截图外推。

## 仍有差异与待核

排序展开、纯文本/选一、即时满箱/满背包、开信时到期缺同版参考；排序当前保持接收时间新→旧，只提示该事实。特殊礼包封面身份已定位，奖励语义不由文件名推定。淡入曲线约0.20/0.16秒及行框运行时色调是复现参数，未证明原Tween精确曲线。

单项蓝条的CFX调用链与骨骼原帧保留，267ms附带星粒子按上节补回；源到设计.68来自既有烘帧.51×显示1024/768，原版业务的最终倍率及与录像绝对逐帧同步仍须新视听证据确认，不把源字段实现等同完整原效果。原粒子细节、所有状态原生1080p/4K、两账号实时通知与旧token、实际声音听感仍由协调者最终联合验收；本代理未实际试听，不把PCM/波形检查冒充听感。兼容检查、热更登记、签名与分发由协调者负责。源码/资源/README/变更日志须同批提交。

11/27交接前最后修正：to_covenant改用既有Summon余额同源的item/token_ticket_rare.png，修正误用绿色友情书签；只改展示图索引，不改奖励数量或规则。受控after旧图里的绿色书签是该纠正前快照，最终分发版以协调者重拍Native图为准。

`MailEntryBinder.Bind`提供顶栏入口的页面作用域监听，业务Binder命名与MailEntry预制一致；缓存入口隐藏不解绑，页面结束才精确移除自己的监听。Foundation只注入开窗动作，不再持有另一份入口业务绑定。

### 列表响应字符上限（11/27边界回归）

HTTP普通响应以`StreamReader`解码后的JSON字符数限为8×1024×1024，仍在追加下一块前拒绝超限；事件流独立保持4096字符行界。收件/历史最多100封，当前接口返回完整正文，单封正文可达8000个UTF-16代码单元。ASP.NET默认编码虽保留普通中文，仍会把合法的U+3000全角空格和U+2028分隔符写为六字符转义；原2Mi字符界会拒绝合法长正文页。当前18种资源/2礼包的保守完整页上界实测6,695,066字符，8Mi界覆盖它；未来扩充嵌套礼包或目录需重验，不能无界增加响应。

`MailResponseBoundsTests`在真实Unity JsonUtility和同源MailApi上实际1失败+1通过→2/2通过、0跳过：100封各3000全角空格+5000中文能完整读回；超过8Mi的响应仍提前拒绝。回执`.artifacts/mail-system-review/tests/response-bounds-{red,green}.xml`，调查与纯.NET真实Host DTO/Freeze/Results.Ok测量见`.artifacts/mail-system-review/response-bounds.md`。该工具未启动监听或访问数据库，不替代Native/数据库联调。
