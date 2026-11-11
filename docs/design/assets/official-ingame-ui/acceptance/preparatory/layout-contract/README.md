# 1024×768 布局坐标提案与生成验证

本批完成结构与坐标的可重复检查，未启用运行皮肤，也不关闭 G0。用户确认的游戏配置/截图布局基准为 1024×768；不依据附件像素尺寸推断 DPI。

- 输入：[布局 JSON](../../../../../../../build/game-ui/layout-official-v1.json)。坐标是实现提案，不是从游戏测量所得。
- 231 个布局项、109 个交互项；16 装备槽、49 包裹格、10 快捷格、57 原属性绑定、8 原公开控件。另列出的等级/经验/生命/魔法 4 项即时值尚未实现。
- 六种分辨率各含 10 个窗口/容器矩形，检查窗口可见性、安全区、HUD 分组互不重叠和角色/包裹默认组合。包裹默认覆盖部分小地图，与参考图层级一致。
- 物品格保留 36×36 外观和 35 像素步长，共享边框只归属一个点击区；未改变全局物品格行为。
- 标注图均写明不是游戏截图；[1024×768 组合](generated/annotations/1024x768-character-inventory.png)和[四页布局](generated/annotations/character-character.png)仅用于审查。
- 9 个故意损坏的输入均被拒绝；两次独立生成的 12 个文件逐字节相同。生成的 C# 编译及读回通过，未加入 Client.csproj。

复现：`powershell -NoProfile -File tests/Run-GameUiLayoutContractTests.ps1`。检查日志及输入哈希见 [verification.json](verification.json)。后续仍须完成最终底图、人物预览、原动作与聊天输入接入，并做实际游戏验收。
