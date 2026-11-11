# 聊天框还原与频道拒绝按钮（2026-11-10）

OfficialV1 聊天区恢复不透明黑底和原版铜色边框，移除可见 Chat 1 页签及其 24px 占位，消息直接从顶部开始。保留原生聊天页、消息历史、输入框 HWND、草稿和频道设置。Legacy 布局保持原来的页签高度；解除 OfficialV1 绑定会恢复原控件样式。

左侧从上到下为拒绝公聊信息、拒绝私聊信息、拒绝喊话信息、拒绝组队聊天信息、拒绝行会聊天信息。接收时为铜色原图，拒绝时为灰色原图；拒绝状态仍可再次点击恢复接收。按钮直接切换所选原生聊天页的过滤复选框，设置面板修改也会同步按钮。保存、重建与场景重入复用原有聊天设置存储。

## 素材来源

已补入 `Client/Resources/GameUi/chat-artwork.png`，通过程序集内嵌资源加载，不依赖额外部署贴图库。对应 JSON 保存每张源图片的 SHA256 和图集位置。生成脚本为 `build/game-ui/Build-ChatArtwork.py`，输入本地严格解码结果 `E:/workspace/mir3-ui-work/runtime-reference-20261107/Inspection`。

| 用途 | 原始 UI 索引 |
| --- | --- |
| 公聊按钮四态 | 210–213 |
| 私聊按钮四态 | 220–223 |
| 喊话按钮四态 | 200–203 |
| 组队按钮四态 | 240–243 |
| 行会按钮四态 | 230–233 |
| 滚动向上 / 向下 / 滑块四态 | 70–73 / 74–77 / 50–53 |
| 铜色边框九宫格 | 115 |

未在已解码清单中找到完整的聊天框背景图。黑底由客户端直接绘制；边框、按钮与滚动控件使用真实素材。显式开启原有“透明”设置时仍按原功能隐藏历史框背景。

## 验证

- Client Debug 编译成功。
- 聊天布局测试：940 项通过，覆盖六种分辨率、历史、滚动锚点和原生输入控件保留。
- 聊天专项集成测试（WindowSet runner 的 `-ChatOnly`）：1,120 项通过，覆盖五频道过滤、真实鼠标路径命中、右键不切换、重复点击、设置联动、无页签布局、实际 GPU 像素、原生拖出/合并、回滚、配置保存和重建。对洋红底执行黑底像素断言，两个状态截图验证每个按钮渲染确实不同。
- 全场景离线集成：4,391 项通过，27 个真实构造函数、48 帧、9 次显式 Device.Reset、690,512 个一致面板像素；真实本地素材、合成角色/地图、本地回环协议。未进行真实账号联网、硬件设备丢失或实时中文输入法验收。
- 整套窗口测试仍在命令轮命中断言处失败：`Command hit changed at {X=198,Y=174}; expected original visible control=False`。未建立该断言的历史基线，不能断言属于既有问题。该套件没有全量通过；聊天专项和全场景测试独立执行通过。原始错误见 `window-suite-failure.log`。

重现命令（PowerShell，在仓库根目录执行）：

```powershell
& tests/Run-GameUiChatLayoutTests.ps1 -ClientDirectory E:/workspace/mir3-ui-work/chat-panel-20261110/runtime
& tests/Run-GameUiWindowSetTests.ps1 -PackagePath E:/workspace/Debug/Client/Data/GameUI/OfficialV1.Zl -ClientDirectory E:/workspace/mir3-ui-work/chat-panel-20261110/runtime -ChatOnly
& tests/Run-GameUiFullSceneTests.ps1 -PackagePath E:/workspace/Debug/Client/Data/GameUI/OfficialV1.Zl -ClientDirectory E:/workspace/mir3-ui-work/chat-panel-20261110/runtime -LegacyDataDirectory E:/workspace/Debug/Client/Data -EvidenceDirectory <新的证据目录>
```

`chat-preview.png` 为真实离线场景聊天区裁图（放大两倍），`official-hud.png` 为完整场景；`chat-states.png` 上半为接收，下半为全部拒绝，洋红色为像素验证的清屏颜色。专项截图中的传统输入按钮使用测试包代替原始 InterfaceLibrary，仅用于隔离验证，实际样式以全场景截图为准。

## 本地调试客户端

确认 Zircon 进程未运行后，已备份并更新 `E:/workspace/Debug/Client/Zircon.exe` 和对应 PDB。Library.dll 与测试版哈希一致，无需替换。部署路径、备份路径及 SHA256 见 `deployment.json`；两组 `tested-runtime.json` 中 Zircon.exe 哈希均与部署一致。未修改 OfficialV1.Zl。
