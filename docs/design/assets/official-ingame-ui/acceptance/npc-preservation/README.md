# NPC 原卡片保留回归

按用户决定，NPC 交互卡片保留原外框、正文、选项和关闭按钮。本次仅新增隔离回归及统一测试入口，NPCDialog、CConnection、GameScene 业务源码和 92 帧包均未改变。

## 已验证

- 实际 GameScene 构造，实际 D3D9 渲染及 TargetForm 坐标点击；Legacy、OfficialV1 测试门禁和重入共三个场景。
- 2,084 项检查、99 次实际命中、27 帧实绘、21 次 Device.Reset；六种分辨率下共 2,399,935 个 NPC 卡片像素逐点一致。
- 正文去标记、原黄色下划线与红色悬停、32 位选项 ID、单选项及跨行共用的一秒节流、不同选项独立节流、观察者限制。
- 回包按原 NPCPage 索引解析；打开时原角色/仓库位置规则、刷新释放旧选项、15 个对话类型、买卖四组合、连续切换服务、关闭选项/关闭按钮/服务器关闭与重复关闭。
- 全部 16 个从属窗口纳入原关闭状态检查；任务卡额外验证关闭联动。窗口集合、发包有序轨迹和服务位置在三个场景一致。

## 运行

```powershell
.\tests\Run-GameUiTests.ps1 -Suite Npc -PackagePath <OfficialV1.Zl绝对路径> -EvidenceDirectory <不存在的新目录>
```

可用独立入口 tests/Run-GameUiNpcTests.ps1；先构建 Debug Client。可通过 -LegacyDataDirectory 指定项目旧资源，默认仓库相邻 Debug/Client/Data。测试仅复制项目运行文件到受检查的临时目录，不创建网络套接字，不操作正式版安装目录或正在运行的游戏。

证据：[机器记录](verification.json)、[指标](npc/metrics.json)、[旧风格轨迹](npc/legacy-trace.txt)、[候选轨迹](npc/official-trace.txt)、[重入轨迹](npc/official-reentry-trace.txt)、[执行文件连续性](runtime-continuity.json)。图像见 npc/official-card.png 与 npc/official-wrapped-option.png；已目视检查原正文、下划线及换行。

## 验收边界

本次为使用合成数据及本地出站队列的离线回归。跨行样本使用原分词器支持的空格分隔文本；没有改写原解析器，也不声称修复无空格连续长词的命中区域。WeaponReset 保持原无操作分支；服务业务和物品发包不由本专项全面验收。

UI-20 更新为进行中；前置任务、G0 和最终游戏验收未关闭。Legacy 默认、RuntimeReady=false、releaseAccepted=false；不据此启用或发布正式皮肤。实现方案原文保持不变，不要求用户补图。

首次观察者测试缺少实际 UserChanged 初始化，已修正夹具；初次统一入口参数插入未匹配 CRLF，已修正声明后成功重跑。原始诊断保存在 E:/workspace/mir3-ui-work/npc-preservation-20261108-v1。
