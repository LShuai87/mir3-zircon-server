# 角色页四项即时值的可选接入

等级、经验、生命、魔法已实现为 `CharacterDialog.UseGameUiReadouts` 的可选绑定，编入 Debug/Release。默认不创建新标签，GameScene 尚未启用新皮肤；本记录不关闭 G0，也不代表实际游戏验收完成。

- 数据直接复制现有 MainPanel 标签的完整文本，监听原控件 TextChanged，不重新计算或解析数值。经验提示复用 ExperienceBar.Hint，保留原经验百分比、上限为 0 分支和地区格式。
- 当前生命/魔法及上限变化，沿用原 GameScene 格式化入口；隐藏角色页时仍同步。标签使用固定单行矩形和省略号；生命/魔法保留完整提示，经验保留原始当前值/上限提示。
- 传入 null 布局解除绑定；参数或矩形校验失败时保留已启用的绑定。替换、禁用、角色页或 HUD 销毁时解除订阅并释放新增标签。
- 绑定矩形相对角色标签页；生成布局中的窗口坐标在最终接入时须扣除实际标签页偏移。独立测试覆盖了这项转换。
- 未替换原 16 个装备格、57 个属性标签、字典对象、原按钮及其点击事件。角色四标签路由、装备位和人物预览仍待最终皮肤接入。

验证结果见 [verification.json](verification.json)：2474 项角色绑定检查通过，覆盖真实 CharacterDialog/MainPanel、真实 HP/MP/XP setter、三个地区格式、六种窗口位置、100 次替换、隐藏刷新和资源销毁。完整 LevelChanged/StatsChanged 的其他业务流程、渲染、发包和游戏验收未运行。

聊天回归 895 项通过。当前布局契约的 231 个节点、109 个交互项通过检查，11 份故意损坏的输入被拒绝；重复生成的 12 个文件逐字节相同，生成 C# 编译与读回通过。Client Debug/Release 构建通过。

当前 [字段草稿](character-bindings.draft.json) 将四项标记为 implemented_opt_in，并记录 runtimeEnabled=false；[布局生成快照](generated-layout/validation.json) 继续保留 runtimeReady=false。历史 character-bindings 和 layout-contract 目录内的证据未改写。

复现：

~~~powershell
powershell -NoProfile -File tests/Run-GameUiCharacterReadoutTests.ps1
powershell -NoProfile -File tests/Run-GameUiChatLayoutTests.ps1
python build/game-ui/Prepare-CharacterBindings.py
powershell -NoProfile -File tests/Run-GameUiLayoutContractTests.ps1 -PythonPath C:/Users/shuai/.cache/codex-runtimes/codex-primary-runtime/dependencies/python/python.exe
~~~

下一步继续最终角色页、装备栏与 HUD 的控件接入，完成最终素材和 G0 证据后启用完整皮肤。1024×768 是用户确认的布局基准；不需要重复截图、登录或修改显示设置。
