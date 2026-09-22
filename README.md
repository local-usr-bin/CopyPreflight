# CopyPreflight

**先体检，再搬家。**

CopyPreflight 是一个 Windows 文件迁移前的检查工具。在真正搬运数据之前，它只读扫描源目录，判断这些内容迁移到目标位置时可能遇到的兼容性、可行性和行为风险，并给出可解释的结论。

CopyPreflight **不是复制软件**。它不执行复制、不执行移动、不删除文件、不自动重命名、不修改权限、不格式化磁盘、不自动修复，也不向源目录或目标目录写入任何探测文件。

## 当前开发状态

| 项目 | 状态 |
| --- | --- |
| 当前里程碑 | **M7 — Preflight workflow and minimal WPF integration：已实现并提交，等待验收** |
| 上一个已验收基线 | **M6 — Target existing-object / collision：已验收通过**（`6da3e249`，422 测试通过） |
| 产品功能 | 只读的源目录扫描已可用（递归 inventory、文件 / 目录 / 逻辑字节统计、扫描 issue、进度、取消、reparse point 安全边界）；目标能力探测已可用，能报告目标文件系统名称、当前用户可用空间与单路径组件长度上限，无法确定的保持 Unknown；**规则层已可用**：源扫描失败与目标不可用会被报告为 Error，可用空间不足与 reparse point 会给出 Warning，FAT32 单文件上限、目标名称组件长度、传统 MAX_PATH 兼容性也已实现，未知目标能力会被说明为 Info；**Windows 命名兼容性规则**：非法名称字符与保留设备名是 Error，结尾空格 / 句点与仅大小写不同的兄弟名称碰撞是 Warning；**目标已存在对象 / 碰撞探测（M6）**：能只读观察每个映射位置当前已有什么，区分已存在对象 / 确证缺失 / 检查失败 / 未查询，并给出对应 findings |
| 检查编排（M7） | 已可用：按固定顺序调用真实的扫描器、目标能力探测、目标已存在对象探测与规则评估，把**运行状态 / 实际覆盖 / 风险**三个维度分开报告，支持取消、重复运行与阶段级进度 |
| 用户界面 | **已有可用的最小检查界面**（M7）：源 / 目标输入 + 浏览、开始检查、取消、阶段与已观察计数进度、findings 表格（按 Severity 稳定排序、UI 虚拟化）与详情面板。多源 / 批量、结果导出、历史对比仍未做 |

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
`MainWindowBindingTests` 核查；拖动分隔条、点击浏览对话框、超大列表滚动手感仍**未**做人工验证：

```bash
dotnet run --project src/CopyPreflight.App
```

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
- [`AGENTS.md`](AGENTS.md) — 后续开发需要遵守的仓库级工程规范。
