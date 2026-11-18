# 召唤

主页「更多 → 召唤」打开 SummonWindow。SummonWindow 按 BindingMap 读取预制体节点，SummonWindowBinder 管理卡池、愿望单、弹窗、回执和演出。需要重新生成预制体时，运行 Unity 菜单「Tools → 游戏主页 → 生成召唤和神器」。

服务端 /api/summon 返回当前账号及区服的池版本、实际概率、货币和免费次数。提交时使用回执 ID 幂等重试。在收到已结算回执后才播放演出，用户随时可以跳过。再次打开时可查看上次结果，溢出的奖励在保管箱内领取。开发账号首次进入本区服发送测试票券。

配表与服务端实现位于 Server/Host/Summon。原版界面、骨骼、音频的路径和哈希见 Res/Textures 与 Res/Presentation 的 source-manifest.json，核对方法见原版召唤演出交付记录。
