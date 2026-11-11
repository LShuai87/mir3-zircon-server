# 有物品时的坐标命中与请求一致性验证

2026-11-08，基准 1024×768、窗口模式。本批为离线准备性证据，releaseAccepted=false；不关闭 G0，不调整任务验收状态，也不改变 Legacy 默认或 RuntimeReady=false。

## 本批结果

- PowerShell 7、Windows PowerShell 5.1 分别通过 7,519 项检查：3 次真实 GameScene/UserObject 构造，1,149 次窗口坐标命中，15 帧实际呈现，6 次 Device.Reset。
- Legacy、OfficialV1、OfficialV1 场景重入的 112 条有序语义请求一致；道具定义在各场景独立创建，链接包中的 InfoIndex 先逐一核对真实定义索引，再归一化为来源包裹格用于跨场景比较。
- 原完整场景回归继续通过 180 项检查、3 次构造、17 帧呈现、7 次 Device.Reset、690,512 个面板像素一致；包含六档分辨率、原生聊天输入句柄/草稿/选区、聊天历史与控件保留。
- 本轮仅新增测试、将共用场景测试类改为 partial；没有改动客户端业务代码、正式版安装目录或运行就绪开关。此前 155 个登记源码逐一核对，其中共用测试源的哈希变化明确记入本报告，其余 154 个保持不变。

## 覆盖行为

| 范围 | 实际执行与断言 |
| --- | --- |
| 包裹 49 格 | 各格填充模拟物品，通过 TargetForm 的 MouseMove/Down/Click/Up 路由到原 DXItemCell；点击取起/再次点击取消，选择不发包。 |
| 装备 16 槽 | 包裹首格穿戴至每个原索引；请求锁住源/目标且不提前移动物品；锁住后重击不重复发包；切到属性页后注入拒绝/成功回包，检查隐藏格解锁、原物品身份和末格卸装。 |
| 左右成对装备 | 两枚手镯、两枚戒指各用独立身份，按 6/5/8/7 顺序卸下，核对其他全部槽位没有被互换。 |
| 换装与限制 | 已占装备格互换保持两件物品身份；错误装备类型、未满足等级条件不发穿戴包。 |
| 首末包裹格 | 拒绝后不变、空格移动、不同定义物品交换、部分堆叠/完整堆叠，数量守恒并正确清空来源。 |
| 原穿戴入口 | 右键、同格双击分别经过原事件路由，核对唯一请求与原武器槽索引。 |
| 原拆分弹窗 | Shift+点击打开原生 DXItemAmountWindow，外框不换肤；关闭不发包；确认 7 个时发准确请求且不提前改数量；拒绝后再次经弹窗提交，再注入成功回包核对 13+7。 |
| 快捷栏 10 格 | 鼠标逐格建立物品定义链接；原数字键 1..9/0 与右键使用均回溯到准确包裹格并发出 Count=1；1280×800→1024×768 两次实际重置后重复核对，保留原 cells/links 对象。 |

## 夹具与证据边界

使用实际 Debug 客户端、十个只读 Legacy 库及91帧 OfficialV1.Zl。每次运行均复制 EXE/DLL 至独立临时目录，使用空白地图、35级战士和模拟物品。所有测试物品图标统一使用 StoreItems 的首个非空帧（索引1），所以截图中装备格显示药瓶；这只用于验证有图标的命中及原物品数据绑定，不代表装备美术、人物穿戴外观或最终画面已验收。

连接夹具由未初始化 CConnection 加内存队列构成，Connected=true 只允许原 Enqueue 运行，没有创建 Socket、登录或发送网络数据。ItemMove/ItemSplit 使用原 Process 回包处理器，但成功/失败消息由测试注入。ItemUse 仅断言请求及锁定，随后由测试清锁以覆盖下一入口，未模拟服务器消耗。Legacy 夹具将原快捷栏调整为十格，位置为测试指定；空链接显式使用 -1；OfficialV1 通过测试专用 Prepare 重载装载，不改生产 RuntimeReady。

本批不覆盖真实服务器权限/背包事务、按住鼠标拖动后的释放路径、丢弃/出售/分解、真实登录/重登、中文 IME 组合态、硬件设备丢失或最终美术验收；不能据本批直接勾选完整 A03/A06/A07/A20。

## 复现

先按现有构建说明生成 Client Debug，再在仓库根目录运行（EvidenceDirectory 必须为新目录）：

```powershell
pwsh -NoProfile -File tests/Run-GameUiItemInteractionTests.ps1 -PackagePath E:/workspace/mir3-ui-work/hud-progress-zl-20261108-v1/OfficialV1.Zl -EvidenceDirectory E:/workspace/mir3-ui-work/item-interactions-new
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tests/Run-GameUiItemInteractionTests.ps1 -PackagePath E:/workspace/mir3-ui-work/hud-progress-zl-20261108-v1/OfficialV1.Zl -EvidenceDirectory E:/workspace/mir3-ui-work/item-interactions-windows-new
```

[机器报告](verification.json)、[PowerShell 7日志](item-tests-powershell7.log)、[Windows PowerShell日志](item-tests-windows-powershell.log)、[场景回归日志](full-scene-regression.log)。两套运行的坐标命中、断言、请求轨迹、截图、测试运行时哈希和只读素材记录分别归档在 powershell7/ 与 windows-powershell/。

[新布局角色与包裹截图](powershell7/official-filled-equipment.png)、[49格填充截图](powershell7/official-filled-bag.png)、[扩展装备页截图](powershell7/official-filled-extra.png)。
