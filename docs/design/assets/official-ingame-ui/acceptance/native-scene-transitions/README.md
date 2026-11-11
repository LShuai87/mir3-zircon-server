# 原生登录、断线与场景重入验证

基准为用户确认的 **1024×768 窗口模式**，沿用已有截图。本轮完成客户端原生场景切换的隔离集成验证，并修复两处可复现缺陷。该记录不关闭真实账号、交易、视觉或发布验收门槛。

## 修改

| 文件 | 行为 |
| --- | --- |
| Client/Controls/DXMessageBox.cs | 隐藏或释放弹窗时先清空保存的焦点引用；仅恢复仍有效、且祖先未开始释放的输入框。避免返回登录时激活已释放编辑框，也避免已隐藏弹窗在释放时抢回新焦点。 |
| Client/Envir/CConnection.cs | 为服务器实际会发送的 AnotherUserPassword 增加原生断线提示与返回登录路径，避免默认分支抛 ArgumentOutOfRangeException。服务器安全策略不变。 |
| tests/game-ui/OfficialUiNativeSceneTransitionTests.cs | 新增完整原生构造、回环 TCP 报文解码/分发、断线弹窗回调、旧控件释放和重进聊天恢复测试。 |
| tests/game-ui/OfficialUiFullSceneTests.cs / tests/Run-GameUiFullSceneTests.ps1 | 在原完整场景套件中运行新用例，覆盖 Legacy 与候选 OfficialV1。 |

窗口测试脚本同步记录实际受测 Zircon.exe、Library.dll、SlimDX.dll 的 SHA256，供报告核对运行时身份。

NPC 卡片和二级弹窗的视觉样式保持原样。弹窗修改仅处理其焦点生命周期。

## 最终验证

| 项目 | 结果 |
| --- | --- |
| 完整场景与原生切换集成 | 911 项检查通过 |
| 构造与绘制 | 25 次真实 GameScene 构造，39 帧实际呈现，9 次显式 Device.Reset |
| 面板重置像素 | 690,512 个像素与基准一致 |
| 新增原生入口 | 22 次经报文进入游戏，64 个经 TCP 解码的报文，78 条证据记录 |
| 断线矩阵 | 两套 UI 均覆盖 8 种游戏内原因、真实回环 EOF、OK 确认和右上角关闭 |
| 窗口/启动/聊天既有回归 | 168,890 项检查通过 |
| 构建 | Debug / Release 通过，匹配的二进制清单已生成 |

退出游戏回到选角、选角退出回到登录、直接返回登录、重复返回登录及再次进入游戏均执行原有客户端入口。旧场景的控件、聊天标签、WinForms 编辑框、适配器、借用关系和静态聊天监听均释放；应用级资源包保留到应用会话结束。10 次 Official 重进验证已保存标签名/系统过滤恢复、初始草稿为空、旧历史不跨场景、消息仅投递一次。

普通弹窗的 OK、关闭和隐藏继续恢复原编辑框的 HWND、草稿与选区；额外覆盖编辑框先释放、祖先先开始释放、弹窗隐藏后焦点已转移三类边界。

## 失败记录

- full-scene.log：测试直接访问受保护的控件集合，编译失败；改为测试侧反射访问。
- full-scene-v2.log：复现 Legacy 下断线弹窗关闭后激活已释放输入框的真实空引用。
- full-scene-v3.log：第一次编辑因 CRLF 匹配未更新两个调用入口，辅助方法未被使用；已修正，并保留原始异常诊断。
- full-scene-v5.log：扩大矩阵后复现 AnotherUserPassword 原生分支缺失；服务器发送点为 Server/Envir/SEnvir.cs 的账号安全处理。

上述失败日志均保留。v4 与第一次 final 是中间通过版本；**full-scene-final-v2.log** 与 **window-set-final.log** 对应最终源码/Debug 二进制。verification.json 记录源码前后哈希、各次命令回执、构建和外部证据摘要；audit-history.py 独立复核完整历史。

## 尚未验收

测试使用隔离的合成账号/世界数据、只读旧素材与本机回环连接。它验证客户端实际路径；真实账号认证、远程服务器重连、存取/服务交易、剩余字段/装备槽语义、物理设备丢失、实机字体与中文输入法、最终视觉仍待验收。EOF 测试显式推进超时时间，仅验证原生分支，不宣称验证等待时长。WrongVersion 属于登录版本校验，不在游戏内断线矩阵中。

默认仍为 Legacy；RuntimeReady=false、productionBindingAllowed=false、releaseAccepted=false。任务维持 7 完成 / 3 进行中 / 18 未开始，G0-2/3/4/5 保持开放。未提交、推送或部署，正式版安装目录未修改。
