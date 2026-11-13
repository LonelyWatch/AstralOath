# 战斗音频原生回归

这个独立 .NET 10 探针直接编译生产 `BattleVoice`、`FmodCharacterAudioService` 和项目固定版本 FMOD 包装器。仅用小型适配器提供 Unity `TextAsset` / JSON / 日志，不启动 Unity，也不替换音频服务。所有事件播放前切换为 FMOD `NOSOUND` 输出，通过主 DSP 的采样峰值确认真正产声。

在仓库根目录运行：

```powershell
& ./.tools/character-assets/Scripts/python.exe Tools/BattleScene/ImportBattleAudio.py --check
& ./.tools/dotnet/dotnet.exe run --project Tools/BattleScene/AudioProbe -- $PWD.Path 'D:/sevenepic/原始资源/sound'
```

验证内容：

- `preview.slime.s1` 启动原版 `event:/model/slime/ani/skill1` 并产生采样。
- 保留首批三名英雄各自的原版 `ani/attacked` 事件，不同时启动所有受击随机变体；输出持续超过事件描述的 20 毫秒，确认短事件没有截断语音。
- 独立测试史莱姆 STG 对应 `hit_new_blunt_normal`，防止目标语音掩盖命中特效无声。
- 未知单位和技能不会套用其他角色声音；释放战斗后不能重启音频。

`ImportBattleAudio.py` 只复制 `sound/gerneal.bank` 并生成本模块来源清单。`--check` 比较原始与导入字节、STG 命中类型、原始证据哈希及导入器哈希，不改只读来源。`gerneal.bank` 约 14.1 MiB，位于 CharacterPresentation 共用声音库并沿用 `*.bank.bytes` Git LFS 规则，原 meta GUID 保留。

这个探针证明 Windows 原生事件和采样路径；场景的 Damage 触发时机、序列化 bank 引用、实际扬声器听感与 Android 插件不在该探针的验收范围。Unity 场景回归由项目协调者统一执行。

十角色回归同时加载六份共用 bank，播放全部 30 个技能的音效与随机日语语音事件，并用原生回调计数和主 DSP 峰值检查产声。源目录明确缺少的日语片段应记录一次诊断、保持音频服务可用，并继续其他片段。探针记录 `.artifacts/ten-hero-audioprobe-final.log`。

将待重复播放的技能演出 ID 追加到命令行，可通过生产 `BattleVoice` 逐次播放并断言日语样本变化、夹攻呼叫/应答和无专用事件时的回退。例如：

```powershell
& ./.tools/dotnet/dotnet.exe run --project Tools/BattleScene/AudioProbe -- $PWD.Path 'D:/sevenepic/原始资源/sound' c_ras_skill_01 c_mercedes_skill_01 c_kise_skill_01 c_ras_skill_03
```

附属录音命令 `--record-button <绝对 WAV 路径>` 只加载原 master/master.strings/ui1，校验 `event:/ui/btn_normal` 的 GUID，再用 FMOD `WAVWRITER` 写出完整事件。Unity 通用按钮使用剪去首尾多余静音的独立 PCM 文件。
