# 正式版资源目录编译与真实包验证

记录日期：2026-11-08。沿用用户确认的1024×768游戏/截图基准，既有截图继续复用。

本批将既有88帧真实素材包的描述编译进客户端：角色、包裹、HP/MP、快捷栏、小地图及聊天外框等，包含19组明确的按钮四态。build/game-ui/asset-manifest.json保存构建期描述；Compile-UiCatalog.py严格核对准备清单、来源基线、组合规则、整包及逐帧哈希、尺寸/补齐/偏移、透明填充和四态对应关系，再生成Client/Envir/GameUiOfficialV1Catalog.g.cs。客户端只使用编译后的C#，运行时不读取JSON。

目录描述资源，不创建游戏操作；Horse/Exit等既有预备图不代表新增入口，最终场景操作映射确定后须裁掉范围外或未使用图。RuntimeReady及productionBindingAllowed仍为false，目录状态为pending-scene-integration；未接入Config/Program/GameScene，Legacy仍为默认。构建脚本的Runtime发布阻断保持有效。

真实包：E:/workspace/mir3-ui-work/character-surfaces-zl-20261108-v1/OfficialV1.Zl；346,016字节、88帧；SHA256为de28e3ce0a84aa670e10b913f3a45a3745a9172fae999ce544bd780a2ab97af4。正式安装目录只读，本批不修改安装文件、不部署皮肤。

## 验证结果

- 5组Python测试通过：确定性生成、陈旧输出拒绝且不覆写、重复JSON键、输入输出别名、激活标记、来源/布局参数/逐帧哈希/按钮四态等无效合同拒绝。
- 真实包与编译目录的C#集成测试：3,958项断言、792次帧上传检查、3个所有者生命周期、19组四态语义/尺寸核对；重复UploadAllFrames不重复占用纹理，缓存到期及纹理失效后可重新上传，Dispose释放全部GPU纹理与文件句柄，损坏包不发布资源库。
- 既有资源预检套件418项检查通过，含288次GPU像素比较和中途上传失败清理。
- Debug/Release编译、生成目录字节一致性检查和git diff --check通过。

792次是88帧×3个生命周期×初次上传/缓存恢复/纹理失效恢复。上述失效测试调用DisposeTexture并重传，没有执行完整设备Reset；真实包测试核对GPU尺寸/格式/生命周期，不新增像素一致性结论。既有美术与压缩证据见[角色底图与素材报告](../preparatory/character-surfaces/README.md)。

最初编译器诊断发现路径字符串转义警告，以及把准备清单正常态的null误判为normal，修正后才生成当前目录；该诊断未保存日志，也未计入通过命令。

## 复核入口

在仓库根目录使用Python 3.10+及Pillow：

~~~powershell
python build/game-ui/Compile-UiCatalog.py --manifest build/game-ui/asset-manifest.json --prepared E:/workspace/mir3-ui-work/character-surfaces-zl-20261108-v1/prepared-manifest.json --package E:/workspace/mir3-ui-work/character-surfaces-zl-20261108-v1/OfficialV1.Zl --recipe build/game-ui/composition-recipe.json --selection build/game-ui/reference-selection.json --source-libraries build/game-ui/selection-source-libraries.json --output Client/Envir/GameUiOfficialV1Catalog.g.cs --check
python -m unittest discover -s tests/game-ui -p test_official_ui_catalog.py -v
./tests/Run-GameUiCatalogTests.ps1 -PackagePath E:/workspace/mir3-ui-work/character-surfaces-zl-20261108-v1/OfficialV1.Zl
./tests/Run-GameUiPackagePreflightTests.ps1
~~~

运行C#测试前先构建Client Debug；执行文件位于隔离临时目录。测试复制待测包后做文件锁/破坏验证，原包只读并再次核对哈希。--check验证现有生成文件，不覆写；首次生成仅允许新输出路径。

命令、日志和源码迁移见[verification.json](verification.json)；修改前120项源码基线见[before.json](before.json)。Client.csproj唯一已登记文件迁移为增加生成目录的Compile项，其余为本批新文件。

## 接续工作及边界

下一步把现有属性、装备槽、控件动作与六种分辨率布局收口为完整合同，再由统一场景安装器消费目录。当前尚缺build/game-ui/character-bindings.json；布局仍是proposed-coordinates。因此五项G0和任务计数保持不变，本批不宣称UI-05/07/12完成或已完成完整换肤。

未进行真实联机、输入法、设备Reset、性能、部署及回退验收。NPC交互卡片、按钮后二级弹窗与显示设置沿用现状。
