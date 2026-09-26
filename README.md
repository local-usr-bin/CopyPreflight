# CopyPreflight

**先体检，再搬家。**

CopyPreflight 是一个 Windows 文件迁移前的检查工具。在真正搬运数据之前，它只读扫描源目录，判断这些内容迁移到目标位置时可能遇到的兼容性、可行性和行为风险，并给出可解释的结论。

CopyPreflight **不是复制软件**。它不执行复制、不执行移动、不删除文件、不自动重命名、不修改权限、不格式化磁盘、不自动修复，也不向源目录或目标目录写入任何探测文件。

## 当前开发状态

| 项目 | 状态 |
| --- | --- |
| 当前里程碑 | **M9 — v0.1 Publish / Distribution / Release Readiness：已实现并提交，等待验收** |
| 上一个已验收基线 | **M8 — Manual-acceptance fixes and UI polish：已验收通过**（`4d546ffaab02379cc27685116066ad1037a03586`，559 测试通过） |
| 产品功能 | 只读的源目录扫描已可用（递归 inventory、文件 / 目录 / 逻辑字节统计、扫描 issue、进度、取消、reparse point 安全边界）；目标能力探测已可用，能报告目标文件系统名称、当前用户可用空间与单路径组件长度上限，无法确定的保持 Unknown；**规则层已可用**：源扫描失败与目标不可用会被报告为 Error，可用空间不足与 reparse point 会给出 Warning，FAT32 单文件上限、目标名称组件长度、传统 MAX_PATH 兼容性也已实现，未知目标能力会被说明为 Info；**Windows 命名兼容性规则**：非法名称字符与保留设备名是 Error，结尾空格 / 句点与仅大小写不同的兄弟名称碰撞是 Warning；**目标已存在对象 / 碰撞探测（M6）**：能只读观察每个映射位置当前已有什么，区分已存在对象 / 确证缺失 / 检查失败 / 未查询，并给出对应 findings |
| 检查编排（M7） | 已可用：按固定顺序调用真实的扫描器、目标能力探测、目标已存在对象探测与规则评估，把**运行状态 / 实际覆盖 / 风险**三个维度分开报告，支持取消、重复运行与阶段级进度 |
| 用户界面 | **已有可用的最小检查界面**（M7）：源 / 目标输入 + 浏览、开始检查、取消、阶段与已观察计数进度、findings 表格（按 Severity 稳定排序、UI 虚拟化）与详情面板。多源 / 批量、结果导出、历史对比仍未做 |
| 手工验收修正（M8） | 源 / 目标两个浏览对话框**状态互相独立**（各有自己的对话框标识与历史，起点只来自本框自己的路径，绝不从另一框推断）；源根不存在 / 不是目录时按 **source-side Error** 结构化报告，不再暴露内部异常类型，并与真正的程序失败严格区分；findings 表格列宽做了可读性调整。**未新增任何检测能力** |
| 发布形态（M9） | **已有可复现的 v0.1 发布形态**：Windows x64、self-contained 普通 publish 文件夹 + ZIP，目标机器**不需要**预装 .NET 10 runtime，可执行文件与产品程序集报告的版本是 **0.1.0**。**没有**新增检测能力，也没有 MSI / MSIX / ClickOnce / 安装器 / 单文件 / trimming / AOT / ReadyToRun / 代码签名 / 自动更新 / remote。用户使用说明、支持范围与已知限制见 [`docs/RELEASE.md`](docs/RELEASE.md) |

M2 加入的真实扫描逻辑**只存在于 Core**。M3 加入的目标能力探测与 Win32 interop、M4–M6 加入的规则评估、
M6 的已存在对象探测，以及 M7 加入的检查编排，**都在 `CopyPreflight.Windows`**（`net10.0-windows`），
Core 保持 `net10.0` 且不含 P/Invoke。全部严格只读：不打开文件、不读取文件内容、不向源目录或目标目录
写入任何内容，目标探测也**不做可写性测试**（不创建 probe 文件、不创建目录、不改 ACL）。
规则评估是**纯计算**：不重新扫描、不重新探测、不修改任何东西；M5 的名称规则、M6 的已存在对象规则
同样不 probe 目标的大小写敏感性、不做可写性测试。

- 扫描器契约（路径语义、统计、错误策略、reparse 边界、进度与取消）：[`docs/SCANNER.md`](docs/SCANNER.md)
- 目标能力探测契约（输入路径语义、Win32 API 与语义选择、部分成功、只读边界）：[`docs/TARGET_PROBING.md`](docs/TARGET_PROBING.md)
- 规则评估契约（已实现规则、Findings、Severity 判定、名称规则与为什么某些结论只能是 Warning）：[`docs/RULES.md`](docs/RULES.md)
- 检查编排与最小 WPF 界面契约（阶段顺序、三个维度、取消、进度、界面行为、限制）：[`docs/WORKFLOW_UI.md`](docs/WORKFLOW_UI.md)
- v0.1 的完整范围：[`docs/PRODUCT_SPEC.md`](docs/PRODUCT_SPEC.md)

## 技术栈

- **.NET 10**（SDK `10.0.401`，由 [`global.json`](global.json) 固定）
- **C#**
- **WPF** — 仅用于 `CopyPreflight.App` 的表现层
- **xUnit** — 测试
- **经典 `.sln`** 解决方案格式（非 `.slnx`）

依赖刻意保持最少。M0 不引入 DI 容器、日志框架、MVVM 框架、数据库、ORM、响应式框架、Web 框架、AI API、原生依赖包或打包框架。测试框架属于正常工程依赖，可以使用标准成熟模板。M7 接入 WPF 界面时同样没有引入上述任何依赖（`Microsoft.Win32.OpenFolderDialog` 属于 WPF 自带）。

## 目录结构

```text
CopyPreflight/
├─ CopyPreflight.sln        # 经典 .sln 解决方案
├─ global.json              # 固定 .NET 10 SDK 版本与 roll-forward 策略
├─ README.md
├─ AGENTS.md                # 仓库级工程规范（面向后续开发 Agent）
├─ .gitignore
├─ LICENSE                  # CopyPreflight 自有源码的 MIT 许可证
├─ THIRD-PARTY-NOTICES.txt  # 随发布包分发的第三方组件声明
├─ licenses/                # 随发布包分发的 .NET 运行时许可文本
├─ docs/
│   ├─ PRODUCT_SPEC.md      # 产品定位、原则与范围
│   ├─ ARCHITECTURE.md      # 高层职责边界
│   ├─ SCANNER.md           # 源扫描器契约（M2）
│   ├─ TARGET_PROBING.md    # 目标能力探测契约（M3，含 M6 已存在对象探测）
│   ├─ RULES.md             # 规则评估契约（M4/M5/M6）
│   └─ WORKFLOW_UI.md       # 检查编排与最小 WPF 界面契约（M7）
├─ src/
│   ├─ CopyPreflight.Core/  # 类库，不依赖 WPF，不依赖 App，不含 P/Invoke
│   │   ├─ Domain/          # 源对象事实模型
│   │   ├─ Capabilities/    # 已知 / 未知的目标能力
│   │   ├─ Findings/        # 规则层的结论单元
│   │   └─ Scanning/        # 源扫描器（M2）
│   ├─ CopyPreflight.Windows/ # Windows 目标能力探测、Win32 interop、规则评估与检查编排（M3–M7），引用 Core
│   │   ├─ Evaluation/      # 规则评估入口与各条规则（M4/M5/M6）
│   │   ├─ Probing/         # 目标已存在对象探测（M6）
│   │   └─ Workflow/        # 检查编排：请求、阶段、进度、结果（M7）
│   └─ CopyPreflight.App/   # WPF 应用，引用 Core 与 Windows
│       └─ Presentation/    # 检查界面的状态机、文案与结果行（M7）
└─ tests/
    └─ CopyPreflight.Tests/ # 测试项目，引用 Core、Windows 与 App
```

## 构建与测试

以下命令均可在仓库根目录直接执行（无需 IDE）：

```bash
# 还原
dotnet restore CopyPreflight.sln

# 构建
dotnet build CopyPreflight.sln

# 运行全部测试
dotnet test CopyPreflight.sln
```

`dotnet build` 会隐式执行 restore，因此最简流程是 `build` → `test`。

手动启动检查界面（用于人工确认窗口可启动并能完成一次检查）：控件级的绑定接线由带真实窗口的
`MainWindowBindingTests` 核查；拖动分隔条、点击浏览对话框、超大列表滚动手感仍**未**做人工验证。
M8 的三项修正（浏览对话框互不串位、源根不可用的 Error 文案、findings 表格列宽）由自动化测试覆盖其
接线与逻辑，但**最终仍需人工在界面上复核**（见 [`docs/WORKFLOW_UI.md`](docs/WORKFLOW_UI.md) §11）：

```bash
dotnet run --project src/CopyPreflight.App
```

## 打包发布

v0.1 的发布形态是 **Windows x64 / self-contained / 普通 publish 文件夹 + ZIP**（无需目标机器预装
.NET 10 runtime）。发布只针对 `CopyPreflight.App` 项目，不针对 solution：

```bash
dotnet publish src/CopyPreflight.App/CopyPreflight.App.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -p:PublishTrimmed=false -p:PublishAot=false -p:PublishReadyToRun=false -p:DebugType=None -p:DebugSymbols=false -o artifacts/release/v0.1.0/win-x64/publish
```

发布产物与 ZIP 都落在被忽略的 `artifacts/release/` 下，**不进入版本库**。完整的参数理由、输出审计、
ZIP 与 SHA-256 生成方式、发布前检查清单见 [`docs/RELEASE.md`](docs/RELEASE.md)。

## 文档

- [`docs/PRODUCT_SPEC.md`](docs/PRODUCT_SPEC.md) — 产品定位、冻结原则、Severity 定义、v0.1 范围内与范围外。
- [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md) — 扫描、目标能力、规则、findings、检查编排与 UI 的职责边界。
- [`docs/SCANNER.md`](docs/SCANNER.md) — 源扫描器契约：source root 与 relative path 语义、统计恒等式、
  scan issue 与错误策略、reparse point 边界、进度与取消。
- [`docs/TARGET_PROBING.md`](docs/TARGET_PROBING.md) — 目标能力探测契约：输入路径语义、三个 capability 的
  Known/Unknown 原则、部分成功语义、使用的 Win32 API 及语义选择、只读边界与 issue 模型。
- [`docs/RULES.md`](docs/RULES.md) — 规则评估契约：公开 API 与纯计算边界、FindingCode 清单、
  每条规则的触发条件与边界（FAT32 单文件上限、名称组件长度、传统 MAX_PATH、Windows 名称兼容性、
  大小写碰撞、reparse point、可用空间、目标已存在对象与碰撞）、Unknown 能力的 coverage Info、
  顺序与短路语义、为什么可用空间与大小写碰撞当前只能是 Warning。
- [`docs/WORKFLOW_UI.md`](docs/WORKFLOW_UI.md) — 检查编排与最小 WPF 界面契约：阶段顺序、运行状态 /
  覆盖 / 风险三个维度、取消与重复运行、迟到回调、进度与投递合并、窗口行为、结果展示、
  限制、验证证据分类与已知边界。
- [`docs/RELEASE.md`](docs/RELEASE.md) — v0.1.0 发布形态与分发：Windows x64 / self-contained /
  publish 文件夹 + ZIP、可复现的发布与打包命令、版本如何进入二进制、发布前检查清单、
  使用说明、支持范围、首版能力与明确未实现的部分。
- [`AGENTS.md`](AGENTS.md) — 后续开发需要遵守的仓库级工程规范。

## 许可证

**License: MIT**（见 [`LICENSE`](LICENSE)）。

MIT 只覆盖 CopyPreflight 的**自有源码**。发布包是 self-contained 的，包内随附的 .NET runtime 与
WPF / Windows Desktop runtime 组件**不属于**该许可证的覆盖范围，它们保留各自的许可证；相关声明与
官方许可文本见发布包内的 `THIRD-PARTY-NOTICES.txt` 与 `licenses/`，说明见 [`docs/RELEASE.md`](docs/RELEASE.md)。
