# Arena

模拟战目录、创建房间弹窗和房间页由独立 View/Binder 管理。目录用 ScrollRect 支持鼠标滚轮与触屏拖动。更新目录从服务端查询，可按公开状态、人数、最近创建和房名筛选。创建房名最多 28 字，支持四位数字密码、聊天开关、五个规则选项。规则目前随房间保存，房间页的开始战斗为禁用态。

房间用独立的 BindingScope 轮询成员和竞技场聊天，退出界面取消请求并释放列表和聊天实例。目录由玩家手动更新。房间密码仅提交到服务器，接口只返回是否设密。会话由服务端发放，客户端在同一进程内持有令牌。网络走与 Foundation 一致的 HTTPS 或本机回环 HTTP 地址。

`arena/directory`、`arena/create-room`、`arena/room` 和 `arena/zh-hans` 加入 Foundation 的 `HomeArena` 收集组。原版素材与截图对照见 `Res/Textures/source-manifest.json` 和[操作与验收](../../../../docs/development/arena-lobby.md)。

依据 2026-11-14 国服录屏优化。默认按人数降序，重复点击排序项切换方向，选择时保留筛选浮层。搜索需用户主动聚焦输入，提交后可继续修改，取消时解除过滤。创建页从疯狂征召开始展示。房间聊天按头像、发送者、时间、正文排版，跟随底部时自动续滚，阅读旧消息时保持锚点。

`WindowCoordinator` 管理目录、房间以及筛选、搜索、创建与密码弹窗。目录交互记录必要的客户端语义事件，创建、进出房和聊天走服务端事务留痕。参见[操作留痕](../../../../docs/development/operation-audit.md)与[录像对照交付](../../../../docs/delivery/2026-11-14-arena-recording.md)。

房间聊天的表情从输入栏打开三列表情浮层，可滚动。服务端与资源导入验证使用同一组 67 个 `emoticon_chat` 资源 ID。客户端显示原版静态聊天图，文本和表情共用消息游标及禁聊规则。源素材及界面对照见[本轮复核](../../../../docs/delivery/2026-11-14-arena-ui-fidelity.md)。
