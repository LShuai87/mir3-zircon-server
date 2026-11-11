# 血球中央金色前景贴图（2026-11-10）

血球原先仅绑定 UI[141] 外框和 UI[142..143] 红蓝填充，遗漏了 UI[145] 前景装饰。本次从与 HUD 资源相同的固定哈希源库提取 UI[145]，以 225×202 原尺寸嵌入客户端，在血球画布 (0,0) 绘制。源帧内含中央金色竖饰及边缘装饰；仅边缘连通纯黑转为透明，提取配置记录源库与原始 PNG 哈希，Prepare-InventoryButtonArt.py --config build/game-ui/hud-orb-overlay-source.json --check 可复现校验。

新增 GameUiHudOrbOverlay，层级为外框 → HP/MP 填充 → 金色装饰 → 数值标签。装饰不参与鼠标输入；随 HUD 显隐、移动和分辨率定位。切回 Legacy、HUD 外部销毁和重新绑定时释放装饰位图和纹理。原有 HP/MP 比例和交互继续使用原控件。

Client Debug 编译通过，HUD 专项 832 项、窗口集成 557306 项检查通过。附件 empty/half/full 图片来自真实 D3D9 离线渲染，逐像素验证前景所有不透明像素，确认透明像素保留下层画面；检查绘图状态恢复、鼠标穿透、前景与标签顺序，以及装饰外部销毁回退。已生成默认 Debug/Client/Zircon.exe，OfficialV1.Zl 哈希保持不变。尚未登录游戏实机验收。

全局 Validate-OfficialUiLayout.py --guards 仍因既有 character-bindings.draft.json 摘要与契约记录不符而失败。该角色绑定文件及预期摘要未在本次修改，不能视为全局验收通过。
