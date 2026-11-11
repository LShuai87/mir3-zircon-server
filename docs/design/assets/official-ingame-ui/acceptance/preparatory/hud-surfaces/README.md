# 聊天与命令区派生边框验证

记录日期：2026-11-08。游戏设置和布局基准为1024×768，窗口模式；复用已有截图，不按附件放大后的像素尺寸推算布局。

本批补齐三个素材role：

| role | 所属区域 | 逻辑尺寸 | 内部透明像素 |
| --- | --- | --- | --- |
| chat-history-frame | 聊天历史与标签容器 | 574×106 | 59,488 |
| chat-input-frame | 独立聊天输入行 | 574×22 | 11,440 |
| hud-command-frame | 下方独立命令按钮容器 | 225×202 | 44,600 |

边框来自已核对的UI[422]（bottom-segmented-frame）。四角按原像素复制，水平边使用明确的无分隔线采样区，边条用最近邻调整长度。中心全部透明；不向聊天区或按钮区铺设不透明底图。命令区采用布局提案中的独立矩形按钮，此外框是派生适配，不代表已判定UI[140]/UI[1270]的原轮盘背景。

原有84帧的PNG、索引和来源记录完全不变；追加3帧后共87帧，仍包含19组明确按钮四态。派生记录保留原始PNG哈希、原库/索引和源偏移；输出偏移与pivot为零。具体派生参数保存在composed-manifest.json及其recipeSha256指向的构建配方中；原生打包器继续使用现有Asset结构，prepared-manifest.json通过sourceManifestSha256绑定完整输入清单。

已验证：32项拼接回归、16项打包器回归；两次拼接119个文件及两次Zl构建2个文件字节一致；672,539个逻辑像素的alpha与13,605个补齐像素符合BinaryMask；原有3,178个内部不透明黑像素保留。真实D3D9绘制90帧/组合，808,889次像素比较通过，最大RGB误差5（容差8），透明像素精确一致。

已保存文件：

- [修改前源哈希快照](before.json)
- [真实素材、原帧保留和确定性检查](art-verification.json)
- [本批完整验证与源哈希迁移](verification.json)
- [构建配方](../../../../../../../build/game-ui/composition-recipe.json)
- [独立验证器](../../../../../../../tests/game-ui/Verify-HudSurfaces.py)

本批输出位于E:/workspace/mir3-ui-work/hud-surfaces-art-20261108-v2、hud-surfaces-zl-20261108-v2；重复构建为相邻v3目录。Zl名称为OfficialV1.Zl，SHA256为fb97489bb3a2d2edb52e7b0c2f6fb67b98c9183eff6eb69c25e5fd610cdcc2d4。v1为开发检查中淘汰的产物，不作为本批验证结果。

此包保持runtimeReady=false、productionBindingAllowed=false、alphaVerified=false。GameScene尚未绑定；字体、人物预览遮挡、实际聊天/IME、联机操作、设备重置、性能与部署回退尚未验收。NPC卡片、二级弹窗和显示设置维持已有范围。任务状态和五项G0不变。
