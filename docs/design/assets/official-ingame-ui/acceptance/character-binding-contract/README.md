# 角色字段、槽位与布局统一映射

记录日期：2026-11-08。沿用1024×768基准；不追加截图请求。

build/game-ui/character-bindings.json现在关联原16个装备格、57个字典属性标签、8个原有命名控件、4个HUD数值镜像和9个原有发包动作。每项保留原数据来源、格式化/刷新约束、稳定ID和原事件，并指向layout-official-v1.json的唯一节点及窗口内逻辑矩形。包含四页、人物预览区域/pivot和六种分辨率的角色/包裹默认位置；角色9主槽+7扩展槽不变，物理抗性继续独立位于状态页。

Assemble-CharacterBindings.py从当前源码重新推导槽位/属性/发包路径，复用现有布局验证，再关联节点并保存输入和源码哈希。--check按字节核对，不覆写；首次输出要求路径不存在，输入文件不能作为输出。文件只作构建期统一输入，客户端仍使用已编译C#，本批没有新增运行时JSON读取。

## 验证结果

- 5组Python测试通过，包含13种独立布局/数据源/发包/操作入口/范围漂移拒绝，稳定ID保留、确定性生成、陈旧文件与覆写拒绝。
- 现有C#装备分页17,248项、属性19,302项、控件4,584项、组合2,115项检查通过；这些套件将编译的候选矩形与同一layout-official-v1.json产生的夹具对照，覆盖四页/六位置、原16格/57属性、原处理函数/本地消息队列、镜像刷新和卸载还原。
- 本批没有运行可选GPU画面；未把本地消息队列验证称作联机验收。

角色窗口内的127个几何节点同时保存在合同中，包含标题/标签/装饰/确认选项等；它们与85项字段路由用途不同。existingControls的targetTab沿用原字段语义分类；标题姓名/婚姻图标在当前C#布局中属于Chrome公共层，最终场景可见性须按原适配器核对，不能仅按targetTab统一隐藏控件。

旧character-composition/character-bindings.draft.json是不可改写的历史证据。本次旧生成器默认检查拒绝它：MainPanel.cs和CharacterDialog.GameUi.cs源码哈希已被后续批次更新，equipmentPageImplementation证据也已推进。没有改写旧文件来消除拒绝；本批重新提取当前源码，生成新的统一文件。该次诊断未保存日志、未计入通过命令。

复核命令：

~~~powershell
python build/game-ui/Assemble-CharacterBindings.py --check
python -m unittest discover -s tests/game-ui -p test_character_binding_contract.py -v
./tests/Run-GameUiCharacterPageTests.ps1
./tests/Run-GameUiCharacterAttributeTests.ps1
./tests/Run-GameUiCharacterControlTests.ps1
./tests/Run-GameUiCharacterCompositionTests.ps1
~~~

源码/日志哈希见[verification.json](verification.json)，修改前126项源码基线见[before.json](before.json)。目录编译与真实包加载见[上一批报告](../catalog-compilation/README.md)。本批仅新增三个文件，不修改原角色/布局适配器。

## 待完成工作

合同状态pending-scene-integration，runtimeReady/productionBindingAllowed/layoutFrozen/releaseAccepted均为false。字段与候选矩形已关联，仍须统一场景安装器按资源所有权和原控件事件接入；完成美术/文本/可见性组合、全入口清单、实际数据/发包比对后才能冻结。Legacy仍为默认，五项G0和任务状态不因本批自动关闭。

未进行真实联机、IME、完整设备Reset、性能、部署或回退验收。NPC卡片、二级弹窗及显示设置沿用现状。
