# 候选 UI 手工核对入口

日期：2026-11-10。基准：1024×768，沿用当前窗口及显示设置。本轮按用户要求取消截图采集、像素对比；最终外观由用户在游戏内核对。

## 直接使用

1. 关闭此前运行的项目客户端，双击 `E:\workspace\Debug\Client\Start-OfficialUiPreview.cmd`。
2. 正常登录自己的项目服务器并进入游戏，查看角色、包裹、快捷栏、血条、聊天、小地图和主界面按钮。已有角色、包裹截图无需重拍。
3. 需要旧界面时，关闭客户端，双击同目录 `Start-LegacyUi.cmd`。界面选择在启动时确定，需重启切换。

两个入口均使用该目录已有的资源、账号连接配置和显示设置。启动参数不写入 `GameUiStyle`；客户端原有的正常配置保存行为保留。直接运行 `Zircon.exe` 仍走原有配置和发布门禁，当前会选择 Legacy。

正式游戏安装目录 `E:\Program Files\ShengquGames\Mir3` 保持只读。入口启动的是本项目客户端。NPC 交互卡片、二级/服务/确认弹窗、显示设置、登录与选角页面均保留原样；NPC 商店主窗口及独立仓库沿用此前确定的范围。

## 实现及故障行为

- `Program.Main` 接收启动参数，并通过 `PrepareForLaunch` 统一选择本次会话。
- `--game-ui-preview` 使用 OfficialV1，仍执行完整资源 SHA256、目录契约、解码与 GPU 上传检查。共94帧，包为 `Data\GameUI\OfficialV1.Zl`。
- `--game-ui-legacy` 强制 Legacy；两个参数同时出现时回退 Legacy。仅精确参数有效，大小写不敏感；`--game-ui-preview=true` 不启用预览。
- 预览资源缺失或损坏时写入原有错误日志，并提示预览不可用，随后使用旧界面。资源所有权和退出释放保持原生命周期。
- 新增 `Prepare-GameUiPreview.ps1`：检查实际编译客户端的资源契约，验证包后复制并生成两个入口；不同内容的现有包/入口拒绝覆盖；`-ValidateOnly` 仅检查；不启动游戏、不编辑 INI。
- 编译门禁仍为 `RuntimeReady=false`，`productionBindingAllowed=false`、`releaseAccepted=false`。显式预览不代表发布验收。

## 本轮验证

| 项目 | 结果 |
| --- | --- |
| Debug / Release | 均构建成功，各有匹配的构建清单 |
| 启动选择 | 368项检查；3次预览 GPU 生命周期，每次上传全部94帧 |
| 资源与入口准备 | 最终版本15项检查；覆盖路径含空格和符号、损坏包、冲突保留、幂等、源目标相同、INI不变、正式安装目录拒绝 |
| 完整离线场景 | 4,297项检查；27次真实场景构造、47帧呈现、9次显式设备重置；覆盖六档分辨率、四个角色分页、场景重入、原生格子/聊天输入保留 |
| 原生回包 | 合成回环验证登录/退出/断线和角色属性刷新；角色34个检查点、52个回环报文、1,156个新旧字段对照 |
| 截图策略 | `-NoScreenshots`；本轮无截图保存和像素比较，保留实际渲染及交互检查 |

离线场景使用真实客户端代码、原生资源以及合成账号/地图/回环报文；不能据此认定真实服务器交易、实际输入法及视觉验收完成。本轮无需用户补截图，也不承诺绝对无缺陷。

Debug 目录已有原生资源和 INI，是本次手工核对入口。Release 已构建并放入候选包及启动器，但该目录未配置原生 Data/Map/Sound 和 INI，不作为本次可直接登录的交付入口。

任务状态保持7完成、3进行中、18未开始，G0-2/3/4/5仍开放。这里只记录本轮预览入口与验证结果，不将整份实施计划标为验收完成。

## 证据与复用

- `verification.json`：本轮源码变更、成功命令、资源和证据哈希。
- `debug-build.json` / `release-build.json`：当前二进制与编译资源契约。
- `tested-runtime.json`：所有本轮套件实际运行的 Debug 二进制哈希。
- `source-before/` 与 `source-before.json`：本轮六个已有文件的变更前快照。
- 外部证据在 `E:/workspace/mir3-ui-work/manual-preview-20261110-*`；完整场景记录包含截图关闭策略。

后续重新构建后，可运行：

```powershell
pwsh -NoProfile -File build/game-ui/Prepare-GameUiPreview.ps1 -ClientDirectory E:/workspace/Debug/Client -PackagePath E:/workspace/mir3-ui-work/warehouse-combined-20261108-v2/zl/OfficialV1.Zl
```

完整场景无截图检查入口：

```powershell
pwsh -NoProfile -File tests/Run-GameUiFullSceneTests.ps1 -PackagePath E:/workspace/mir3-ui-work/warehouse-combined-20261108-v2/zl/OfficialV1.Zl -EvidenceDirectory <新的证据目录> -NoScreenshots
```
