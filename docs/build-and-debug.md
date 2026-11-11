# 编译与调试（DevExpress 24.2.8）

## 1. 已验证的开发环境

2026-11-07，在本机使用以下环境完成验证：

- Visual Studio 2022 Enterprise，安装“.NET 桌面开发”工作负载。
- .NET Framework 4.8 Developer Pack / Targeting Pack。
- DevExpress WinForms 24.2.8，程序集版本 24.2.8.0。
- VS 自带 MSBuild：D:\Program Files\Microsoft Visual Studio\2022\Enterprise\MSBuild\Current\Bin\MSBuild.exe。
- 本机 DevExpress 程序集目录：D:\Program Files\DevExpress 24.2\Components\Bin\Framework。

原项目目标为 .NET Framework 4.5.2，本机该版本引用程序集不完整，原构建报 MSB3644。本次将解决方案内的 7 个项目统一为 .NET Framework 4.8；使用 VS 的 MSBuild 构建传统项目及许可证资源。

## 2. 编译

在仓库根目录 E:\workspace\mir3-zircon-server 打开 PowerShell：

~~~powershell
$msbuild = 'D:\Program Files\Microsoft Visual Studio\2022\Enterprise\MSBuild\Current\Bin\MSBuild.exe'
& $msbuild '.\Zircon Server.sln' /t:Rebuild /p:Configuration=Debug '/p:Platform=Any CPU' /m /nologo /v:minimal
& $msbuild '.\Zircon Server.sln' /t:Rebuild /p:Configuration=Release '/p:Platform=Any CPU' /m /nologo /v:minimal
~~~

DevExpress 引用由 build/Dependencies.props 提供安装目录，优先检查系统 Program Files 路径，随后检查本机 D: 安装位置。其他安装目录可通过 MSBuild 参数覆盖：

~~~powershell
& $msbuild '.\Zircon Server.sln' /t:Rebuild /p:Configuration=Debug '/p:Platform=Any CPU' '/p:DevExpressAssemblyPath=C:\YourPath\DevExpress 24.2\Components\Bin\Framework' /m
~~~

VS 设计器也需要找到该目录。对于上述自动检测以外的路径，可在启动 VS 前设置环境变量 DevExpressAssemblyPath，再从该终端启动 VS；或调整 Dependencies.props 中的本地检测路径。仍应使用 24.2.8，避免混用其他补丁版本。

引用已设为 Copy Local，构建会将所需 DevExpress DLL 复制到各应用输出目录。仓库不包含 DevExpress 商业程序集，开发电脑仍需安装对应版本。

## 3. 工程和输出目录

解决方案名称：Zircon Server.sln；共 7 个项目。沿用原项目输出布局，输出位于仓库的上一级，Debug 和 Release 分开：

| 项目 | 目标架构 | Debug 输出 |
| --- | --- | --- |
| Server | x64 | E:\workspace\Debug\Server\Server.exe |
| Client | x86 | E:\workspace\Debug\Client\Zircon.exe |
| Launcher | AnyCPU | E:\workspace\Debug\Launcher\Launcher.exe |
| Patcher | x86 | E:\workspace\Debug\Patcher\Patcher.exe |
| PatchManager | AnyCPU | E:\workspace\Debug\PatchManager\PatchManager.exe |
| ImageManager | x64 | E:\workspace\Debug\ImageManager\ImageManager.exe |
| Library | AnyCPU | E:\workspace\Debug\Library\Library.dll |

Release 输出将上述 Debug 替换为 Release。解决方案平台选择 Any CPU，应用仍按各自的 PlatformTarget 编译。

Client、Server 分别使用 Components/SlimDX/x86 和 x64 中的 4.0.13.43；二进制来源、架构、哈希及许可证见 Components/SlimDX/README.md。

根目录 MirDB/MirDB.csproj 是未加入解决方案的遗留工程，不属于此次迁移的构建入口。当前实际使用的数据库实现位于 Library/MirDB，并编译进 Library.dll。旧独立工程还存在 ClientNPCInfo / ClientMagicInfo 类型缺失和重复类型问题，不能用它的独立构建结果代替当前解决方案的结果。

## 4. 在 Visual Studio 中调试

1. 打开 Zircon Server.sln，选择 Debug / Any CPU，先“重新生成解决方案”。
2. 调试服务器时，在解决方案资源管理器中右击 Server，选择“设为启动项目”。Library 是类库，不能直接作为启动应用。
3. Server 项目“属性 → 调试”中，将工作目录设置为准备好的服务器数据目录；使用默认输出目录时，应设为 E:\workspace\Debug\Server。所有下述相对路径都依赖工作目录。
4. 先在 Server/Program.cs 的 Main、Server/SMain.cs 的 SMain_Load 处设断点，再按 F5。主窗体打开时会加载文件数据库；点击 Start 才进入服务器启动流程，可在 StartServerButton_ItemClick 继续设断点。
5. 客户端可把 Client 设为启动项目单独调试，或在另一 VS 实例中调试；同样设置工作目录为包含匹配客户端资源的目录。

启动前应准备匹配版本的数据副本和资源。编译本身不会准备游戏运行数据：

| 使用者 | 默认相对路径 | 含义 |
| --- | --- | --- |
| Server | .\Server.ini | 网络、版本校验、地图等配置 |
| Server | .\Database\System.db | 系统数据 |
| Server | .\Database\Users.db | 用户数据 |
| Server | .\Backup\ | 文件数据库备份 |
| Server | .\Map\ | 地图 |
| Client | .\Zircon.ini | 客户端配置 |
| Client | .\Map\ 及配套客户端资源 | 游戏运行资源 |

仓库根目录现有的 System.db 不会自动成为 .\Database\System.db；应明确选择数据副本和工作目录。服务器关闭时会保存数据库，调试应使用可恢复的副本。

## 5. 本机连接配置

服务器源码默认监听 127.0.0.1:7000。Server.ini 的对应节可设为：

~~~ini
[Network]
IPAddress=127.0.0.1
Port=7000
~~~

客户端源码的默认远程地址为 145.239.204.13:7100。Client/Scenes/LoginScene.cs 只有在 UseNetworkConfig=True 时才读取 INI 配置的 IPAddress / Port；因此本地调试必须同时设置三个值：

~~~ini
[Network]
UseNetworkConfig=True
IPAddress=127.0.0.1
Port=7000
~~~

Server 默认启用客户端版本校验（Server.ini 的 [System] CheckVersion=True），VersionPath 默认为 .\Zircon.exe。可将 VersionPath 指向本次构建的客户端，或将同一客户端复制到服务器工作目录。客户端运行时使用的 EXE 必须与服务器计算哈希的文件一致。

本地联调直接启动构建的 Zircon.exe；Launcher 的加载逻辑会进入补丁下载，Patcher 的加载逻辑会移动/删除文件并启动目标程序，不应将它们当作普通的客户端调试入口。

## 6. 设计器兼容

所有 DevExpress 引用和许可证类型已迁移到 v24.2 / 24.2.8.0。Server 还补充了 DevExpress.Data.Desktop、DevExpress.Drawing、DevExpress.Printing.Core 引用，解决新版 Grid / RepositoryItem 等类型需要的依赖。

在 VS2022 中选中主窗体的 .cs 文件，按 Shift+F7 打开设计视图。不要直接打开 .Designer.cs 作为设计入口。已实际打开并确认正常呈现：

| 窗体 | 验证范围 |
| --- | --- |
| Server/SMain.cs | Ribbon、NavBar、DocumentManager |
| Server/Views/ItemInfoView.cs | GridControl、列、RepositoryItem、明细表格、Ribbon |
| Launcher/LMain.cs | 主界面、图片、按钮、进度条 |
| Patcher/PMain.cs | WaitForm / ProgressPanel |
| PatchManager/PMain.cs | 编辑框、按钮、进度条 |
| ImageManager/IMain.cs | 编辑框、按钮、进度条 |

这些窗体还执行了设计器保存，并在保存后重新生成 Debug 和 Release。保留了 VS/DevExpress 24.2 自动更新的布局缩放参数、图像资源序列化及许可证条目；当前系统下部分旧窗体的 AutoScaleDimensions 从 6×13 更新为 7×14，等待窗体更新为 6×12。未逐一检查其他所有业务窗体，也未验证其他显示缩放比例下的布局。

如果出现“无法加载基类/控件”，先确认安装路径、24.2.8 DLL 和项目生成结果，再关闭重开设计视图。不要通过删除 InitializeComponent 或控件定义来绕过依赖错误。

## 7. 验证结果与边界

2026-11-07，迁移后的最终结果：

- Debug / Any CPU：7 个项目 Rebuild 通过，0 错误。
- Release / Any CPU：7 个项目 Rebuild 通过，0 错误。
- 上述 6 个真实 VS 窗体设计器可加载和保存。
- 保留 4 条原有编译警告：MapViewer.Designer.cs:237 的 CS0108；PlayerObject.cs:6586、6651、8621 的 CS0162。
- 未执行真实数据库保存、开服、客户端登录/战斗或补丁发布；这些结果不能从编译或设计器验证推导。
- 本次未修改已有 System.db 和 data/，未提交或推送代码。
## 8. 启动时提示“序列不包含任何匹配元素”

如果堆栈指向 SEnvir.LoadDatabase 的 GoldInfo 查询（SEnvir.cs 第 681 行），表示已加载的物品表没有 Effect=Gold 的记录。先检查运行工作目录和 .\Database\System.db：缺文件时 MirDB 会保留空集合，随后 First 查询会抛出这个异常。

2026-11-07 本机排查确认 Debug/Server/Database 原为空目录；仓库根目录 System.db 经只读加载有 1040 条物品，Gold、RefinementStone、Fragment1/2/3、ItemPart、FortuneChecker 均存在。已将原文件复制到 E:\workspace\Debug\Server\Database\System.db，副本与原文件 SHA256 一致，未修改原文件。

其他输出目录需要单独准备数据。以下命令从仓库根目录运行，只创建副本，目标已存在时会报错，避免覆盖已有数据库：

~~~powershell
$databaseDirectory = 'E:\workspace\Debug\Server\Database'
[System.IO.Directory]::CreateDirectory($databaseDirectory) | Out-Null
[System.IO.File]::Copy((Join-Path $PWD 'System.db'), (Join-Path $databaseDirectory 'System.db'), $false)
~~~

代码已在主窗体点击 Start 时检查系统数据库是否存在，缺库会显示绝对路径并取消启动。若文件存在但缺少必需物品，仍需检查数据版本和完整性，不能用 FirstOrDefault 跳过必需数据。该只读检查验证了数据库反序列化和上述必需物品，尚不代表地图、怪物、客户端资源和完整开服流程均可用。

## 9. 客户端启动时无法加载 SlimDX.dll

即使输出目录已有 SlimDX.dll，缺少它依赖的本机 DLL 也会在 Client.Program.Main 入口出现 FileNotFoundException。2026-11-07 本机检查确认：客户端和 SlimDX 均为 x86，SlimDX 版本为 4.0.13.43；缺失的是 VC++ 2010 CRT 和旧版 DirectX 辅助库。

已从微软官方 VC++ 2010 SP1 和 DirectX June 2010 再分发包中提取以下 x86 文件，并放入 Debug/Client 与 Release/Client：

- msvcr100.dll、msvcp100.dll。
- d3dx9_43.dll、d3dx10_43.dll、d3dx11_43.dll、D3DCompiler_43.dll、d3dcsx_43.dll。
- X3DAudio1_7.dll、xinput1_3.dll。

这些文件与 Zircon.exe 同目录部署，不需要管理员权限，也不会安装或修改系统组件。不要用 x64 DLL 替换客户端依赖。

重新生成或清理输出目录后，可在仓库根目录执行：

~~~powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\build\Setup-ClientRuntime.ps1
~~~

默认处理仓库上一级的 Debug\Client 和 Release\Client，两处均需先编译出 Zircon.exe 和 x86 SlimDX.dll。只准备一个输出目录时执行：

~~~powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\build\Setup-ClientRuntime.ps1 -OutputDirectories 'E:\workspace\Debug\Client'
~~~

脚本首次准备缓存时需要网络和 7-Zip，可用 -SevenZipPath 指定 7z.exe；已存在有效缓存时不需要下载或解压。默认缓存为 %LOCALAPPDATA%\Zircon\NativeRuntime\x86，可用 -CacheDirectory 更改。微软下载地址、安装包和每个 DLL 的 SHA256 固定在 build/client-runtime.json 中；脚本在解压前验证安装包哈希和微软数字签名，在复制前验证 DLL 哈希。

验证结果：Windows PowerShell 5.1 下，已有缓存和仅有安装包的全量解压两条路径均执行成功。Debug、Release 输出的独立 x86 探针均能加载全部 9 个本机 DLL 和 SlimDX，Direct3D9 枚举出 2 个适配器，DirectSound 对象创建成功。该验证覆盖本次程序集加载错误；尚未验证实际客户端窗口、完整图形设备初始化、登录和游戏过程。

## 10. 登录连接回调中的 NullReferenceException

如果堆栈指向 System.Net.Sockets.TcpClient.EndConnect 和 LoginScene.Connecting，需检查连接重试与异步回调的生命周期。原实现每 5 秒关闭上一次 TcpClient，再创建新连接；旧回调可能在 Close 后调用 EndConnect。2026-11-07 在本机 .NET Framework x86 独立回环探针中，按“关闭后再执行 EndConnect”的顺序复现了相同的 System.NullReferenceException。原回调虽有 catch，VS 仍可能在异常抛出时中断；旧回调还存在将新 ConnectingClient 清空的问题。

已改为 ClientConnectionAttempt 持有一次连接及其原始 Socket，回调调用该 Socket 的 EndConnect 并发布结果。成功接管、失败重试、取消、断开及场景释放均由客户端主循环管理，回调不再写 CEnvir.Connection 或新连接状态。失败信息写入调试输出，保留 5 秒重试及握手等待行为。

回归验证使用生产代码，仅连接本机临时回环端口，不启动游戏：

~~~powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tests\Run-ClientConnectionAttemptTests.ps1
~~~

测试通过：成功连接及所有权转移、连续 200 次取消与后续新连接、连接被拒绝、参数导致的同步失败。脚本通过 vswhere 查找 VS Roslyn 编译器，也可用 -CompilerPath 显式指定 csc.exe。Client 及 Library 的 Debug / Release 构建均通过。

本机 Debug 客户端已设置 UseNetworkConfig=True、IPAddress=127.0.0.1、Port=7000。服务端 E:\workspace\Debug\Server\Server.ini 应设置 CheckVersion=True、VersionPath=E:\workspace\Debug\Client\Zircon.exe，避免维护一个会过时的服务端客户端副本。

2026-11-07 运行验证发现，服务端仍指向旧副本，其 MD5 为 4175BD6E919872DEA8F7BE6274022229；实际 Debug 客户端 MD5 为 6768C212BBAC09883678677C99CC15C2。已修正已保存配置并重启服务端。在实际 127.0.0.1:7000 上使用生产 Library.dll 数据包验证：当前客户端哈希返回 GoodVersion，故意错误的哈希返回 Disconnect/WrongVersion，版本校验保持开启。此验证只覆盖连接和版本握手，不代表账号登录或游戏流程已通过。

服务端启动时读取版本哈希。以后重新编译客户端，需要在服务端点击 Check Version 重新读取，或重启服务端；仅修改路径不会自动刷新运行中的哈希。

## 11. Debug 客户端无贴图、只有文字

客户端启动时在当前工作目录下寻找 Library/Libraries.cs 列出的 Data\*.Zl；Client/Program.cs 对不存在的文件直接跳过。2026-11-07 检查发现 E:\workspace\Debug\Client\Data 只有 Users.db，没有任何贴图库，因而不会报缺文件异常，但登录背景和按钮均无法绘制。完整资源实际位于 E:\workspace\mir3-zircon-server\data，编译项目不会自动复制这些大体积文件。

在仓库根目录执行以下资源准备命令（默认只处理 Debug）：

~~~powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\build\Setup-ClientAssets.ps1
~~~

需要准备其他输出目录时显式指定（先编译出对应 Zircon.exe）：

~~~powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\build\Setup-ClientAssets.ps1 -ResourceRoot 'E:\workspace\mir3-zircon-server\data' -OutputDirectories 'E:\workspace\Release\Client'
~~~

脚本只复制 Data 下的 .Zl/.png、Map 下的 .map/.xob、Sound 下的 .wav，保留子目录并核对目标文件大小。不复制或覆盖 .db、.ini、可执行文件、翻译配置，不使用镜像删除选项，也不修改资源源目录。资源作为独立副本部署；Debug 约需额外 8.24 GB（十进制）空间。可重复执行以增量同步资源，不应将工作目录改成原始 data 后直接运行，以免在那里生成用户数据或配置。

本机已向 E:\workspace\Debug\Client 准备 327 个贴图/调色板文件、1,011 个地图文件、1,569 个声音文件。Client.csproj 已设置 StartWorkingDirectory 为当前 Configuration 对应的输出目录；从其他启动器运行时，同样需要将工作目录设为 Zircon.exe 所在目录。若客户端在复制前已经启动，必须退出并重新启动，图库列表只在 Main 中初始化一次。

注意：现有 Libraries.cs 中还存在 Data\WWM-WeaponAOH5.Zl、Data\WWM-WeaponAOH6.Zl 两个引用，与当前资源文件名不一致；本次不擅自改动角色武器映射。这两项不影响本次登录界面验证，不能据此宣称全部角色装备贴图均已验证。

部署脚本的隔离回归验证：

~~~powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tests\Run-ClientAssetsTests.ps1
~~~
本次已启动实际 Debug 客户端并检查窗口：登录场景背景、设置齿轮按钮、系统设置对话框及排行榜边框均正常绘制。未输入账号密码，未验证角色选择、进入地图或全部装备效果；本次只准备 Debug 资源，没有重新编译 Zircon.exe，其 MD5 仍为 6768C212BBAC09883678677C99CC15C2。

## 12. 创建角色后不能开始游戏 / failed to spawn character

两条提示分别对应不同条件：

- `Starting the game is currently disabled`：服务端 `Config → Control → Start Game` 未开启。勾选并点击 Save，核对运行目录的 Server.ini 中 `AllowStartGame=True`。
- `Unable to start the game, failed to spawn character.`：PlayerObject.StartGame 无法找到有效绑定出生点，或当前位置及绑定区域均无法生成角色。应先检查服务端地图、地图路径、启动日志和 Safe Zone 的 Bind Region。

服务端也需要地图文件。客户端 Map 已准备好不代表服务端地图已经准备好。2026-11-07 本机 Debug 服务端最初配置 `MapPath=.\Map\`，但 `E:\workspace\Debug\Server\Map` 不存在，导致地图和出生点无法加载。已从项目 data\Map 复制 1,009 个 .map 和 2 个 .xob 到该目录，逐一核对目标文件大小一致。

从仓库根目录可重复执行以下增量复制；robocopy 返回 0–7 为成功，8 及以上为失败：

~~~powershell
robocopy '.\data\Map' 'E:\workspace\Debug\Server\Map' *.map *.xob /E /R:2 /W:1
if ($LASTEXITCODE -ge 8) { throw "Map copy failed: $LASTEXITCODE" }
~~~

在服务端 `Config → System` 将 Map Path 设置为 `.\Map\` 并点击 Save。当前 Map.Load 使用字符串直接拼接目录和文件名，因此路径末尾必须带 `\`；通过文件夹选择框得到的 `E:\workspace\mir3-zircon-server\data\Map` 缺少末尾分隔符时，实际会寻找 `Map0.map` 等错误路径。使用绝对路径也必须补齐末尾分隔符。

地图只在服务启动时加载：准备文件、保存配置后，需要 Stop Server，等待服务停止，再 Start Server。已有客户端会收到 ServerClosing，需重新登录；仅点击 Save 或重新点击客户端“开始游戏”不会补加载地图。

本次使用 Debug 服务端 System.db 的临时副本、生产 Library.dll 和已部署地图做只读出生点检查：244 张配置地图文件全部存在；战士地图 0 有 236 个有效绑定点，法师地图 2 有 188 个，道士地图 1 有 165 个，刺客地图 14_000 有 65 个。已保存 `MapPath=.\Map\`、保留 `AllowStartGame=True` 并正常停止/启动服务；最新启动日志显示加载用时 1 秒，场景对象从 0 增至约 12.4 万。没有修改账号或角色数据库，没有重新编译可执行文件。实际客户端当时停留在重启导致的断线提示，角色进入地图仍需重新登录后确认。

## 13. 点击开始游戏后客户端闪退

2026-11-07 13:03:21 的客户端错误日志及 Windows 应用事件确认：进入地图创建 GameScene 时，无条件读取缺失的拾取列表.txt，抛出 FileNotFoundException。显名列表.txt、高亮列表.txt 也存在同样的问题，原保存方法还使用要求文件已存在的 FileMode.Truncate。

已通过 Client/Envir/ItemListStorage.cs 统一处理：列表文件或其父目录不存在时读取为空列表；保存时创建父目录及文件，并以 UTF-8 写入全部内容。已有列表内容正常保留，拾取列表仍发送到服务端；权限等其他 I/O 错误继续显式报出。GameScene 的三处读取及保存均已使用该实现。

另一个已核实的运行数据缺口是 Debug/Client/Data/System.db 不存在。客户端通过 MirDB Session 从此路径加载地图、物品、怪物等配置，未通过登录协议同步这些表；文件缺失时集合为空，旧日志中的 DataObjectMonster.OnComplete / First 查询因此失败。已从正在使用的 Debug/Server/Database/System.db 复制到 Debug/Client/Data/System.db，未覆盖用户配置、未修改源文件，复制后两份 SHA256 均为 C71D5E308C463DA6FA9BFA325CA7DF0E9460B08BA8129471DCE919FBF0A88919。后续变更服务端系统配置时应同步客户端系统数据，并在客户端重启后加载。

回归测试：

~~~powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tests\Run-ItemListStorageTests.ps1
~~~

测试直接编译并使用生产 ItemListStorage，实现验证三份缺失列表、创建保存、中文 UTF-8、读取不改文件、覆盖旧内容、清空列表、父目录创建及其他 I/O 错误透出。Debug 和 Release 客户端均编译成功。已在正在运行的服务端点击 Check Version，并用实际协议验证新 Debug 客户端获得 GoodVersion、错误哈希仍返回 WrongVersion；本次 Debug EXE MD5 为 BEA7B7817FD1F099328FA5581FB0C40D。

重新启动 Debug 客户端后，已实际观察到角色进入“比奇县”，场景、角色、NPC、小地图及状态栏正常显示。此验证覆盖本次进入地图，不代表所有职业、地图及装备资源均已验证。Release 已编译，但本次运行数据补充与进入地图验证限于 Debug。

## 14. 登录后 UI 部署清单与版本校验（2026-11-08）

资源部署默认使用 Legacy，旧命令继续有效，无需 OfficialV1.Zl。显式指定 OfficialV1 时必须提供清单；脚本核对全部目标后才复制资产，不写入客户端样式配置。客户端普通启动默认使用 OfficialV1，并按启动配置执行完整资源预检；失败回退 Legacy。`Start-LegacyUi.cmd` 或 `--game-ui-legacy` 可强制旧 UI，显式 `GameUiStyle=Legacy` 配置也继续生效。编译目录的 RuntimeReady 单独约束正式发布部署。

先将经过审核的包放入资源暂存根目录的 Data\GameUI\OfficialV1.Zl，再从实际编译输出生成清单。暂存根目录应是独立副本；正式版安装目录只读。以下使用示例暂存路径，运行前需具备对应文件：

~~~powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\build\game-ui\New-GameUiReleaseManifest.ps1 -ClientDirectory 'E:\workspace\Release\Client' -ResourceRoot 'E:\workspace\mir3-ui-work\release-assets' -OutputPath 'E:\workspace\mir3-ui-work\OfficialV1-release-manifest.json'
~~~

输出路径必须是尚不存在的 .json 文件。生成器在独立 x86 Windows PowerShell 进程中读取目标 EXE 的实际编译目录，调用同一 EXE 的 CPU 包预检，验证完整 Zl 结构、几何、包及逐帧哈希，不启动游戏或创建 D3D 设备。清单记录 EXE、同目录全部 DLL、Zircon.exe.config（如存在）、Zl 的长度及 SHA256，以及目录来源哈希、91 帧和19组按钮的当前资源契约。buildId 根据这些内容生成，同一输入在 Windows PowerShell 5.1 与 PowerShell 7 得到相同标识。

当前 RuntimeReady=false 时仍可生成只用于检查的清单，但 OfficialV1 部署会拒绝；没有跳过门禁的参数。Build-OfficialUi.ps1 的 Runtime 模式继续阻止尚未验收的资源发布。G0及游戏验收完成、客户端编译目录就绪后，部署命令为：

~~~powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\build\Setup-ClientAssets.ps1 -ResourceRoot 'E:\workspace\mir3-ui-work\release-assets' -OutputDirectories 'E:\workspace\Release\Client' -GameUiStyle OfficialV1 -ReleaseManifestPath 'E:\workspace\mir3-ui-work\OfficialV1-release-manifest.json'
~~~

部署前，目标目录必须已有匹配的 Zircon.exe 和完整依赖；此脚本只部署资源，不替换 EXE/DLL。缺少清单或资源、目标 EXE/DLL 版本不同、依赖清单缺项、资源哈希不同或实际编译目录未就绪都会失败。执行期间持有包、清单及程序文件的只读租约，避免这些输入在核对后被写入或替换。源与目标必须互不包含，拒绝目录联接或符号链接。

递归复制及用户设置/数据库保留规则沿用原脚本。复制后再次核对目标 UI 包 SHA256，再原子更新目标的 GameUiReleaseManifest.json；运行时不读取此 JSON。多目标的预检全部完成后才开始复制；复制阶段发生磁盘等错误仍可能留下部分资产，应停止启动并重新部署完整匹配包。这不是整目录原子升级或旧整包回退验收。

[部署验证记录](design/assets/official-ingame-ui/acceptance/deployment/README.md)区分实际未就绪客户端的拒绝路径与隔离测试宿主的成功路径；后者不能代替正式游戏发布验收。
