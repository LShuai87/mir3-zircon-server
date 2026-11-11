# Zl 目录生成准备

这是 UI-07 的独立准备工具。当前只接受 kind=synthetic-ui-catalog-preparation、productionBindingAllowed=false，并只发布 Fixture.Zl；不生成或部署 OfficialV1.Zl，也不修改客户端工程的编译项。UI-07 与 A01/A02 均未验收。

[生成器](../../../../../../../build/game-ui/Prepare-UiCatalog.py)读取显式角色契约、资源/布局/字段清单和已经写出的 Zl。完成以下校验后，向一个全新的目录发布包、PreparedUiCatalog.cs 和构建报告：

- **语义目录**：契约内角色完整且唯一，拒绝未知角色、冲突索引和空索引；每张非空 Zl 图片必须有且只有一个绑定。
- **包结构**：核对包 SHA256、图像压缩数据 SHA256、存在位、25 字节图头、逻辑尺寸、有符号偏移和 DXT1 数据长度；拒绝越界、重叠、空洞、尾部数据及未支持的 shadow/overlay。
- **透明与偏移**：按 DXT1 选择器逐像素核对；Opaque 的逻辑像素不得透明，右侧/下方补齐像素必须透明；BinaryMask 允许逻辑像素透明。baked 的包偏移为零，source 的包偏移与清单一致；连续透明和未知混合模式拒绝处理。
- **按钮与布局**：四态必须显式列出，四态几何、pivot、偏移策略及透明/混合方式一致；允许显式复用同一张状态图。六种固定分辨率均须有全部约定矩形，拒绝零尺寸和越界。
- **编译目录**：输出只读的角色、按钮、六分辨率矩形及字段来源描述，同时记录包/契约/清单哈希；生成目录不依赖运行时读取 JSON。
- **发布**：所有输入校验完成后才写同父目录的临时目录，最后改名发布。拒绝覆盖现有目录；写出或发布失败会清理本次临时目录。拒绝输入/输出路径中的 junction 或 symlink。

这套校验不证明官方素材的透明规则、压缩观感、真实窗口命中或字段刷新正确。字段来源是编译期描述，尚未订阅游戏数据事件。最终角色/布局契约必须由 UI-05/06 冻结；不能把夹具契约改名作为正式输入使用。

## 复现

在仓库根执行：

~~~powershell
python tests/game-ui/test_ui_catalog.py
~~~

需要 Python 3.10+ 和 Visual Studio Roslyn C# 编译器。测试通过 vswhere 查找编译器，也可以设置 GAMEUI_CSC。测试必须成功编译并运行真实 writer 和生成目录；没有编译器时会失败，不会跳过后声称通过。

[测试夹具](../../../../../../../tests/game-ui/OfficialUiCatalogFixture.cs)使用项目原 Mir3Library.Save，生成包含空索引、有符号偏移、非 4 倍数尺寸和四态的合成包。[测试入口](../../../../../../../tests/game-ui/test_ui_catalog.py)验证重复构建的全部输出逐字节一致，并覆盖清单反例、二进制/透明反例、重复 JSON key/非有限数、发布失败清理和 CLI 拒绝路径。结果和执行日志见 [validation.json](validation.json) 与 [catalog-tests.log](catalog-tests.log)。

可对已准备的夹具输入单独运行：

~~~powershell
python build/game-ui/Prepare-UiCatalog.py --contract CONTRACT.json --manifest MANIFEST.json --package SOURCE.Zl --output NEW_DIRECTORY
~~~

JSON 字段和夹具生成规则由测试入口保存；输入哈希按原始文件字节计算，改动空白也会改变所记录的输入哈希。
