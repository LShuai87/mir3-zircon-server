# 完整场景离线验证

配置基准为用户确认的 **1024×768、窗口模式**。复用既有正式版截图，不修改显示设置、不要求重复采图。

本轮新增正常 `GameScene` 构造与实际 D3D Reset 集成验证。使用临时工作目录、真实旧版 Zl 库和合成角色/空白地图，未连接游戏服务器。原有 144 个已登记源码文件的哈希全部保持不变；本轮仅新增测试入口及证据。

## 已通过

- 3 次正常场景构造：Legacy 一次、同一 Official 启动会话下的场景进入与再次进入各一次。未使用未初始化的 GameScene 或 UserObject 替身。
- 17 次通过客户端真实 RenderGame 路径呈现画面；额外渲染并保存 16 张不同文件名的 GPU 截图。每次真实呈现均检查 FPSCounter 增长、DeviceLost 未置位、会话未要求重启，避免被渲染层捕获的异常造成假通过。
- 四个角色页均绘制：角色、扩展装备、状态、修炼。角色和包裹同时显示。
- 实际调用 DXManager.ResetDevice 7 次，并检查恢复后的后备缓冲尺寸。覆盖 1024×768、1366×768、1280×800、1440×900、1600×900、1920×1080，最后回到 1024×768。
- 每次 Reset/调整尺寸均核对原有 16 装备槽、49 包裹格、10 快捷格及链接对象身份；聊天页签/历史和原生文本框 HWND、中文草稿、选区不变。
- 两次 1024×768 Reset 后分别对比角色与包裹完整矩形。累计 690,512 个像素完全一致，并要求矩形包含有效非黑色像素，排除空画面假通过。坐标及每次像素数见 [reset-pixel-probes.txt](reset-pixel-probes.txt)。
- NPC 主对话框和设置框未接入新皮肤；场景退出后聊天页签清空、游戏场景解除注册，关闭启动会话后包文件独占打开成功。

执行结果为 **180 项检查通过**。详见 [测试日志](full-scene-tests.log)、[结构化报告](verification.json)。

## 复现

先构建 Client Debug，再从仓库根目录执行：

```powershell
./tests/Run-GameUiFullSceneTests.ps1 -PackagePath 'E:/workspace/mir3-ui-work/hud-progress-zl-20261108-v1/OfficialV1.Zl' -EvidenceDirectory 'E:/workspace/mir3-ui-work/full-scene-new-run'
```

EvidenceDirectory 必须是尚不存在的目录。可用 LegacyDataDirectory 指定真实旧版 Data 目录；默认读取相邻 Debug/Client/Data。本次读取的 10 个资源库及 SHA256 见 [legacy-resources.txt](legacy-resources.txt)，被测程序集见 [tested-runtime.json](tested-runtime.json)。所有游戏数据库、空白地图、拾取配置均位于测试创建的临时目录；测试退出后只清理该生成目录。

## 图像证据与边界

[1024×768 主界面](captures/official-hud.png) · [角色与包裹](captures/official-panels.png) · [扩展装备](captures/official-extraEquipmentTab.png) · [状态](captures/official-StatsTab.png) · [修炼](captures/official-HermitTab.png) · [再次进入场景](captures/official-reentry-panels.png)

这些是客户端的 **离线 GPU 渲染图**。黑色世界背景来自合成空白地图，不是正式服场景；角色为合成角色，装备与包裹为空。不得用作用户已确认最终视觉效果的证据。

独立测试 EXE 不等同于客户端 Program：MirDB 只扫描入口、执行及调用程序集，因此测试显式注册 Client 中的空用户集合，并准备隐藏宠物窗口依赖的一条系统记录。未验证 CEnvir.LoadDatabase 的正式入口和已有数据库迁移。场景构造、UserObject、入场数据赋值、控件、Process、RenderGame、Device.Reset、释放路径使用真实客户端实现。

此次覆盖的是主动调用真实 Device.Reset；未模拟拔卡、硬件设备丢失或远程桌面切换。原生输入框句柄/草稿/选区检查不替代实机 IME 候选窗验收；聊天测试消息由原有 ReceiveChat 在本地加入，未验证服务器发送。未验证有装备/道具时的拖放、交易、技能及服务器业务。小地图仅验证外框，未加载真实地图缩略图。

Legacy 仍为默认，RuntimeReady=false，五个 G0 门禁保持未关闭。任务状态保持 6 done / 1 in_progress / 21 not_started，UI-12 仍不能据此验收。本报告是对历史 [场景启动验证](../scene-startup/README.md) 的补充，不修改历史结论；下一步仍需真实登录/重登、装备与按钮行为、实机 IME 及最终画面核对。NPC 交互卡片、二级弹窗、显示设置、登录及选角界面保持既定排除范围。
