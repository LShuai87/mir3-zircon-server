# 独立仓库底板候选

日期：2026-11-08。推进 UI-05 的独立素材准备；UI-25 接入与联动验收尚未开始。基准为 1024×768、窗口模式，已有截图继续复用。

## 来源与组合

候选 role 为 `warehouse-background`，复用已在商店截图确认过的 UI[511] 空白边框及底纹。源图 344×438、offset 0，SHA256 为 `f574ddf094e5f1a0b4d78ccd31da1f9e9b3402ec6be8f9fb9b025358239c85d1`。这是明确的素材复用选择，没有声称 UI[511] 是正式版仓库的原始底图。

[选图输入](../../../../../../build/game-ui/warehouse-selection.json)和[组合配方](../../../../../../build/game-ui/warehouse-composition-recipe.json)固定唯一来源、透明与缩放规则。先将边缘四连通的纯黑转换为二值透明，再执行九宫格组合；左/上/右/下切片为24/44/24/48像素，四角逐像素保留，边条和内部使用最近邻调整，不插值透明度。输出508×480，offset/pivot均为0、Normal混合。

首版508×456预览中，底板内部下边线穿过网格最后一行，已调整为508×480。外部首版art/zl留存用于比较；最终产物使用art-v2/zl-v2。网格没有缩放，格子身份与容量没有修改。

## 控件预留

[候选布局](../../../../../../build/game-ui/warehouse-layout-candidate.json)采用窗口内坐标；布局未冻结。

| 区域 | x,y,width,height | 约束 |
| --- | --- | --- |
| 标题 | 24,10,440,18 | 原标题文字 |
| 关闭 | 478,10,18,18 | 原关闭动作 |
| 筛选容器 | 16,48,473,26 | 保留原名称、类型、清理控件及父子结构 |
| 网格 | 16,78,456,351 | 13列、10行可视，36×36格、35像素步长 |
| 滚动条 | 478,79,14,349 | 原滚动值、范围和滚轮回调 |

容量继续来自GameScene.Game.StorageSize，动态总行数继续使用原RefreshStorage算法；不能将10行可视区解释成容量上限。存取、筛选、滚动、物品格事件、包裹联动及原协议在后续适配中复用。当前未修改StorageDialog或场景入口。

[底板PNG](warehouse-background.png) · [位置示意](warehouse-geometry-preview.png)。示意中的文字、网格线和青色框仅用于审查位置，没有写进底板。

## 输出与验证

独立候选包：`E:/workspace/mir3-ui-work/warehouse-assets-20261108-v1/zl-v2/OfficialV1.Zl`，仅含仓库1帧，121,954字节；不能用它覆盖现有93帧资源包。包摘要及PNG/BC1数据摘要见[独立产物校验](artifact-audit.json)。正式版安装目录没有写入。

[5项针对性检查](focused-tests-final.log)验证四角、全部像素的独立最近邻参考、输入不变、二值透明、非法几何及网格区域。全套[164项Python检查](python-suite-final.log)通过；[原93帧包审计](existing-package-audit.json)仍通过。

独立纯Python BC1解码验证243,840个逻辑透明度像素，5,222个透明像素；PNG到BC1最大通道误差47、平均绝对通道误差0.8961。该误差尚无GPU实绘或视觉批准，不能解释成已达到最终画质门槛。

首次产物核对因当前Python未安装numpy失败，错误保存在artifact-audit-attempt1-error.log；最终验证器改用独立纯Python BC1解码，无需安装依赖。早期合成调用分别缺少decoded/selection.json和显式按钮tint策略，补齐后生成成功；不将这些试运行列为通过记录。

## 后续工作与边界

下一步核对原筛选子控件实际边界，制作可恢复的仓库适配器，验证身份/事件/容量不变，再合并素材、生成配套目录并做GPU、组合布局和真实存取回归。当前用途清单仍保留仓库缺项，待完成接入与验收后再更新；本候选不关闭任何G0门槛。

Legacy默认、RuntimeReady=false、productionBindingAllowed=false。NPC卡片和二级弹窗继续原样式，没有发布、部署或零缺陷结论。源变更和证据摘要见[验证清单](verification.json)。
