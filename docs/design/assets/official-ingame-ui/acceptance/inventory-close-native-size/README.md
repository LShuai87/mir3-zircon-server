# 包裹关闭按钮恢复正式尺寸（2026-11-10）

正式 UI[40..43] 四态关闭贴图原尺寸为 21×22；此前为适配旧标题栏区域，被缩到 18×18。本次恢复原尺寸，新增贴图不再缩小。窗口皮肤和布局契约的点击区域同时调整到 (247,8,21,22)，保持右侧 8px 间距。49 格、金币框、负重条和底部按钮布局保持不变。

更新 inventory-close-source.json 的目标尺寸，使用 Prepare-InventoryButtonArt.py 从固定哈希的官方源文件只读提取。四态源图尺寸、源图哈希和嵌入图复现检查通过。

Client Debug 编译成功。包裹专项 10253 项检查通过，其中四态关闭按钮逐像素核对原尺寸贴图，确认未缩放或裁切，原关闭回调及 Legacy 回退验证通过。窗口集成 284355 项检查通过；预览启动校验通过。render/buttons-normal.png 为本轮真实 D3D9 离线预览，另附悬停、按下、禁用状态。尚未登录游戏验收，RuntimeReady=false。

附加全局布局脚本 Validate-OfficialUiLayout.py --guards 被既有角色绑定草稿的哈希不一致阻断：ValueError: Binding source outside workspace or hash mismatch。角色绑定文件及预期哈希未在本次修改；本次关闭区域已由包裹实际控件渲染与窗口集成测试验证。具体文件摘要见 verification.json。
