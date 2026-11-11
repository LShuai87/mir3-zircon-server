# 角色绑定草稿与切换约束

本目录是 UI-06 的独立准备输入，**不是冻结布局或已接入的游戏 UI**。原实施方案和历史基线保持原样。机器清单见 [character-bindings.draft.json](character-bindings.draft.json)，静态核对结果见 [validation.json](validation.json)，六个损坏草稿反例和逐字节重复生成结果见 [guard-checks.json](guard-checks.json)。

## 页面与装备

页面使用稳定 ID：

| ID | 标签 | 内容 |
| --- | --- | --- |
| character | 角色 | 9 个主装备槽、基础属性、七系元素、身份、即时数据 |
| extraEquipment | 扩展装备 | 7 个扩展槽，按 4+3 排列 |
| status | 状态 | 11 项扩展属性、物理抗性强/弱显示 |
| hermit | 修炼 | 14 项现有修炼属性、剩余点数、原确认开关和 8 个分配按钮 |

主装备槽 ID 为 0/1/2/4/5/6/7/8/9；扩展槽为 3/10/11/12/13/14/15。每个槽只保留一个原 DXItemCell，Grid[id]、Slot、Equipment backing grid 和 GridType 不变。切换页面只改变 Parent、位置和可见性；不复制物品格，不互换左右槽，不把火把改为腰带。

57 项字典属性全部记录独立 ID、原控件引用、目标页面、数据来源、原格式策略和源码位置：角色 30 项、状态 13 项、修炼 14 项。物理抗性两项位于状态页，七元素顺序仍为火/冰/雷/风/神圣/暗黑/幻影。原 GetFormat、正/负/零分支、图标 Tag 和颜色语义由现有更新逻辑保持，不在皮肤中重新计算。

另保留姓名/行会/职位、两种负重、剩余修炼点、婚姻图标、头盔开关共 8 个现有控件引用。新加入角色页的等级、经验、生命、魔法四项尚未实现；清单标为 not_implemented，并列明现有 HUD 数据与刷新入口。

## 刷新与原动作

- StatsChanged 继续调用 CharacterBox.UpdateStats；保留 User.Stats/HermitStats setter 的刷新路径。S.StatsUpdate 先写 HermitPoints，再写 Stats、HermitStats。
- 新生命/魔法标签同时跟随当前值和上限变化；新经验标签同时跟随经验和等级导致的上限变化。用原 HUD 的格式及最大经验为 0 分支。
- 负重沿用 WeightChanged 与超重变红。身份、婚姻、头盔状态保留原回包/更新入口。
- 原 8 个修炼按钮复用现有 handler，包括零点数条件、二次确认开关、确认/取消及 C.Hermit 参数。头盔继续发送原 C.HelmetToggle。9 个发包动作入口不因换皮重复订阅。

## 资料差异与未冻结项

历史 [实现起点基线](../../baseline/implementation-start/baseline.json) 的部分 targetPage 中文含 Unicode replacement character。枚举名称、槽位 ID 和属性键仍可核对。此草稿从冻结方案 §6.2/6.3 重建目标页面，使用英文 ID 与显式 UTF-8 中文标签；历史文本不作为路由键。

早期方案按 UI[100] 366×484 描述角色视觉；当前 [截图匹配](../../reference/panel-matching.json) 对应 UI[2765] 412×562。最终资源选择需在 UI-05/06 明确，不能把旧图坐标整体缩放到新图。本草稿没有替代原方案，没有冻结任意一套最终坐标。

以下输入仍为空：16 槽 visualRect/hitRect/iconRect、人物 pivot、属性/按钮矩形、滚动区、六分辨率窗口组合和真实交互对照。因此 productionBindingAllowed=false、layoutFrozen=false；UI-06、UI-17/18 和 A03/A05 均不标完成。

## 校验入口

在仓库根执行：

~~~powershell
python build/game-ui/Prepare-CharacterBindings.py
~~~

该命令只读检查。显式 --write-draft 才重建草稿；源码、冻结方案或历史字段集变化时应先审查原因。校验拒绝重复 JSON key、错漏槽位、原字典字段丢失、错误页面/数据源以及把草稿标为生产可用。它核对当前源码和文档，未运行客户端、未验证绘制或发包行为。
