# Arena

模拟战目录、创建房间弹窗和房间页由独立 View/Binder 管理。目录用 ScrollRect 支持鼠标滚轮与触屏拖动。更新目录从服务端查询，可按公开状态、人数、最近创建和房名筛选。创建房名最多 28 字，支持四位数字密码、聊天开关、五个规则选项。规则目前随房间保存，房间页的开始战斗为禁用态。

房间用独立的 BindingScope 轮询成员和竞技场聊天，退出界面取消请求并释放列表和聊天实例。目录由玩家手动更新。房间密码仅提交到服务器，接口只返回是否设密。会话由服务端发放，客户端在同一进程内持有令牌。网络走与 Foundation 一致的 HTTPS 或本机回环 HTTP 地址。

`arena/directory`、`arena/create-room`、`arena/room` 和 `arena/zh-hans` 加入 Foundation 的 `HomeArena` 收集组。原版素材与截图对照见 `Res/Textures/source-manifest.json` 和[操作与验收](../../../../docs/development/arena-lobby.md)。
