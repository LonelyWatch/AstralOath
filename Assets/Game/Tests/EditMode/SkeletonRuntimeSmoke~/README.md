# 官方内核真实数据 smoke

先用角色资源转换器产生六个 `.spine.json` 验证文件。参数一为含 `model/`、`portrait/` 的转换输出目录；参数二为只读原始资源目录，其中各自有同名 `.atlas`。只读这些输入，不生成或修改原始文件。

```powershell
& ./.tools/dotnet/dotnet.exe run --project Assets/Game/Tests/EditMode/SkeletonRuntimeSmoke~/SkeletonRuntimeSmoke.csproj -- .artifacts/character-import/validation D:/sevenepic/原始资源
```

使用仓库固定的双版本官方源码，netstandard2.1 隔离 vendor，以 net10.0 控制台遍历每个 skin/animation 的 30fps 采样，应用 FFD、drawOrder、事件并生成所有可见 attachment 世界顶点；任一解析错误或非有限顶点使进程退出失败。输出 JSON 行记录覆盖数量。此项验证数据与算法兼容，不调用 Unity、不验证最终纹理像素或 GPU 渲染。
