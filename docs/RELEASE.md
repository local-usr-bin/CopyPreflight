# CopyPreflight — v0.1 发布与分发（M9）

> **状态：v0.1.0 的发布形态已冻结。**
> 本文件记录发布形态、可复现的发布与打包命令、版本如何进入二进制、发布包内容与审计方式、
> 用户使用说明、支持范围与已知限制。它是发布这件事的事实来源；扫描 / 探测 / 规则 / 编排的行为
> 仍由 [`SCANNER.md`](SCANNER.md)、[`TARGET_PROBING.md`](TARGET_PROBING.md)、[`RULES.md`](RULES.md)、
> [`WORKFLOW_UI.md`](WORKFLOW_UI.md) 定义，本文件不重复，也不改动它们。

## 1. v0.1.0 发布形态

| 项目 | 决定 |
| --- | --- |
| Version | `0.1.0` |
| Platform | Windows **x64** |
| Deployment | **self-contained**（.NET 10 运行时随包提供） |
| Distribution | **普通 publish 文件夹 + ZIP** |
| 目标机器前置条件 | **不需要**预装 .NET 10 runtime |

**本轮明确不做**（不是遗漏，是范围）：MSI、MSIX、ClickOnce、Microsoft Store、winget、任何 installer、
auto update、single-file EXE、trimming、Native AOT、ReadyToRun、代码签名，以及 ARM64 / x86 发布。
v0.1 不为了减小包体或提升启动性能引入任何新的发布优化变量。

## 2. 从 clean repo 到 v0.1 publish folder

发布**只针对 `CopyPreflight.App` 项目**，不针对 solution。以下命令在仓库根目录执行，不需要 IDE、
不依赖 Visual Studio 的任何 UI 状态：

```bash
# 输出目录不会被 publish 清理；先删掉旧目录，保证结果只来自这一次发布。
rm -rf artifacts/release/v0.1.0/win-x64/publish

dotnet publish src/CopyPreflight.App/CopyPreflight.App.csproj \
  -c Release \
  -r win-x64 \
  --self-contained true \
  -p:PublishSingleFile=false \
  -p:PublishTrimmed=false \
  -p:PublishAot=false \
  -p:PublishReadyToRun=false \
  -p:DebugType=None \
  -p:DebugSymbols=false \
  -o artifacts/release/v0.1.0/win-x64/publish
```

（Windows 上若在 `cmd.exe` 里执行，把续行符 `\` 换成 `^`；写成一行同样有效。）

### 2.1 每个参数为什么在这里

| 参数 | 理由 |
| --- | --- |
| `-c Release` | 发布只用 Release 配置。 |
| `-r win-x64` + `--self-contained true` | 目标机器不需要预装 .NET 10 runtime。**RID 写在命令行而不是 csproj**：如果写进项目文件，`dotnet build` / `dotnet test` 的输出路径与开发流程都会跟着改变，而它们没有任何理由因为"要发布"而改变。 |
| `PublishSingleFile=false` | 保持普通文件夹：内容可以用资源管理器直接查看，杀毒软件与备份工具对它的处理是常规的，出问题时能看到真实的文件布局。SDK 默认就是 `false`；显式写出来，是为了让"不启用单文件"成为可复现命令的一部分，而不是一个会随 SDK 默认值漂移的隐含项。 |
| `PublishTrimmed=false` | WPF **不支持** trimming。不为了体积去赌一个本平台不支持的模式。 |
| `PublishAot=false` | Native AOT **不支持** WPF。 |
| `PublishReadyToRun=false` | ReadyToRun 是启动性能优化。本轮不做性能型发布优化，也不为它付出包体代价。 |
| `DebugType=None` + `DebugSymbols=false` | 分发的 ZIP 里不放调试符号。**只作用于这一次 publish 命令行**：`dotnet build -c Release` 仍然照常产出 PDB，本地调试配置没有被改动。 |
| `-o artifacts/release/v0.1.0/win-x64/publish` | 输出落在仓库内、已被 `.gitignore` 忽略的 `artifacts/` 下。 |

**为什么不用 publish profile（`.pubxml`）**：仓库里没有任何 `Properties/PublishProfiles`，所有构建与运行命令
都以显式 `dotnet` 命令的形式写在文档里，这是本仓库既有的风格。本轮沿用该风格，不引入 Visual Studio 专属的
profile 文件——那会让"怎么发"分散到 IDE 状态里。

## 3. 版本如何进入二进制

三个产品项目各有一条版本属性，没有别的机制：

- `src/CopyPreflight.App/CopyPreflight.App.csproj`：`<Version>0.1.0</Version>`，外加文件元数据
  （`Product` / `AssemblyTitle` / `Company` / `Description`，让可执行文件的属性页不再显示
  "CopyPreflight.App" 这种工程名）。
- `src/CopyPreflight.Core/…csproj`、`src/CopyPreflight.Windows/…csproj`：同样一条 `<Version>0.1.0</Version>`，
  使随包分发的三个产品程序集报告同一个版本，而不是 exe 说 0.1.0、自家 DLL 说 1.0.0。

`.NET` SDK 由 `<Version>` 派生 `AssemblyVersion` 与 `FileVersion`。发布版 EXE 实测：

```text
FileVersion      0.1.0.0
ProductVersion   0.1.0
ProductName      CopyPreflight
FileDescription  CopyPreflight
CompanyName      CopyPreflight
```

**显式关闭了 revision 后缀**（`IncludeSourceRevisionInInformationalVersion=false`）。默认情况下 SDK 会把
"当时 checkout 的提交"拼进 `InformationalVersion`，于是发布版会显示成 `0.1.0+<某个提交>`：它指向的是构建时
恰好留在工作区里的那个提交，而不是这个包所代表的版本；同一个版本在两个不同的 checkout 上构建，还会报出
两个不同的 product version。所以包版本就是干净的 `0.1.0`，**是哪个修订产生了某个 ZIP 记在 ZIP 旁边**
（见 §5），而不是塞进二进制里。

没有版本管理框架，没有 `Directory.Build.props`，没有第三方 versioning 包。版本是源码树里的一个常量，
不依赖构建时间或构建位置。

## 4. 发布包内容与审计

### 4.1 应该有什么、不应该有什么

应当存在的：入口 `CopyPreflight.App.exe`、三个产品程序集（`CopyPreflight.App.dll` / `Core.dll` /
`Windows.dll`）与其 `deps.json` / `runtimeconfig.json`、以及 **self-contained 所必需的 .NET 10 运行时**
（`coreclr.dll`、`clrjit.dll`、`System.Private.CoreLib.dll`…）、WPF 运行时（`PresentationFramework.dll`、
`PresentationCore.dll`、`WindowsBase.dll`、`wpfgfx_cor3.dll`…）与它们的本地化资源目录。

**运行时分发依赖不是脏东西，不要为了"看起来干净"手删。**

不应当存在，并且在本次发布中确实不存在的：`tests/` 与任何测试夹具、人工验收目录（`manual-*`）、`.git/`、
`.workbuddy/`、`.codebuddy/`、`MEMORY.md` 或任何当日日志、任何 `*.cs` 源码、任何 `.csproj` / `.sln`、
发布输出之外的开发缓存、Git 配置、用户真实路径或数据、secrets / token / session、构建日志、临时 probe 文件、
调试符号（`*.pdb`）。

### 4.2 本次实测（v0.1.0）

| 项目 | 实测值 |
| --- | --- |
| 文件数量 | **401** |
| 总大小 | **146,542,305 字节**（139.75 MiB） |
| 可执行文件 | `CopyPreflight.App.exe`，**162,816 字节** |
| 产品程序集 | `CopyPreflight.Core.dll` 33,792 / `CopyPreflight.Windows.dll` 130,560 / `CopyPreflight.App.dll` 54,784 字节 |
| 本地化资源目录 | 13 个（`cs` `de` `es` `fr` `it` `ja` `ko` `pl` `pt-BR` `ru` `tr` `zh-Hans` `zh-Hant`） |
| `*.pdb` / `*.cs` / `*.csproj` / `*.sln` / `*.log` / `*.tmp` | 0 |
| 最大的三个文件 | `System.Private.CoreLib.dll` 16.0 MB、`PresentationFramework.dll` 15.8 MB、`PresentationCore.dll` 8.3 MB |

**包体约 140 MiB 不是缺陷**：self-contained 发布必然携带整个 .NET 运行时。压缩成 ZIP 后是 60.25 MiB。

数字会随 .NET SDK 补丁版本小幅变化；重跑时以自己那一次的输出为准。

### 4.3 审计命令（可重跑）

```bash
# 文件数量与总大小
powershell -NoProfile -Command "$f = Get-ChildItem -LiteralPath artifacts\release\v0.1.0\win-x64\publish -Recurse -File; $f.Count; ($f | Measure-Object Length -Sum).Sum"

# 不应出现的内容（期望：一个都不匹配）
powershell -NoProfile -Command "Get-ChildItem -LiteralPath artifacts\release\v0.1.0\win-x64\publish -Recurse -File | Where-Object { $_.Name -match '\.(cs|csproj|sln|pdb|log|tmp|md|trx|dmp)$' } | Select-Object -ExpandProperty FullName"

# 版本元数据
powershell -NoProfile -Command "(Get-Item -LiteralPath artifacts\release\v0.1.0\win-x64\publish\CopyPreflight.App.exe).VersionInfo | Format-List FileVersion,ProductVersion,ProductName,FileDescription"
```

## 5. ZIP 与 SHA-256

ZIP 从**最终的 publish folder** 生成，解压后是单一顶层目录，双击里面的
`CopyPreflight.App.exe` 即可运行，**不需要任何安装脚本或第二步骤**。

```bash
# 1) staging：把 publish 内容放进一个名字干净、与版本一致的顶层目录
#    artifacts/release/v0.1.0/staging/CopyPreflight-0.1.0-win-x64/
powershell -NoProfile -Command "Copy-Item -Path artifacts\release\v0.1.0\win-x64\publish\* -Destination artifacts\release\v0.1.0\staging\CopyPreflight-0.1.0-win-x64 -Recurse -Force"

# 2) 压缩
powershell -NoProfile -Command "Compress-Archive -Path artifacts\release\v0.1.0\staging\CopyPreflight-0.1.0-win-x64 -DestinationPath artifacts\release\v0.1.0\CopyPreflight-v0.1.0-win-x64.zip -CompressionLevel Optimal"

# 3) 校验值
powershell -NoProfile -Command "(Get-FileHash -LiteralPath artifacts\release\v0.1.0\CopyPreflight-v0.1.0-win-x64.zip -Algorithm SHA256).Hash"
```

`certutil -hashfile <zip> SHA256` 或 `sha256sum <zip>` 得到同一个值；随 ZIP 一并写出的
`CopyPreflight-v0.1.0-win-x64.zip.sha256` 也是这个值。

**本次实测（v0.1.0）**

| 项目 | 实测值 |
| --- | --- |
| ZIP 路径 | `artifacts/release/v0.1.0/CopyPreflight-v0.1.0-win-x64.zip` |
| ZIP 大小 | **63,177,759 字节**（60.25 MiB） |
| SHA-256 | `1CCA2BD7EDF065B50E5B928A7746E7CF9C94F5C51E3590C0838B57558F45A0A8` |
| 解压后顶层 | 单一目录 `CopyPreflight-0.1.0-win-x64/`（401 个文件，146,542,305 字节） |
| 解压可执行文件 | SHA-256 与 publish folder 中的 EXE **完全一致** |

**publish 输出、staging 目录与 ZIP 都不进入版本库**（`artifacts/` 已被 `.gitignore` 忽略）。`artifacts/`
整个目录可以随时删除，必要时按 §2 与本节重跑即可重建。

## 6. 使用说明

### 6.1 用途

CopyPreflight v0.1.0 是一个 **Windows 文件迁移前的只读预检工具**。它回答的是：
"把源文件夹里的这些内容搬到目标文件夹，有哪些已知的、会拦住或影响这次搬迁的问题。"

**它不是复制软件，也不代替你的复制工具。** 迁移怎么做、用什么工具做，仍由你决定。

### 6.2 基本使用

1. 选 **Source folder**（源文件夹）——你要搬走的内容所在的目录。
2. 选 **Target folder**（目标文件夹）——你打算搬到的目录。
3. 点 **Start check**。
4. 看结果：表格按 **Error → Warning → Info** 排列；选中任意一行，右侧显示它的
   Explanation（为什么）、Impact（会有什么影响）、Recommendation（建议怎么做）。

映射关系写在你面前的界面上：`source\<relative path>` → `target\<relative path>`，
**目标文件夹本身就是这次迁移的目标根目录**，不会在目标下再套一层以源目录命名的文件夹。
两个路径都必须是完整路径；相对路径不会被解析成"相对于程序启动的目录"。

### 6.3 核心安全边界（v0.1 不可协商）

- **不复制**
- **不移动**
- **不删除**
- **不自动改名**
- **不自动修复**
- **不修改权限、不格式化**
- **不向源目录或目标目录写入任何 probe 文件**（不建测试文件、不建目录、不改 ACL）
- **不读取文件内容**，也不计算 hash
- **"目标可写"永远不被声称**——可写性没有、也不会通过写操作验证

整个过程对源与目标是**只读**的。

### 6.4 结论怎么读

检查跑完且没有发现阻塞问题时，界面显示的是：

> **No known blocking issue was detected.**

这**不等于** "复制一定会成功"。它只说明"基于本次实际观测到的事实，没有发现已知的阻塞问题"。
目标的某些能力可能仍是 Unknown，运行时状态（权限、占用、网络、磁盘故障）会变，
复制程序的行为也可能与本工具的假设不同。

### 6.5 Error / Warning / Info 的实际含义

| 级别 | 含义 |
| --- | --- |
| **Error** | 基于当前已知事实，确定或接近确定会**阻止**迁移。例如目标文件系统无法容纳单个超大文件，或某个名称在 Windows 上根本用不了。 |
| **Warning** | 风险真实存在，但最终结果取决于复制程序、你的策略、环境或运行时状态。例如目标位置已存在同名文件，最终行为取决于复制程序的重名策略。 |
| **Info** | 通常不阻止内容到达，但存在值得你知道的行为变化、能力差异或元数据变化；也包括"这一项检查没能做成"的说明。 |

覆盖情况与结论**并列显示**，不会被结论掩盖：某个阶段"未执行"或"未完成"会单独写出来。

## 7. 首版能力概览

- **源侧清单（source inventory）**：递归观察源目录，给出文件数、目录数、reparse point 计数与逻辑总字节数，
  以及无法读取 / 无法枚举的部位。
- **目标能力**：目标文件系统名称、当前用户真正可用的空间、单路径组件长度上限；无法确定的保持 **Unknown**。
- **FAT32 单文件上限**：目标为 FAT32 时，超出单文件上限的文件会被报出来。
- **路径与名称检查**：单路径组件长度、传统 `MAX_PATH` 兼容性、Windows 非法字符、保留设备名、
  结尾空格或句点。
- **大小写碰撞告警**：同一目录内仅大小写不同的兄弟名称会被提示（Warning，因为目标是否区分大小写未经探测）。
- **reparse point 的保守处理**：源与目标两侧遇到 reparse point 都按保守边界处理，只记录事实，不进入、不解析。
- **目标已存在对象 / 碰撞**：逐个映射位置只读观察目标上当前有什么，区分已存在对象 / 确证缺失 /
  检查失败 / 未查询。
- **文件与目录的结构冲突**：目标位置上挡路的普通文件、类型不匹配等会被区分报告。
- **覆盖情况与 Unknown 报告**：运行状态、实际覆盖、风险三个维度分开呈现；未执行与"执行了但没发现"始终是两件事。

## 8. 首版未实现 / 已知限制

- **不做实际复制或移动**，也不执行任何 overwrite / skip 策略——工具只报告，不决定重名时怎么办。
- **不做 symlink / junction 的精确解析**：只记录遇到了 reparse point 以及保守的边界，不还原其指向。
- **不做 ADS / ACL / 时间戳丢失分析**，因此这类元数据风险不会被单独报出。
- **不做内容或 hash 校验**：不读文件内容，因此"内容是否一致"不在结论范围内。
- **不做 Unicode 规范化深度兼容分析**。
- **不做报告导出**（CSV / JSON / HTML 均无），也没有多源目录批量检查、历史对比、结果过滤与分组。
- 检查是**单次、基于当前时刻观测**的：观测之后源或目标发生变化，结论不会自动更新。
- 界面只有英文，没有本地化框架；没有安装包、没有自动更新、没有崩溃遥测、没有 Explorer 集成。

更细的工程级边界（各规则的确切触发条件、证据分类、只读边界的证明范围等）写在
[`PRODUCT_SPEC.md`](PRODUCT_SPEC.md)、[`RULES.md`](RULES.md) 与 [`WORKFLOW_UI.md`](WORKFLOW_UI.md) 里，
这里只列用户需要知道的那些。

## 9. 支持范围与措辞

- **平台**：Windows **x64**。本包是 `win-x64` 的 self-contained 包，**不需要**目标机器预装 .NET 10 runtime。
- **操作系统版本仍然要落在 .NET 10 的支持范围内。** self-contained 只解决"运行时从哪里来"，
  不改变"这个操作系统版本是否被 .NET 10 支持"。以 Microsoft 当前的 .NET 10 支持矩阵为准
  （<https://github.com/dotnet/core/blob/main/release-notes/10.0/supported-os.md>）：矩阵中列出的 Windows
  客户端版本是 Windows 11 各版本，以及 Windows 10 的 Enterprise / IoT 长期服务版本
  （`10 1607 (E)`、`10 1809 (E)`、`10 21H2 (E)`、`10 21H2 (IoT)`）；矩阵同时写明，
  **操作系统发布方已经不再支持的版本，不由 .NET 测试或支持**。
- **本项目的实测环境是 Windows 10 x64（内部版本 10.0.19045，即 22H2）**，这是一个**不在**上述受支持列表里的
  OS 版本。因此：

> **本包在该项目环境上按预期启动与运行，但更广的 Windows 版本覆盖没有被独立认证。**
> *Initial release validated on the project Windows environment; broader Windows-version coverage has not
> been independently certified.*

- **不要**把这理解成"兼容所有 Windows 10 / 11"：本项目没有、也不能在没有多 OS 实测的情况下作出这种声明。
- 本机已经装有 .NET SDK，所以"在本机启动成功"**不等于**论证了"完全没有 .NET 环境的机器也能启动"（见 §11）。

## 10. 发布前检查清单

1. `git status` 干净，HEAD 是预期基线，`git remote -v` 为空（v0.1 不建 remote、不 push）。
2. `dotnet build CopyPreflight.sln -c Release` → 0 警告 / 0 错误。
3. `dotnet test CopyPreflight.sln -c Release` → 全部通过，0 失败、0 跳过。
4. 按 §2 从 clean 目录 publish；确认命令里 `-r win-x64`、`--self-contained true` 存在，
   `PublishSingleFile` / `PublishTrimmed` / `PublishAot` / `PublishReadyToRun` 都是 `false`。
5. 按 §4.3 审计发布内容：无源码 / 测试 / PDB / 日志 / 私密文件；数量与大小与本文件记录的同一量级。
6. 确认发布版 EXE 的 `FileVersion` 是 `0.1.0.0`、`ProductVersion` 是 `0.1.0`（§3）。
7. 从 **publish 目录**（不是 `bin\Debug` / `bin\Release` 的普通 build 产物）启动一次 EXE：
   窗口能创建、能正常关闭、退出码为 0（§11 记录了本次做法与证据边界）。
8. 用仓库内自有的小型夹具跑一次正常检查，以及一次"目标已存在同名文件"的检查（应当得到 Warning）。
9. 生成 ZIP 与 SHA-256（§5），确认解压结构与 EXE 哈希与 publish 一致。
10. 确认 ZIP / publish 输出没有被 `git status` 看到。

## 11. 证据边界（不要把其中一类说成另一类）

本次 M9 完成的发布版运行验证：

| 做了 | 结论 |
| --- | --- |
| 从 **publish 目录**启动 `CopyPreflight.App.exe` | 进程存活、主窗口句柄非 0、窗口标题为 `CopyPreflight — migration preflight check`、窗口保持响应，收到关闭请求后正常退出，**退出码 0** |
| 从 **ZIP 解压结果**启动 `CopyPreflight.App.exe` | 同上（退出码 0） |
| 用仓库内自有小型夹具（源 3 文件 + 1 子目录 / 空目标）跑真实检查 | 运行状态 `Completed`，四个阶段全部 `Complete`，无 finding |
| 用"目标已存在同名文件"的夹具跑真实检查 | 得到 1 条 **Warning**：`TARGET_EXISTING_FILE_COLLISION` |
| 检查前后对比夹具状态 | 目标目录没有任何新增 / 修改 / 删除：空目标仍为空，已有文件内容未被触碰 |

上面第 3、4 项是通过一个**临时驱动**调用**发布目录里的程序集**完成的（该驱动只存在于被忽略的
`artifacts/` 下，不属于仓库，也不参与构建）；驱动启动时会打印所加载程序集的 SHA-256，
实测与发布目录中的同名文件**逐字节一致**，因此这三项验证针对的是真正被打包的那份二进制。
界面上的**手工点击**（点击 Browse、敲路径、点 Start check、看列表滚动）仍然**未**在自动化环境中执行——
无法在没有 UI 自动化框架、也不允许安装的前提下模拟。**这一部分留给最终用户在独立环境完成**（§12）。

另外必须说清楚的两件事：

- **"在本机启动成功" ≠ "没有 .NET 的机器上也能启动"。** 本机装有 .NET SDK，因此无法用"能跑起来"证明
  self-contained 的效果。可以通过发布配置（`--self-contained true`）与输出审计（包内含 `coreclr.dll`、
  `hostfxr.dll`、`System.Private.CoreLib.dll` 等运行时文件，`runtimeconfig.json` 里是 `includedFrameworks`
  而不是 framework reference）证明**这个包是按 self-contained 模式生成的**——这就是本项目能给出的证据强度。
- **发布期的写与运行期的写是两件事。** 生成 publish 输出、staging 与 ZIP 当然会写磁盘，写的是仓库内的
  `artifacts/release/`，那是构建产物；这与"运行时向用户的 Source / Target 写东西"是完全不同的两件事，
  后者在本项目中依然是被禁止的（§6.3）。

## 12. 最终用户在独立环境的最短验收步骤

把 `CopyPreflight-v0.1.0-win-x64.zip` 复制到一台**没有本项目开发环境**的 Windows x64 机器上：

1. 解压 ZIP，进入 `CopyPreflight-0.1.0-win-x64\`，双击 `CopyPreflight.App.exe` → **窗口出现**。
2. Source 选一个普通文件夹，Target 选一个**空文件夹**，点 **Start check** →
   应当跑完；没有 Error 时应当看到 `No known blocking issue was detected.`，并且覆盖情况里没有"未执行"。
3. 制造一个 Warning：在 Target 里放一个与 Source 中**同名**的文件，再检查一次 →
   应当出现一条 **Warning**（目标已存在同名文件）。
4. 关闭窗口 → 程序**正常退出**，没有报错对话框；源与目标内容都**没有**被本工具改动过。

任何一步与上述不符，请记录：Windows 版本（`winver`）、第几步、界面上的原文，以及 ZIP 的 SHA-256。

## 13. 相关文档

- 产品定位、冻结原则、Severity 定义、v0.1 范围内与范围外：[`PRODUCT_SPEC.md`](PRODUCT_SPEC.md)
- 高层职责边界与依赖方向：[`ARCHITECTURE.md`](ARCHITECTURE.md)
- 各层自身契约：[`SCANNER.md`](SCANNER.md)、[`TARGET_PROBING.md`](TARGET_PROBING.md)、[`RULES.md`](RULES.md)
- 检查编排与界面行为、验证证据分类：[`WORKFLOW_UI.md`](WORKFLOW_UI.md)
- 仓库级工程规范：[`AGENTS.md`](../AGENTS.md)
