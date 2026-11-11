# 包裹标题及底部读数调整（2026-11-10）

本轮保持现有 7×7、49 格包裹，调整 Official UI 预览中的标题、负重、金币和底部三个按钮。

## 实现

- 标题使用“包裹”，文字与窗口装饰采用相近的米金色。
- “重量”改为“负重”。沿用图集已有的 124×9 槽位，内部填充由服务器同步的 BagWeight / Stat.BagWeight 数值计算；正常为金色，超重为红色。鼠标悬浮可读完整数值。
- 填充比例限制在 0～1，容量为零时不执行除法；负数、超重与 int 边界不会越过槽位。
- 金币保留原生整数千分位格式和点击取金币行为；统一标题与数值区域的基线、暗色底框和金棕色边框。长数值限制在原区域并保留完整提示。
- 远程出售、远程拆解、爆率查询三个原有按钮保留原回调，统一暗棕底、金色边框与文字，并覆盖悬浮、按下和禁用状态。
- 切回 Legacy 时恢复标题、文字、几何与控件样式，并释放新增填充控件；保留实时数据。

格子数量、编号、原控件身份和交互未改变。本轮未更改 OfficialV1.Zl 图集、RuntimeReady 或正式发布验收标志。

## 验证

- Debug 编译成功，已写入 E:/workspace/Debug/Client/Zircon.exe。
- 实际 Debug 输出运行包裹测试：6393 项通过，包括 49 格原控件、六种窗口位置、反复安装/回退、动态数据、物品链接、金币点击守卫和负重边界。
- 使用实际候选图集的 D3D9 渲染覆盖 empty、half、full、overweight、zero-capacity、max-gold；GPU 像素检查覆盖填充范围及超重变色。图片见 render/，测试输出见 inventory-tests.txt。
- 窗口集集成测试：284355 项通过，使用本轮相同源码的隔离 Debug 构建；覆盖六种分辨率、窗口生命周期、布局持久化和回退。其实际运行文件哈希见 window-set/tested-runtime.json。
- 布局静态校验与 16 个反例守卫通过，确认 inventoryCells=49。见 layout-validation/validation.json。
- Character bindings 校验通过，已同步布局输入摘要。
- Debug 目录的预览启动校验通过：94 帧，RuntimeReady=false，图集 SHA256 为 dd45cbf51d976a69d28269ee0d27a985def1ee1693cab2b509b74ae347a880a4。

D3D9 图片由真实 InventoryDialog 控件离线渲染，未登录游戏，物品格为空；旧界面图库使用测试桩。这些图片用于本轮标题与底部读数校验，不代表联网场景或整体正式验收。

## 查看

使用 E:/workspace/Debug/Client/Start-OfficialUiPreview.cmd 启动预览；Start-LegacyUi.cmd 可回到旧界面。启动前确认上一客户端已经退出。