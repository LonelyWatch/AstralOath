# 固定防守竞技场

在当前区服按玩家永久保存首通。关卡、防守顺序、等级、装备加值、首通征服点及按通关数解锁的段位均来自 [竞技场表](../../../Config/ArenaTower/catalog.json)。守方没有加值时空装，有加值时以单件虚拟装备应用表中明确配置的攻、防、血、速，沿用共享装备计算，无额外套装加成。玩家队伍读取自己的英雄实例、实际等级与星级、装备和神器。

## HTTP

所有调用使用已有游戏会话 Bearer。请求超时 15 秒，禁用缓存；查找战斗、历史和进度限定当前玩家和区服。

- `GET /api/arena/tower` 返回 `highestCleared/currentStage/rankId` 和顺序排列的 `stages`。全部通关后定位最后一关，历史关均可重打。
- `POST /api/arena/tower/stages/{stage}/challenge` 接收无连字符的 UUID `requestId` 与一至四个按出战顺序排列的 `heroInstanceIds`。仅可打已过关卡及下一关；跨关 409。相同请求重试返回同场，改变参数 409。返回试战形状的邀请，`roomId` 与 `opening.snapshot.battleId` 相同，`allyToken/enemyToken` 为空，玩家沿用自己会话进入手动战斗。未使用开发预览入口。
- `GET /api/arena/tower/battles/{id}?after=…` 和 `POST /api/arena/tower/battles/{id}/commands` 共用在线战斗的快照、游标和技能指令。服务端只接收己方合法行动，敌方使用既有技能 AI。终局根据引擎判定，接口不接收客户端胜负或领币申报。
- `GET /api/arena/tower/stages/{stage}/clear-team` 返回第一次通关时已经冻结的 `heroes[{slot,instanceId,heroId,level,stars}]` 与敌方 `defenderHeroIds`。未通关返回 404。

## 存储与战斗恢复

迁移 `023_arena_tower.sql` 建立尝试、指令、首通和钱包表。首次开始、终局与首次奖励分别记录权威审计。不同请求可以重打，首通主键 `player_id/realm_id/stage` 锁定第一次胜利，重复挑战、并发胜利和重复提交指令都不会再次发放征服点。商店读取并扣减 `arena_tower_wallet` 中的 `conquest_point`。

战斗种子、敌我快照、规则版本和逐步合法输入持久化。服务器重启后可按相同规则重新构建战斗，旧关卡表覆盖不会改变已开场对局的队伍和奖励。战斗规则不一致时返回冲突。战斗读取按已提交版本缓存；未提交的命令不会改动共享缓存。第三个侧栏按钮预留给对战记录；玩家可观看历史回放的页面和接口等后续参考确定后再接入。

服务端用例位于 [ArenaTowerTests.cs](../../Tests/ArenaTowerTests.cs)。新手强度样本使用拉斯、梅赛德丝和安洁莉卡的初始等级和星级，全部空装，自动选择合法技能；对前五关各运行三个固定种子。另逐关构造全部 100 个防守阵容并检查角色人数。MySQL 集成用例覆盖重启、越关、账号隔离、并发胜利只发一次首通奖励和重打不替换首通阵容。
