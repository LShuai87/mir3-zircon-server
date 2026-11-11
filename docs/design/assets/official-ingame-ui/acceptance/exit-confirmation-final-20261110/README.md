# 退出确认验证（2026-11-10）

新 UI 右下角使用包内 `hud-exit-icon` 四态图，点击打开居中模态确认框，提供“返回角色选择”和“退出游戏”。窗口关闭与 Alt+F4 进入相同确认框。关闭弹窗或 Esc 取消；登录/选角页关闭窗口提供退出/取消确认。

返回角色选择复用 `ClientPackets.Logout`，由原有 `ServerPackets.GameLogout` 切回角色场景。完整退出使用一次性确认标记，避免 `FormClosing` 再次弹窗。保留战斗后 10 秒限制；打开弹窗清除地图鼠标状态并接管输入。系统关机关闭事件正常放行。

## 验证

- Client Debug、Release 构建成功。
- `tests/Run-GameUiHudLayoutTests.ps1`：842 项通过。
- `tests/Run-GameUiFullSceneTests.ps1`：最终 4391 项通过，27 次真实场景构造，48 帧渲染，9 次 Device.Reset，六种分辨率。
- 新增验证包括真实场景鼠标命中与点击、实际 Form.Close 取消、重复关闭复用弹窗、Esc/关闭取消、聊天焦点恢复、地图按住输入时打开确认框、战斗阻止、恰好 10 秒边界、确认退出一次性标记，以及原生 Logout 请求和 GameLogout 场景跳转。
- 确认退出测试观察实际 FormClosing，并由测试事件取消最终关闭，使离线测试进程继续完成其他回归。
- 窗口故障时使用原生确认框的分支已编译，未自动点击验证。

使用真实客户端资源、合成角色/地图与本机回环数据包；未连接真实游戏服务器或验证真实账号退出。未更改 UI 上线验收标记。

效果截图：`official-exit-confirmation.png`（背景为离线合成场景）。
