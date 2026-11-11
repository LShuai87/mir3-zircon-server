# 启动选择、资源预检与应用资源所有权

沿用用户确认的 1024×768 基准和既有截图。本批补充 UI-12 的准备性实现，不启用正式皮肤。

- 配置位于 Zircon.ini 的 [Graphics] 节：GameUiStyle=Legacy 或 OfficialV1；缺省 Legacy，允许大小写和首尾空白。无效值保留供诊断，并按 Legacy 创建本次启动会话。
- Program 在配置加载、窗口和 D3D 创建后，登录场景创建前运行预检。路径以 Application.StartupPath 为基准，固定为 Data\GameUI\OfficialV1.Zl；不读取运行时布局 JSON。
- 正式皮肤请求先检查编译目录的 RuntimeReady。当前为 false，因此生产入口记录尚未通过验收的原因并使用 Legacy，不访问正式皮肤包。通过门禁后的预检逻辑在隔离测试中覆盖：校验整包哈希、91 帧数据和全部 GPU 上传；缺包、坏包、无设备均无残留包引用。
- 会话保存启动决定，之后修改配置不会切换风格。包由启动/绘制线程唯一持有；场景候选安装器只能借用。退出按场景销毁、连接清理、包释放、图形设备释放、声音释放的顺序执行，使用 finally 保证后续清理。
- Program 已接入预检和释放；尚未把预检会话传给 GameScene 的候选安装器，也未接通运行中故障时的输入保护。仅修改配置仍不能启用正式皮肤。

验证通过：Debug/Release 构建；启动选择 348 项，含原 ConfigReader 的隔离 INI 读写、无效值、缺包/坏包/无设备、不可热切换、线程约束、文件锁和三轮各 91 帧 GPU 加载/释放；资源预检 418 项及 288 次 GPU 像素比较；窗口集成 9,202 项；91 帧/19 按钮组编译目录及源码格式检查。

未运行实际游戏入口、联机进场/退出/重登、退出故障注入、真实 IME、完整画面或设备 Reset。窗口回归仅包含既有两张装备图形探针，不作为全场景绘制证据。五项 G0 保持 open，RuntimeReady=false，任务仍为 6 已完成、1 进行中、21 未开始；UI-12 不标记完成。继续处理正式场景启用/失败回退与运行中故障保护，随后进行剩余验收。

修改前的 134 项已登记源码快照见 [source-before.json](source-before.json)，新登记的 Program/Config 原始哈希见 [additional-source-before.json](additional-source-before.json)。本批源码哈希、历史迁移和成功日志见 [verification.json](verification.json)，任务复核输出见 [task-validation.log](task-validation.log)。资源包字节和旧报告保持原样。
