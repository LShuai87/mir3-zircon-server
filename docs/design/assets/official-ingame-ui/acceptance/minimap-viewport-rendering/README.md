# 小地图不同尺寸绘制验收

日期：2026-11-11。

普通视口 150×152、放大视口 300×302 使用相同定位规则：地图素材在较小或刚好适配的轴上居中，在较大的轴上按边界限位。自动跟随以玩家标记中心定位，拖动使用相同的限位计算；地图图片与所有原生标记保持原像素比例。放大按钮扩大可见区域。

初次绑定、切图、真正改变视口尺寸或展开时重新定位。重复应用相同布局、换肤及窗口位置变化保留手动拖动位置。无玩家标记时整图居中。空地图、缺失帧、空帧、零尺寸素材、缺失库和无效地图宽高会隐藏旧图并清零投影比例；有效地图恢复时重新生成标记。

`preview.png` 汇总原生 Direct3D 绘制结果，使用合成颜色隔离地图、标记、背景和裁剪。红色为框体，绿色为地图，青色为玩家，蓝色为场景背景；图片不代表正式地图美术。`verified-render/` 同时包含正式框体与底部按钮截图、运行文件及素材包哈希。

- Debug / Release 构建通过。
- 小地图共 3,352,592 次检查通过，其中 CPU 1,424 次；新增矩阵 49 帧，2,918,520 次像素比较。
- 矩阵覆盖 300×260、40×32、400×32、40×400、150×152、300×302 六种素材，普通/放大两种视口，中心/边缘跟随、拖动与半透明、收起、清空。
- 窗口集成 562,065 次检查通过，含六种分辨率与放大/收起状态切换。
- 原有标题、底栏、框体、透明度与裁剪检查继续通过。
- 路由绑定静态契约通过。全量布局检查和联表生成仍被既有 `commands.CharacterButton` / `commands.BeltButton` 重叠校验阻塞。

复验命令：

```powershell
./tests/Run-GameUiMiniMapLayoutTests.ps1 -RenderPath E:/workspace/mir3-ui-work/<fresh-directory> -PackagePath E:/workspace/Debug/Client/Data/GameUI/OfficialV1.Zl
./tests/Run-GameUiWindowSetTests.ps1 -PackagePath E:/workspace/Debug/Client/Data/GameUI/OfficialV1.Zl
```

未登录真实服务器，未加载真实游戏地图。透明度沿用现有逐层混合。细节与哈希见 `verification.json`。
