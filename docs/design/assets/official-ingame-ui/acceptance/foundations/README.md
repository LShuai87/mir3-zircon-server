# 基础设施验证记录

日期：2026-11-07。验证批次：`official-ui-foundations-20261107-2cc5f7ece409`。实现仍在工作区，未生成发布 buildId、未提交。

[机器报告](verification.json) 记录源码哈希、实际命令、退出码、日志与证据哈希；[任务清单](../../../../../tasks/official-ingame-ui/README.md) 记录实现依赖和状态。

| 验证 | 结果 |
| --- | --- |
| 全解决方案 Debug / Release | 通过 |
| 解码与资源编码 | 18 项 Python 测试、70 项资源检查通过 |
| 图片/按钮绑定及真实参考库读取 | 8,726 项通过 |
| 可选窗口皮肤 | 60 项检查通过 |
| D3D9 实绘 | 14 项像素检查通过 |
| 真实参考库纹理 | 2,208 张上传并逐字节读回一致 |
| 独立布局 | 35 项通过，隔离的旧 Users.db 前后哈希相同 |
| 旧回归 | 资源部署、连接取消/失败、物品列表、75 项怪物 HUD 检查通过 |
| 重复构建 | 2,209 个解码文件和 5 个参考包文件逐字节一致 |
| 构建保护 | 已有输出、源目录重叠、Runtime 拒绝及子进程失败清理通过 |

参考资源位于 `E:\workspace\mir3-ui-work\reference-build-v1`，四库总计 4,276 个索引、2,208 张图。`Decoded` 保留 PNG；`Package/Reference` 为有损 opaque DXT1 参考 Zl。原始安装目录只读，源文件按 SHA256 基线校验。资源文件本身不加入 Git，保留 [参考 manifest](reference-manifest.json) 与 [构建溯源](reference-build.json)。

[不透明](skin/skin-opaque.png)、[半透明](skin/skin-half-opacity.png)、[改尺寸](skin/skin-resized.png)、[纹理重建](skin/skin-recreated.png) 是基础皮肤合成夹具，不能作为正式版运行图或最终素材透明效果的证据。报告不包含早期失败调试图。

本基础验证批次完成时，UI-02尚缺当前正式版已登录参考。后续已采集6张原图、覆盖8类视图，并核对6类界面底图，见[运行参考](../reference/README.md)；DPI、客户区及资源关联仍有缺项，G0-1..G0-5均未关闭。原批次验证结果仍只覆盖旧参考包。尚未交付最终 OfficialV1.Zl、编译目录或 GameScene 风格切换。A01..A20 的完整游戏验收均为 not_run；六分辨率交互、IME、NPC 发包、性能和游戏设备重置由 UI-27/28 继续验证。

复验命令见机器报告 commands；Bindings/Npc 仍未实现，调用会明确报错。构建失败夹具脚本在 logs/Check-BuildGuards.ps1，含本机记录路径，使用新的临时输出；故意以 Python 替代 ImageManager 验证子进程失败，不执行正式运行包构建。
