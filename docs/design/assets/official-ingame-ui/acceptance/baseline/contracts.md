# UI-01 最小接口契约

记录日期：2026-11-07。对应任务起点 HEAD `fc581d5`。下表区分已实现接口与后续接入契约；后续不能把未冻结的坐标或资源索引塞进基础控件。

| 接口 | 状态 | 调用及约束 |
| --- | --- | --- |
| `DXImageControl.BindImage(MirLibrary,int)` | 已实现 | 校验后一次绑定 Library/Index；同步 Size/DisplayArea 并失效纹理。`FixedSize` 保持逻辑尺寸；清除仅允许 null/-1。IndexChanged 回调看到完整绑定。 |
| `DXButton.BindButtonImages(MirLibrary,ButtonImageStates)` | 已实现 | 必须显式指定 normal/hover/pressed/disabled 索引，可重复索引并配色补态；四态尺寸一致。切态不移动矩形；沿用原点击事件。直接 BindImage 清除四态映射。 |
| `DXWindow.WindowSkin` / `GameUiWindowSkin` | 已实现 | 可空、资源不归窗口所有。描述九宫格/固定尺寸、客户区边距、命中内缩、标题/拖动/关闭区域、最小尺寸与关闭四态。统一使用基类 GetSize/GetClientArea/SetClientSize，固定角不缩放。 |
| `GameUiLayoutStore(configDirectory)` | 已实现 | 只写 GameUiLayout.OfficialV1.json；schema=1、style=OfficialV1、六分辨率、WindowType 为键。只存位置/尺寸/许可的 visible；损坏回默认，原子替换失败保留上次内存及文件。 |
| `DXWindow.UseOfficialLayout(store)` | 已实现，尚未接入 GameScene | 必须在首次 LoadSettings 前调用。清空旧 Settings，新的 Load/Save 不触及 CEnvir.WindowSettings。None 不保存；AutomaticVisiblity=false 不恢复/保存服务窗口可见性。 |
| `DXWindow.SuppressLayoutWrites()` | 已实现 | 可嵌套 IDisposable scope；初始化/统一重排/批量恢复期间不写盘。首次 LoadSettings 之前不写盘。 |
| `OfficialUiCatalog` | UI-07 待实现 | 构建生成到 Client/Models/GameUi；包含包哈希、语义 role、尺寸、偏移策略、显式四态、布局常量及属性映射；客户端运行不读构建 JSON。 |
| `GameUiAssets` 启动上下文 | UI-12 待实现 | 在 GameScene 创建前读取 GameUiStyle 并完成全包预检；同一生命周期持有共享 MirLibrary/布局 store。只重启切换，失败在创建窗口前整体回退 Legacy 并提示，禁止窗口间混用新旧资源。 |
| 统一布局入口 | UI-11 待实现 | 单一入口接收 GameScene/六分辨率；默认矩形→许可的保存布局→NPC/包裹/仓库组合约束。整个过程抑制写入并保证可关闭、确认可达；窗口子类不得再各自覆盖官方坐标。 |

固定业务边界：16 装备槽 ID、49 包裹格、10 快捷链接、36×36 物品格（步长 35）、WindowType 枚举和服务端包语义不变。角色页面为角色/扩展装备/状态/修炼；主页面槽 0/1/2/4/5/6/7/8/9，扩展页 3/10/11/12/13/14/15。

测试入口 `tests/Run-GameUiTests.ps1 -Suite ...`：

| 套件 | 当前状态 | 验证范围 |
| --- | --- | --- |
| Assets | 已实现 | 18 项解码测试、70 项编码/参考包检查 |
| Controls | 已实现 | 原子绑定、按钮四态、窗口皮肤；可逐项读四个真实参考库；RenderPath 可启用 14 项真实 SlimDX 像素检查，并对真实参考库的 2,208 张纹理逐字节读回 |
| Layout | 已实现 | JSON/边界/原子写入/新旧隔离；真实生产序列化的旧 Users.db 夹具前后哈希一致 |
| Bindings | 待实现，调用报错 | UI-14/17/18 槽位/属性/容量和既有 handler |
| Npc | 待实现，调用报错 | UI-20..25 文本命中/节流/联动/取消及发包一致性 |

真实登录后的显示、交互发包、IME、性能和设备重置仍由 UI-27 验收。基础夹具与参考转存不替代这部分证据。
