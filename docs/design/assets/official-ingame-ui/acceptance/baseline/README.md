# 实现基线及未采集项

源清单：[implementation-start/baseline.json](implementation-start/baseline.json)。接口：[contracts.md](contracts.md)。

本次在已有基础代码工作区补录：HEAD 为 `fc581d5`，18 个历史关键文件中 13 个仍相同、5 个有基础设施修改，逐文件哈希和原因已记录。原方案历史 HEAD 与文件哈希保持原样；此快照不冒充编码前的纯净运行现场。

已按源码清点 16 槽、57 项角色字典绑定、76 项默认按键定义及可定位路由、17 个 NPC 窗口，以及公共标签、HUD 入口、确认/数量弹框和发包构造位置。角色绑定项逐项记录目标页面与验证任务；库存/快捷栏/仓库保留现有业务动作。

旧客户端登录后截图、旧操作发包轨迹和当前正式版登录后截图均未采集。UI-01 按允许的源码基线交付；UI-02/G0-1 与后续行为/视觉验收继续开放。正式版运行证据需覆盖 HUD、实际角色面板、包裹/快捷栏、小地图、聊天、物品提示、普通 NPC 对话及正式版实际存在的一种服务；每张附版本、分辨率、DPI、窗口模式和操作状态。项目新增四标签及独有 NPC 服务使用已确认视觉元素适配，在本项目验收，不要求正式版存在一一对应截图。见[采集要求](../reference/README.md)。

重采集命令（必须使用新目录，保留前一快照）：

```powershell
python .\build\game-ui\Capture-UiBaseline.py --output .\docs\design\assets\official-ingame-ui\acceptance\baseline\snapshot-next
```
