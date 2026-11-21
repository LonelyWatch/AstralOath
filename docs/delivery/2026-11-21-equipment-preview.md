# 普通装备第一阶段 · Windows 本机功能验证

2026-11-21，候选 `85f4b1c3c044e0a13aea6efc19e516dfb3988e2d`。本次验收覆盖背包、英雄槽、穿脱回执、面板和试战数据。原版装备背包、穿戴比较和英雄装备槽的视觉效果仍待取得对应原版界面参考后逐项核对。

## 本机独立试玩

双击 `.artifacts/equipment-win64/Play-Equipment-QA.cmd` 可启动装备功能验证客户端，也可再次打开新窗口。入口先唤起或复用专用 MySQL、游戏服和签名内容源，再把隔离服地址传给原生客户端。同一台开发机使用此入口即可。测试账号可获七件固定训练装，资料存在专用数据库。客户端缓存写到 `D:/UnityProjects/AstralOath/.artifacts/equipment-win64/data`，产品名为 `AstralEQ27`。

- 装备游戏服监听 `127.0.0.2:7728`，QA 数据库端口为 `3307`，内容源为 `127.0.0.1:5298`。
- 常规客户端继续连接 `127.0.0.1:7728`，使用 `127.0.0.1:5295` 分发的 R26。验证前后比对签名清单和进程身份，常规 Host PID 44616、MySQL PID 44576 和服务管理 PID 37008 均一致。旧临时端口 57573 已结束监听。回执为 `.artifacts/equipment-win64/final-endpoints.json`。

## 构建、冷启动与重开

核对 23,307 个 Git/LFS 输入，从同一 0.1.23 Windows x64 母包 `25cda4dbafde4770b98611b1323689a1` 构建隔离内容 R27。母包内置公钥分别验证了构建输出和交付目录，七个热更程序集均通过原生归档构造函数兼容检查。签名清单 SHA-256 为 `e4c38acebf8ee029ce067cc07678bbce33a18f21a11729c4f80c4622cd2e0a82`，协议 1、结构 3。与 R26 比较，清单中 98 个文件可复用 90 个，差量 8 个文件共 32,616,053 字节。证据为 `.artifacts/equipment-win64/build/receipt.json` 与同目录 `native-archive-compatibility.txt`。

1. 从隔离的 R26 缓存启动 D3D11 图形 Player，实际激活 R27。打开背包并确认七件训练装，按头盔筛选，随后从英雄页打开同一件装备，点确认穿到拉斯身上。界面收到服务端成功回执后槽内出现物品图，生命从 **263 升至 343**。再次确认卸下，物品图清空，生命回到 **263**。原生日志和退出码见 `.artifacts/equipment-player/fa8d142fee6443ad8f3fadb0bd4ce82a/verified.json`。
2. 确认三个隔离服务均已停止，常规 R26 仍在线。从 `Play-Equipment-QA.cmd --verify` 一次启动数据库、游戏服、签名源和新的图形 Player，再次从首页进入背包、英雄，完成相同的穿脱和生命值核验。记录为 `.artifacts/equipment-win64/cold-before.json` 与 `.artifacts/equipment-player/11a9ee8935334527b9c0a83e1bc13a27/verified.json`。
3. 无参数运行同一启动器，复用已就绪的独立服务并实际启动常规 Windows Player，观察到 D3D11 初始化。该次只核对了普通启动分支。穿装脚本使用专用训练访客，未手动点击常规登录页。记录为 `.artifacts/equipment-win64/normal-launch.json`。

## 前台切换与试战边界

Unity PlayMode 已对回前台的刷新方法执行两个分支，原版本仍有效时只做版本校验，服务器版本改变后取回新库存。最终 PlayMode 回执为 `.artifacts/equipment-ui/gear-85f4b1c3-playmode.xml`。Windows 实机当时已有其他窗口占用前台，未实际切换操作系统焦点。检测记录为 `.artifacts/equipment-win64/focus-d6844c3f/skipped.json`。

在真实 MySQL 8.4 中给两名同模板英雄按实例分别穿装，持有试战按所选顺序取各自面板。第二客户端在开战后转移武器，原场次续接仍读原面板，新场次使用新面板。现有神器继续在装备百分比之后单独加攻、加生命。装备、神器、房间和单位相关的专项回归共 21 项通过。证据为 `.artifacts/equipment-battle/trial-final.log`。规则与视觉参考范围见[装备实施方案](../plans/2026-11-21-equipment-system.md)。
