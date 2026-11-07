# SlimDX 本地依赖

项目原有的强名称引用依赖 SlimDX 4.0.13.43。为避免依赖开发机 GAC，按 CPU 架构保存到本目录，并通过项目 HintPath 引用、复制到输出目录。

| 文件 | 使用者 | AssemblyVersion | PE Machine | SHA256 |
| --- | --- | --- | --- | --- |
| x86/SlimDX.dll | Client | 4.0.13.43 | 0x014c (I386) | F23EC20AEAEC149BA963D1FB03AF3EA1585CCDF1AC207F315361A80CEAADCC05 |
| x64/SlimDX.dll | Server | 4.0.13.43 | 0x8664 (AMD64) | BA8F80CAB13A87E10ECF659E6B3F98C56C51C0A792C104E7B681A81FB86F43FA |

PublicKeyToken：b1b0c32fd1ffe4f9。

## 来源（2026-11-07）

- x86：NuGet 包 SlimDX 4.0.13.43 中的 lib/NET40/SlimDX.dll。包地址：https://api.nuget.org/v3-flatcontainer/slimdx/4.0.13.43/slimdx.4.0.13.43.nupkg 。包声明作者 SlimDX Group，包维护者 exDreamDuck。
- x64：从本机既有工作副本 E:\workspace\mir4j\Zircon\Components\SlimDX.dll 复制；已核实程序集版本、强名称和 PE 架构。该二进制未重新编译，其更早的分发来源未独立核实。
- 上游源码：https://github.com/SlimDX/slimdx 。MIT 许可证见同目录 License.txt，原文来源：https://raw.githubusercontent.com/SlimDX/slimdx/master/License.txt 。

这两个 DLL 均是包含本机代码的程序集，不能互换。仓库内 DLL 解决编译引用和文件部署问题；客户端还需要 x86 VC++ 2010 CRT 和旧版 DirectX 辅助库。编译后可运行 [Setup-ClientRuntime.ps1](../../build/Setup-ClientRuntime.ps1) 将 9 个本机依赖部署到客户端输出目录，操作及验证范围见 [编译与调试说明第 9 节](../../docs/build-and-debug.md#9-客户端启动时无法加载-slimdxdll)。该脚本仅处理 x86 客户端依赖；服务器地图查看器的 x64 本机依赖及实际图形设备兼容性仍需单独验证。
