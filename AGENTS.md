# AGENTS.md — 仓库级工程规范

面向在本仓库工作的开发 Agent。这里只记录**代码库层面**的事实与约定；
更高层的项目级约束（例如只读原则、范围冻结、需要停下来向用户报告的情形）以项目指令为准，本文不重复。

## 1. 环境前提

- 目标平台：**Windows 10 x64**。
- .NET SDK 版本由 [`global.json`](global.json) 固定：`10.0.401`，`rollForward: latestFeature`，`allowPrerelease: false`。
  - 不要擅自改动 `global.json` 中的 SDK 版本或 roll-forward 策略。
- `dotnet` 与 `git` 预期已在系统 PATH 中。
  - 若某个 Agent 会话的 shell PATH 被精简、找不到这两个命令，按其标准安装位置调用即可，**不要修改系统 PATH**：
    - `C:\Program Files\dotnet\dotnet.exe`
    - `C:\Program Files\Git\cmd\git.exe`

## 2. 项目职责与依赖方向

| 项目 | 类型 | 职责 | 依赖 |
| --- | --- | --- | --- |
| `src/CopyPreflight.Core` | 类库 (`net10.0`) | 扫描、目标能力、规则、findings（见 `docs/ARCHITECTURE.md`） | 无 |
| `src/CopyPreflight.App` | WPF (`net10.0-windows`) | 表现层与用户交互 | → Core |
| `tests/CopyPreflight.Tests` | xUnit (`net10.0`) | 测试 | → Core |

**硬约束：**

- `CopyPreflight.Core` **不得**依赖 WPF，**不得**引用 `CopyPreflight.App`。
- 依赖方向单向指向 Core。不要把 UI 相关类型泄漏进 Core。
- 不要为了形式上的分层而新增程序集。当前三个项目是刻意的下限，不是需要被拆分的上限。

## 3. 构建与测试

在仓库根目录执行：

```bash
dotnet restore CopyPreflight.sln
dotnet build CopyPreflight.sln
dotnet test CopyPreflight.sln
```

约定：

- 解决方案使用**经典 `.sln`**。`dotnet new sln` 在 .NET 10 默认生成 `.slnx`，新增项目请显式使用 `-f sln`。
- 新增/删除项目后，必须同步 `CopyPreflight.sln` 并重新完整 build + test。
- 测试必须能在**无人值守**下运行通过（不允许需要人工点击的步骤）。
- 一个里程碑完成前，必须运行完整 build 与 test，并检查 `git status` 与改动范围。

## 4. 编码约定

- `Nullable` 与 `ImplicitUsings` 在所有项目中均已启用，新代码应保持开启。
- 使用**文件范围命名空间**（`namespace Foo;`）。
- 命名空间与程序集同名（`CopyPreflight.Core`、`CopyPreflight.App`、`CopyPreflight.Tests`）。
- 新增公开类型应有 XML 文档注释说明其职责；尤其是临时/占位类型，必须说明它为何存在、何时应被移除。
- 源文件使用 UTF-8。中文文档与注释是允许且常见的。
- 优先写可测试的纯逻辑；把 I/O 与平台调用隔离在明确边界之后。

## 5. 依赖政策

默认**不新增依赖**。当前明确不引入：DI 容器、日志框架、MVVM 框架、数据库、ORM、响应式框架、
Web 框架、AI API、原生依赖包、打包框架。

- 测试框架属于正常工程依赖，可使用标准成熟模板。
- 需要新增任何依赖前，先说明必要性并取得确认；不要静默添加。

## 6. 产品边界（写代码时最容易违反的部分）

以下行为在本仓库中**一律禁止**，包括以"验证能力"为名义的实现：

- 不复制、不移动、不删除、不重命名
- 不修改权限、不格式化
- **不向源目录或目标目录写入任何 probe / 临时文件**

无法确定的目标能力必须表达为 **Unknown**，不得猜测。
"扫描通过"只意味着 "No known blocking issue was detected."，不得表述为任何形式的保证。
完整定义见 [`docs/PRODUCT_SPEC.md`](docs/PRODUCT_SPEC.md)。

## 7. 面对不确定的 Windows 行为

- **不猜测**。先做**最小范围验证**，确认路线后再正式实现。
- 避免无控制的大规模试错。同一技术问题如果已经完整尝试两条明显不同的路线仍失败，
  应停止并报告：当前问题、已尝试路线、失败证据、最可能原因、建议下一步。

## 8. 里程碑纪律

- 不要实现尚未进入当前里程碑的功能，不要提前开始下一个里程碑。
- 不要擅自扩大产品范围（v0.1 的范围内外清单见 `docs/PRODUCT_SPEC.md`）。
- 改动产品范围时，必须同步更新 `docs/PRODUCT_SPEC.md`；改动职责边界时，同步更新 `docs/ARCHITECTURE.md`。

## 9. 提交约定

- 小步、职责单一的提交。提交信息使用英文祈使句，例如：
  `Initialize CopyPreflight project baseline`
- 确认 `bin/`、`obj/` 等构建产物不进入版本库（由 `.gitignore` 保证，但仍需在 `git status` 中确认）。
- 提交前确认没有无关改动、没有改动工作目录之外的内容。
- 未经明确要求，**不创建 remote、不 push**。
