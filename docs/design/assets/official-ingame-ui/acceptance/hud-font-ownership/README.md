# HUD 字体所有权修复

基准为1024×768、窗口模式，继续复用已有截图。

临时属性标题曾直接引用原生数值标签的Font。DXLabel销毁时会释放Font，导致切回Legacy后原生标签引用已释放字体。本轮将七个临时标题改为持有字体副本，由各自的控件负责释放。

- 新增行为回归先在旧Debug运行时复现ArgumentException；修复后796项HUD检查通过。
- 检查字体引用身份和高度，覆盖恢复、十次切换、首次安装失败回滚及四种外部控件生命周期；同时确认临时字体已释放。
- 完整离线场景4328项检查通过：27个真实构造、47帧绘制、9次Device.Reset、690512个面板像素一致。
- Debug和Release已重新构建，实际运行文件、依赖和包身份见[验证记录](verification.json)、[Debug清单](debug-build.json)、[Release清单](release-build.json)。

候选实绘：E:/workspace/mir3-ui-work/hud-font-ownership-20261108-fullscene-v1/official-refresh-CharacterTab.png。已检查1024×768画面，标题外观和左下等级圆框保持；黑色世界来自离线夹具。

本轮生产代码只改MainPanel.GameUi.cs，另更新HUD测试。完整命令、失败复现、代码前后哈希及证据摘要记录在verification.json。

真实账号/服务器、交易、实机字体/输入法和最终视觉仍待验收。任务保持7完成/3进行中/18未开始，G0-2/3/4/5保持开放；RuntimeReady=false、productionBindingAllowed=false、releaseAccepted=false，默认Legacy。NPC卡片、二级弹窗和设置保留现状。未提交、推送、部署或改写正式版安装目录。
