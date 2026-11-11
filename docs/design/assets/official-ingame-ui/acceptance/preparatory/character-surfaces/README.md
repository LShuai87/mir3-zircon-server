# 角色面板底纹与独立装备格验证

记录日期：2026-11-08。沿用用户确认的1024×768、窗口模式基准，复用既有截图。

本批修正角色底图与四标签布局的冲突：原UI[2765]的内容区包含旧装备槽、右侧数值凹槽、底部数值框和分隔线。若直接用作四页共用底板，空白页也会出现假槽位。现保留外围装饰与标题/标签区，将明确的内容矩形以同图空白底纹覆盖；此处为项目16槽/四页布局的美术适配，原内容区的龙纹装饰也随覆盖移除，不宣称与正式版原面板逐像素相同。

| 项目 | 本批结果 |
| --- | --- |
| character | 412×562；取原图[24,452,56,72]底纹，原像素平铺至[8,90,396,461]，尾部裁切，不缩放、不混色 |
| 装饰保留 | 覆盖区外48,988像素完全不变，整图231,544像素alpha不变 |
| equipment-cell-frame | 从已适配inventory的[15,49,36,36]直接裁切；保留UI[120]的原PNG来源哈希与索引 |
| 槽位布局 | 原16个Grid绑定唯一，角色页9槽、扩展装备页7槽；状态与修炼页不铺装备格 |
| 原素材影响 | 之前87帧中仅character改变，其余86帧的图片、索引和来源描述完全不变；追加1帧后共88帧 |

底纹覆盖输入严格限制字段、源/目标矩形与目标数量；源采样及目标必须完全不透明，目标互不重叠。所有矩形先验证再写入，始终从修改前的源图取样，即使采样区位于覆盖区内也不会串扰。每个装备格将由相应页面的控件绘制；底板本身不烘焙任何新装备格，运行时接线仍待完成。

[修改前](character-before.png)、[清理后](character-clean.png)与[独立装备格](equipment-cell.png)均为素材图，不是游戏运行截图。

验证结果：45项拼接回归、16项打包器回归通过；两个独立输出目录中的121个素材文件和2个打包文件逐字节一致；673,835个逻辑alpha像素与13,605个补齐像素通过检查，3,184个内部不透明黑像素保留；实际D3D9绘制91帧/组合，810,185次像素比较通过，最大RGB误差5（容差8），透明像素精确一致。独立像素验证另外核对182,556个覆盖像素均来自指定原纹理、外框与alpha保留、36×36裁切及16槽的页面归属。

产物位于E:/workspace/mir3-ui-work/character-surfaces-art-20261108-v1和character-surfaces-zl-20261108-v1，重复构建为相邻v2目录。OfficialV1.Zl的SHA256为de28e3ce0a84aa670e10b913f3a45a3745a9172fae999ce544bd780a2ab97af4。完整命令和日志见[verification.json](verification.json)，独立美术结果见[art-verification.json](art-verification.json)。输入和旧报告保持原样，本批通过sourceChanges显式登记源哈希迁移。

复现顺序：按build/game-ui/README.md的Compose命令生成两个全新素材目录；分别用ImageManager的prepared模式生成两个全新包；运行tests/game-ui/Verify-CharacterSurfaces.py核对两组输出，参数为--art、--repeat-art、--package、--repeat-package、--previous-art（本批比较hud-surfaces-art-20261108-v2）、--recipe、--layout、--output；再运行Verify-ComposedUi.py及Run-GameUiComposedRenderTests.ps1。输出目录/报告必须为全新路径。

边界：此包仍为接入测试素材，runtimeReady、productionBindingAllowed、alphaVerified均为false。未改GameScene、启动配置或运行时绑定；独立装备格尚未绑定到16个真实控件。最终字体/属性可读性、人物预览、包裹底部按钮美术、完整目录接线、真实游戏操作、IME、联机发包、设备重置及性能仍待验收。NPC卡片、二级弹窗与显示设置沿用原范围。28任务的状态计数和五项G0不变，不将素材验证等同于完成换肤。
