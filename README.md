# CopyPreflight

**先体检，再搬家。**

CopyPreflight 是一个 Windows 文件迁移前的检查工具。在真正搬运数据之前，它只读扫描源目录，判断这些内容迁移到目标位置时可能遇到的兼容性、可行性和行为风险，并给出可解释的结论。

CopyPreflight **不是复制软件**。它不执行复制、不执行移动、不删除文件、不自动重命名、不修改权限、不格式化磁盘、不自动修复，也不向源目录或目标目录写入任何探测文件。

## 当前开发状态

| 项目 | 状态 |
| --- | --- |
| 当前里程碑 | **M2 — Source scanner baseline：已完成** |
| 产品功能 | 只读的源目录扫描已可用：递归 inventory、文件 / 目录 / 逻辑字节统计、扫描 issue、进度、取消、reparse point 安全边界。尚未实现目标能力探测与规则判定，因此还不会产生 findings |
| 用户界面 | 仅有一个可启动的占位窗口；扫描器尚未接入 UI，最终 UI 也尚未设计 |

M2 加入的真实扫描逻辑**只存在于 Core**，且严格只读：它不打开任何文件、不读取文件内容、
不向源目录或目标目录写入任何内容，也不做任何探测。规则判定、目标能力探测与 Win32 调用**尚未实现**。

- 扫描器契约（路径语义、统计、错误策略、reparse 边界、进度与取消）：[`docs/SCANNER.md`](docs/SCANNER.md)
- v0.1 的完整范围：[`docs/PRODUCT_SPEC.md`](docs/PRODUCT_SPEC.md)

## 技术栈

- **.NET 10**（SDK `10.0.401`，由 [`global.json`](global.json) 固定）
- **C#**
- **WPF** — 仅用于 `CopyPreflight.App` 的表现层
- **xUnit** — 测试
- **经典 `.sln`** 解决方案格式（非 `.slnx`）

依赖刻意保持最少。M0 不引入 DI 容器、日志框架、MVVM 框架、数据库、ORM、响应式框架、Web 框架、AI API、原生依赖包或打包框架。测试框架属于正常工程依赖，可以使用标准成熟模板。

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
│   └─ SCANNER.md           # 源扫描器契约（M2）
├─ src/
│   ├─ CopyPreflight.Core/  # 类库，不依赖 WPF，不依赖 App
│   │   ├─ Domain/          # 源对象事实模型
│   │   ├─ Capabilities/    # 已知 / 未知的目标能力
│   │   ├─ Findings/        # 规则层的结论单元（M2 尚未产生）
│   │   └─ Scanning/        # 源扫描器（M2）
│   └─ CopyPreflight.App/   # WPF 应用，引用 Core
└─ tests/
    └─ CopyPreflight.Tests/ # 测试项目，引用 Core
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

手动启动 UI（仅用于人工确认窗口可启动，不参与自动化测试）：

```bash
dotnet run --project src/CopyPreflight.App
```

## 文档

- [`docs/PRODUCT_SPEC.md`](docs/PRODUCT_SPEC.md) — 产品定位、冻结原则、Severity 定义、v0.1 范围内与范围外。
- [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md) — 扫描、目标能力、规则、findings 与 UI 的职责边界。
- [`docs/SCANNER.md`](docs/SCANNER.md) — 源扫描器契约：source root 与 relative path 语义、统计恒等式、
  scan issue 与错误策略、reparse point 边界、进度与取消。
- [`AGENTS.md`](AGENTS.md) — 后续开发需要遵守的仓库级工程规范。
