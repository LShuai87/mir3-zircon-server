# 独立商品商店底图补齐

日期：2026-11-08。此批推进 UI-05；未完成窗口布局绑定或发布验收。

## 范围与来源

- 复用既有截图05的独立商品商店，匹配 UI[511]；NPC交互卡片及按钮打开的二级弹窗保持原样。
- 原图344×438，offset为0；来源PNG SHA256为 f574ddf094e5f1a0b4d78ccd31da1f9e9b3402ec6be8f9fb9b025358239c85d1。
- 采用原图尺寸、identity变换及边缘四连通纯黑透明规则。保留内部暗纹与边框，不预制商品、价格或业务文字；透明像素3,840个。
- 用户已确认1024×768窗口模式。设置分辨率、截图像素尺寸分别记录；不据此推断Windows DPI或将图片坐标直接当运行客户区坐标。无需重复截图。

## 输出与兼容性

独立工作目录：`E:/workspace/mir3-ui-work/npc-shop-assets-20261108-v1`。

`build/game-ui/npc-shop-selection.json` 与 `build/game-ui/npc-shop-composition-recipe.json` 保存于仓库；来源安装目录只读。

- 新候选包：30个源图、93帧、19组按钮状态，533,106字节。
- 包路径：`E:/workspace/mir3-ui-work/npc-shop-assets-20261108-v1/zl/OfficialV1.Zl`。
- 包SHA256：50ec53a62c5b564c3e1bf736c5be01e6d86ebbd2cd08994578aafeb7ce031ddb。
- 当前92帧包SHA256：b469289d105e2404361033c81869b610399d7610edee5143dd7e62d1803ccfa7；当前运行清单与编译目录保持原版本。
- 原92张PNG及92段BC1压缩数据按role逐字节一致；重复解码33个文件、93张PNG、组合清单、准备清单及Zl均一致。
- 新npc-shop帧索引87。chat-history-frame 87→88、chat-input-frame 88→89、equipment-cell-frame 89→90、hud-experience-track 90→91、hud-experience-fill 91→92。
- **93帧包须配套重新生成的目录；不得覆盖到当前92帧编译目录下使用。** 校验包含旧目录与新包混用的拒绝用例。

## 验证结果

| 检查 | 结果 |
| --- | --- |
| PNG透明与独立四连通参考逐像素比较 | 通过 |
| 原92帧PNG和BC1压缩数据保留 | 各92帧逐字节相同 |
| 整包重复生成 | 相同 |
| 来源索引错误、来源摘要错误、旧帧损坏、透明边缘泄漏、旧目录混用 | 5类输入均被拒绝 |
| 独立BC1解码 | 1,044,977个逻辑透明度像素；16,383个透明补边像素；4,262个不透明纯黑像素保留 |
| D3D9实绘 | 93帧及3个组合；共1,181,327个像素匹配独立BC1参考 |
| D3D9误差 | 透明区RGB精确一致；不透明RGB通道最大差5，阈值8 |

PNG原色到BC1存在有损压缩，其逐帧误差见[BC1报告](bc1-verification.json)。上表D3D9差值是GPU输出与BC1参考的比较，不能解释为PNG到Zl无色差。

[逐项复现与索引迁移](reproduction-validation.json) · [完整验证报告及成功命令](verification.json) · [GPU日志](render.log)

[商店透明底图](npc-shop.png) · [实际GPU绘制结果](npc-shop-gpu.png)

## 下一步与边界

接入商品列表、滚动条、购买按钮与行会资金勾选框时，复用原控件和购买处理；保留商品刷新、双击购买、滚动、数量弹窗、权限与原协议。同步生成93帧目录并验证安装/回退后，才能切换运行包。

此批不关闭G0-2/G0-5，不修改其他G0状态。Legacy仍为默认，RuntimeReady=false、productionBindingAllowed=false；真实服务器、中文IME及部署回退验收仍待完成。
