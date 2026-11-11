# 正式版 UI 参考资源转存

## 当前启动方式（2026-11-11）

客户端普通启动默认使用 `OfficialV1`。无配置文件或 `[Graphics]` 下缺少 `GameUiStyle` 时使用此默认值；已有的 `GameUiStyle=Legacy` 仍明确选择旧 UI，切换需改为 `OfficialV1`。

- 普通启动：运行客户端目录中的 `Zircon.exe`。
- 强制旧 UI：运行 `Start-LegacyUi.cmd`，或 `Zircon.exe --game-ui-legacy`；仅对当前进程生效，不改写配置。
- 强制新 UI 预览：`Start-OfficialUiPreview.cmd` / `--game-ui-preview` 继续可用，可覆盖已保存的 Legacy 配置。
- 新 UI 在登录场景之前校验资源包 SHA256、帧清单与 GPU 上传；资源缺失、损坏、版本不匹配或上传失败均回退 Legacy，诊断写入原错误日志，配置继续保留。

开发测试目录可通过 `Prepare-GameUiPreview.ps1 -ClientDirectory <目录> -PackagePath <包>` 暂存匹配包并生成新 UI 预览及旧 UI 启动入口。脚本不修改 INI；已有测试配置需将 `[Graphics] GameUiStyle` 改为 `OfficialV1`。普通启动策略与发布验收字段独立：编译目录的 `RuntimeReady=false` 及部署工具的发布检查保持，普通启动仍执行完整资源预检。本节描述当前入口，下文带日期的验收记录保留当时状态。


此工具只读取正式版 Data 目录，转存 UI、Interface、GameInter、ProgUse 四库。保留原索引、空槽、逻辑尺寸和有符号偏移。

依赖：Windows、Python 3.10+、Pillow，以及已编译的 ImageManager（含其 DLL 和配置）。先构建 ImageManager，然后在仓库根目录执行：

```powershell
& .\build\game-ui\Build-OfficialUi.ps1 -SourcePath 'E:\Program Files\ShengquGames\Mir3\Data' -OutputPath 'E:\workspace\mir3-ui-work\reference-build-v1'
```

可通过 `-PythonPath`、`-ImageManagerPath`、`-BaselinePath` 显式指定工具和基线。默认使用仓库相邻 Debug/ImageManager/ImageManager.exe。

输出目录必须不存在，且与源目录互不包含；不接受路径中的符号链接或目录联接。全部转换成功后原子发布，失败只清理本次暂存目录。默认按 source-libraries.json 验证八个 MIX/MIZ 文件的 SHA256；版本不同会停止，需重新审查并提供新的基线。

| 输出 | 用途 |
| --- | --- |
| Decoded/manifest.json 和分库 PNG | 无损 RGB565 解码、原索引和偏移元数据 |
| Package/Reference/UI.Zl 等四库 | 通过现有客户端 Zl 读取器浏览和选图 |
| Package/reference-manifest.json | 原文件哈希、Zl 哈希及索引数量 |
| build.json | 工具哈希、相对路径、两份 manifest 的哈希 |

参考 Zl 采用显式 opaque DXT1。黑色保留为不透明颜色；当前没有按素材语义验证透明区域，不能直接部署成正式 UI 包。DXT1 有损，不能将 Zl 像素等同于原 PNG。所有参考清单固定 `alphaVerified=false`，`-Mode Runtime` 会明确失败。

最终 OfficialV1.Zl 只应包含确认后的登录后 UI，并配套语义 role、透明/混合规则、六分辨率布局和编译目录；其构建属于 UI-05/06/07。参考转存不关闭这些验收门槛。

```powershell
.\tests\Run-GameUiTests.ps1 -Suite Assets,Controls,Layout -DecodedPath 'E:\workspace\mir3-ui-work\reference-build-v1\Decoded' -ReferencePath 'E:\workspace\mir3-ui-work\reference-build-v1\Package' -RenderPath 'E:\workspace\mir3-ui-work\acceptance\skin'
```

Controls/Layout 还需先构建 Debug Client。RenderPath 启用真实 SlimDX/D3D9 像素检查，需当前环境具备 D3D9 设备。Bindings 统一入口尚未实现，选择该套件会明确失败。Npc 入口已接入原交互卡片回归，使用下列命令；仅验证隔离样本，不代表真实服务器验收。

## 资源版本差异诊断

当新版本无法通过完整解码时，可对已固定哈希的快照运行诊断器：

```powershell
python .\build\game-ui\Inspect-OfficialUi.py --source 'E:\workspace\mir3-ui-work\runtime-reference-20261107\Data' --output 'E:\workspace\mir3-ui-work\runtime-reference-20261107\Inspection-new' --baseline '.\docs\design\assets\official-ingame-ui\acceptance\reference\runtime-source-libraries.json'
```

输出目录必须不存在。诊断器沿用严格解码器的源文件哈希、表结构、边界和像素预算校验；记录单张解码失败的原索引、头信息及错误。源哈希或表结构等整体校验失败时不发布输出。所有受支持图仍保持原索引。

诊断输出只有 `inventory.json`，明确标记 `buildable=false`、`alphaVerified=false`，不生成构建用 `manifest.json`。退出码 0 只表示诊断完成，必须检查 `rejectedCount`；不能据此认定全库转换成功。不得截断异常像素数据、生成占位图或将诊断输出直接接入运行包。

2026-11-07 的新快照诊断为 2,235 张成功、5 张拒绝；严格全库转换仍失败。详见[本批次结果](../../docs/design/assets/official-ingame-ui/acceptance/reference/runtime-inspection.json)。旧基线和已验证参考包继续保留，不能将两个版本的索引或测试证据混用。

## 按已核对角色选集转存

当前安装目录的全库含5张未支持记录；已审查的27张选图均可严格解码，且与既有截图匹配时的PNG哈希一致。可仅转存清单选中的记录，保留所属库的原索引与空槽：

```powershell
& .\build\game-ui\Build-OfficialUi.ps1 -SourcePath 'E:\Program Files\ShengquGames\Mir3\Data' -OutputPath 'E:\workspace\mir3-ui-work\selected-reference-new' -BaselinePath 'build/game-ui/selection-source-libraries.json' -SelectionPath 'build/game-ui/reference-selection.json'
```

选集清单固定源库、索引、PNG哈希、逻辑尺寸和有符号偏移；源文件仍整体验证哈希及索引表。未选记录只保留存在信息，不解码、不计入成功数；选中记录的任何不匹配均使本次构建失败。`selectedOnly=true`不代表全库已支持。

输出仅含选中的库，本批为`Reference/UI.Zl`（5674个索引槽、27张有效图）。客户端读取和GPU测试按包清单验证，声明的库缺失、哈希不符或纹理数不符都会失败；旧四库回归仍通过。两次实际输出的Zl和清单哈希一致，见[验证记录](../../docs/design/assets/official-ingame-ui/acceptance/preparatory/selected-assets/verification.json)。透明规则仍未通过验收，不能作为运行包部署。


## 选集拼接与接入测试 Zl

`Compose-OfficialUi.py` 使用 `composition-recipe.json`，核对选集与每张 PNG 的 SHA256、尺寸和偏移；只清除边缘相连的纯黑背景，内部纯黑不作全局色键。包裹重建为 7×7、快捷栏保留 10 格，单格 36×36、步长 35。按钮四态使用明确 tint，血球层共享原偏移基准。

```powershell
python build/game-ui/Compose-OfficialUi.py --decoded <已选参考Decoded目录> --output <全新拼接输出目录> --recipe build/game-ui/composition-recipe.json
ImageManager.exe --official-ui prepared <拼接输出目录> <全新Zl输出目录> <拼接输出目录>/composed-manifest.json
```

`prepared` 是新的接入测试模式，使用显式 BinaryMask，检查 PNG/RGBA 哈希、输出索引、偏移、按钮和格子绑定；发布前重新核对输入，通过后原子发布 `OfficialV1.Zl` 和 `prepared-manifest.json`。该产物始终保持 runtimeReady/productionBindingAllowed/alphaVerified=false，不会被参考或正式交付目录接受。

独立 BC1 核对使用 `tests/game-ui/Verify-ComposedUi.py`，需要 Pillow 与 NumPy；可使用桌面依赖运行时的 Python。客户端 D3D9 核对入口为 `tests/Run-GameUiComposedRenderTests.ps1`。实测结果与限制见 [拼接验证](../../docs/design/assets/official-ingame-ui/acceptance/preparatory/composed-assets/README.md)。

## 真实控件布局宿主回归

构建 Client Debug 后执行 `powershell -File tests/Run-GameUiSceneLayoutHostTests.ps1`。测试在独立进程使用现有 DXWindow、ChatTab、DXTextBox，验证六种分辨率下的对象/消息/滚动/输入矩形保留和按场景保存抑制；不连接服务器或运行正式版游戏。宿主仍待 GameScene 生命周期接线，不代表已激活正式皮肤。

## 布局坐标提案验证

`layout-official-v1.json` 是六种分辨率布局的构建输入，`Validate-OfficialUiLayout.py --guards --output <全新目录>` 验证坐标/覆盖关系后生成 C# 和明确标注的设计图。此阶段的 productionBindingAllowed 固定为 false，生成类不加入运行项目。完整复现命令：`powershell -NoProfile -File tests/Run-GameUiLayoutContractTests.ps1`。见[验证记录](../../docs/design/assets/official-ingame-ui/acceptance/preparatory/layout-contract/README.md)。

## 聊天原控件几何回归

构建Client Debug后执行 `powershell -NoProfile -File tests/Run-GameUiChatLayoutTests.ps1`，验证可选聊天内容/输入布局、历史滚动和原生输入状态。两个接口尚未在GameScene启用；真实IME组合态与交互发包另行验收。见[聊天适配证据](../../docs/design/assets/official-ingame-ui/acceptance/preparatory/chat-geometry/README.md)。

## 聊天与命令区派生边框

当前配方追加chat-history-frame（574×106）、chat-input-frame（574×22）及hud-command-frame（225×202）。使用已选UI[422]的明确边缘采样，中心透明，原有84帧不变；不据此判定原轮盘背景。派生配方随composed-manifest.json保存，Zl清单的sourceManifestSha256绑定该完整输入。独立验证入口为tests/game-ui/Verify-HudSurfaces.py，核对两次构建、原帧保留、原始来源、逻辑尺寸和透明中心。见[本批复现与限制](../../docs/design/assets/official-ingame-ui/acceptance/preparatory/hud-surfaces/README.md)。

## 角色内容区清理与独立装备格

当前配方在上一批87帧基础上仅替换character，追加equipment-cell-frame后共88帧。texture-cover从同图明确的不透明采样区原像素平铺至不重叠目标，保留目标外像素和整图alpha；crop-frame从已适配inventory原像素裁切36×36格子。两种变换均显式登记配方并由完整输入清单哈希绑定；运行时不读取配方。角色四页共用的底板不烘焙槽位，独立格子仍待真实控件接线。

新增13项拼接回归后共45项；独立验证入口为tests/game-ui/Verify-CharacterSurfaces.py。其--previous-art应指向上一批hud-surfaces-art-20261108-v2；其余参数--art、--repeat-art、--package、--repeat-package、--recipe、--layout、--output均须明确指定，完整已执行命令见[本批验证记录](../../docs/design/assets/official-ingame-ui/acceptance/preparatory/character-surfaces/verification.json)。上节Verify-HudSurfaces.py描述的是已封存87帧批次，当前配方的四项派生记录由新验证器核对，不用修改历史报告。

## 编译后部署清单

`New-GameUiReleaseManifest.ps1` 从实际客户端 EXE 和已暂存的 `Data\GameUI\OfficialV1.Zl` 生成部署检查清单，使用客户端本身的 CPU 包验证器；不依赖 Python、编译器或开发机安装路径。需要 Windows、x86 Windows PowerShell 及客户端随包依赖。

`Setup-ClientAssets.ps1 -GameUiStyle OfficialV1 -ReleaseManifestPath ...` 在复制前校验所有目标的 EXE/完整依赖/目录契约及源包，复制后校验目标包，默认仍为 Legacy。清单无法覆盖编译目录的 `RuntimeReady=false`；当前只能生成检查材料，不能发布正式皮肤。

参数、暂存目录和执行顺序见[构建说明](../../docs/build-and-debug.md#14-登录后-ui-部署清单与版本校验2026-11-08)，边界和测试结果见[部署验证记录](../../docs/design/assets/official-ingame-ui/acceptance/deployment/README.md)。

## 紧凑命令轮盘与点击归属（2026-11-08）

当前候选将225×175原生外壳放在225×202画布的(0,27)，按既有截图的部件相对位置收紧七个轮盘按钮。此处坐标为适配布局，未将1280×959附件尺寸解释为游戏客户区；参考设置仍为用户确认的1024×768窗口模式。

八个图像按钮共用普通态透明遮罩，前后绘制顺序显式固定；可见交叠处由最上层接收，隐藏后露出下层，禁用时阻止点击下穿。候选期间关闭按钮自动置顶，解绑恢复原Sort与层次。Belt与模式文字继续独立布局。仅hud-command-frame素材变化，其余91帧保持。

Verify-CompactWheel.py独立验证原生像素、底部对齐、透明补边、其他帧不变和两次构建一致；真实控件测试覆盖228个共享不透明像素。完整命令、源码与运行包哈希见[本批验证](../../docs/design/assets/official-ingame-ui/acceptance/compact-wheel/README.md)。Legacy、RuntimeReady=false、G0和任务状态保持；真实登录、服务器、中文IME、最终画面及整包回退仍待验收。

### 物品悬浮提示候选（2026-11-08）

物品提示仅在场景的 OfficialV1 窗口集合安装成功后使用深色底、细边框和 6 像素内边距。原 `CreateItemLabel` 内容构造、字段计算、品质/条件颜色、定时刷新及物品格悬停事件保留。连续英文长名称使用 GDI TextBoxControl 换行，多属性卡片按原阅读顺序纵向分栏；距屏幕边缘保留 4 像素。窗口集合回退后，下一次处理恢复旧版提示。

`tests/Run-GameUiItemTooltipTests.ps1` 覆盖旧版/候选版文字与颜色逐项一致、真实坐标悬停/离开、金币提前返回分支、碎片、长名称、锁定、多属性、四角、六种分辨率重置、定时更新与回退。资源包未变化。详情见 `docs/design/assets/official-ingame-ui/acceptance/item-tooltip/`。超出分栏容量时启用有限视口滚动：鼠标保留在物品格上，滚轮阅读全部原字段；部分滚轮刻度和阅读位置跨每秒刷新保留，离开重置，回退立即恢复原滚轮分发。超长单条文字直接绘入视口尺寸纹理，不创建超高标签纹理。验证见 `docs/design/assets/official-ingame-ui/acceptance/item-tooltip-overflow/`。已验证极端样本不代表无限长度承诺；UI-19、真实在线与最终画面验收及正式启用门禁仍未完成。

## NPC 原卡片保留回归

```powershell
.\tests\Run-GameUiTests.ps1 -Suite Npc -PackagePath 'E:\workspace\mir3-ui-work\compact-wheel-20261108-v1\zl\OfficialV1.Zl' -EvidenceDirectory 'E:\workspace\mir3-ui-work\npc-acceptance-new'
```

先构建 Debug Client；可用 `-LegacyDataDirectory` 指定项目旧资源目录。独立入口为 `tests/Run-GameUiNpcTests.ps1`。测试在临时目录构造实际 GameScene 和 D3D9 设备，使用原 TargetForm 坐标点击、NPC 解析器、选项节流及回包处理；不创建网络套接字，不操作正式版游戏。

覆盖 Legacy、OfficialV1 测试门禁及重入；比较原卡片像素、正文/选项/关闭样式、15 个对话类型、买卖四种组合、服务连续切换及全部 16 个从属窗口的关闭状态。跨行测试使用原分词器支持的空格分隔文本；不据此声称修复了原解析器对无空格连续长词的命中区域。六种分辨率与 Device.Reset 后检查卡片控制实例、页面、点击及发包；保存逐项断言、坐标、事件轨迹、实际运行文件哈希和截图。NPC、设置及二级弹窗不接入新皮肤，发布门禁保持未通过。

## 2765 原生角色主页（2026-11-10）

角色主页现按 UI[2765] 恢复龙纹、原生槽框与属性底框；16 个装备槽全部在同一主页，仅保留角色/状态/修炼三个页签。六处空槽图案限于 30×30 内区修补，主页层之外的其余 93 帧保持。最新布局、实际渲染、构建与交互记录见[本批说明](../../docs/design/assets/official-ingame-ui/character-material-reselection-20261110/README.md#已落地的-2765-布局2026-11-10)。

本批原生像素与打包帧核验入口为 `tests/game-ui/Verify-NativeCharacter.py`；上文 `Verify-CharacterSurfaces.py` 验证的是此前纹理覆盖的封存批次。两者对应不同阶段，历史报告不回写。
