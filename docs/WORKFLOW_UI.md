# CopyPreflight — 检查编排与最小 WPF 界面（M7）

> 本文件记录 M7 交付的两样东西的**行为契约**：
> 把 M2–M6 各层串成一次检查的**编排层**（`src/CopyPreflight.Windows/Workflow/`），
> 以及把它接到窗口上的**最小呈现层**（`src/CopyPreflight.App/Presentation/` + `MainWindow`）。
> 它不改变任何既有规则、Finding 文本或 Severity 语义，也不改变
> [`PRODUCT_SPEC.md`](PRODUCT_SPEC.md) 的产品范围。
> 状态：**M7 已实现、已独立提交并已验收通过**（`84b3af3`）。
> 2026-09-22 验收反馈核实了三件事——界面绑定接线、进度投递的合并、取消识别与终态发布边界——
> 其中两处修正（`IsFindingsEmpty` 的通知、`OperationCanceledException` 的过滤条件）见本文 §4、§10.1，
> 以独立 corrective commit 提交。
> **M8（手工验收修正与界面可读性）追加 §11**：两个浏览对话框的状态互相独立、源根不可用按 source-side
> Error 报告、findings 表格列宽只是呈现。它**不新增任何检测能力**，也不改变 M1–M7 的任何契约。

## 1. 编排层回答什么

编排层只做一件事：**按固定顺序调用既有各层，并把结果如实报告出来**。它自己不含任何规则、
不做任何文件系统调用、不产出任何 `Finding`。

| 顺序 | 阶段（`PreflightRunPhase`） | 调用的真实实现 |
| --- | --- | --- |
| 1 | `ScanningSource` | `SourceScanner.Scan`（M2） |
| 2 | `ProbingTargetCapabilities` | `WindowsTargetCapabilityProbe.Probe`（M3） |
| 3 | `ProbingExistingObjects` | `WindowsTargetExistingObjectProbe.Probe`（M6） |
| 4 | `EvaluatingRules` | `WindowsPreflightEvaluator.Evaluate(source, target, existingObjects)`（**三参数重载**） |

- 每个阶段开始前先广播该阶段的进度快照，**阶段之间**检查一次取消。
- **在一个正常走到评估阶段的运行里**，M6 的已存在对象事实总是被取得、评估器总是拿到它；两参数重载不会被悄悄使用
  （用它会产出 `…CHECK_NOT_PERFORMED` Info，等于把"没查"当成"查过"）。
  取消或失败的运行在它当时所在的阶段停下，**其后的阶段根本没有执行**，结果里记为 `NotPerformed`——
  这不违反上面的约定，因为约定说的是"凡是走到评估的这次运行"，而不是"不论什么运行都会评估"。
- 这条路径上**没有**示例数据、没有替身、没有捷径：四个阶段都是生产实现。

## 2. 三个维度互不折叠

一次检查的结果（`PreflightRunResult`）同时给出三件不同的事，没有任何属性把三者合成一个布尔值：

| 维度 | 类型 | 取值 |
| --- | --- | --- |
| **运行状态** | `PreflightRunStatus` | `Completed` / `Cancelled` / `Failed` |
| **实际覆盖** | `PreflightCoverage`（每阶段 `PreflightPhaseCoverage`） | `NotPerformed` / `Complete` / `Incomplete` |
| **风险** | `IReadOnlyList<Finding>` | 逐条 `Severity` = Error / Warning / Info |

要点：

- **运行状态与风险无关**：一次 `Completed` 的运行完全可以带 Error findings（目标上已有异类对象是
  一个已经确定的冲突），而应用程序本身运转正常。
- **"覆盖完整"与"没有 findings"无关**：一次完整、零 findings 的运行也只支持
  `"No known blocking issue was detected."` 这一句结论。
- **未完成的运行不产出 findings**：`Cancelled` / `Failed` 只报告已确立的事实与未到达的阶段。
  构造函数据此拒绝自相矛盾的组合（已完成却没有 inventory / 未完成却带 findings / 失败却没有原因 等）。
- **`Incomplete` ≠ `NotPerformed`**：一次**已开始但未返回**的源观测记 `Incomplete`
  （它确实走过了一部分树，旁边的 "observed so far" 计数与它一致）；从未开始的阶段记 `NotPerformed`。

## 3. 输入与请求标识

`PreflightRequest.TryCreate(source, target, out request, out error)`：

- 两个路径**原样保留**——不 trim、不规范化、不重写、不折叠 `..`。
- 只做两项检查：非空、且 `Path.IsPathFullyQualified`（完全限定路径）。
  相对路径会被**拒绝**而不是按当前目录解析——那等于检查了用户没输入的东西。
- 返回前先报 source 再报 target 的错误。
- 每个请求带一个 `RequestId`（`Guid`），进度快照与结果都带着它。

**界面线程上不做任何文件系统存在性检查**：输入合法性只靠 `Path.IsPathFullyQualified`，
真正的存在性 / 可用性由 M2 / M3 在工作线程上确定，并作为事实或 issue 报告，不作为输入校验。

## 4. 线程、取消与重复运行

- **唯一一次线程跳转**：`PreflightWorkflow.RunAsync` 用一次 `Task.Run` 把同步内核挪出调用线程；
  `RunAsync` **不**把 token 交给线程池（已取消的 token 必须返回 `Cancelled` 结果，而不是抛在启动前）。
  全程没有 `.Wait()` / `.Result`，没有 `async void`（WPF 事件边界除外）。
- **每次运行一个 `CancellationTokenSource`**，运行结束即释放。
- **取消是诚实的**：
  - 只有**同时**满足两件事的 `OperationCanceledException` 才算用户取消：
    `token.IsCancellationRequested` 为真，且 `exception.CancellationToken == token`。
    只比较 token 是不够的——一个阶段可以抛出一个"带着本轮 token"的异常而根本没有谁请求过取消，
    那必须按 **`Failed`** 处理，而不是伪装成用户的停止请求。
  - 其他 token 的取消同样按 **`Failed`** 处理。
  - 取消后**绝不**进入下一阶段，**绝不**重新观测来"补齐"。
  - 未完成的运行**不产出 findings**。
- **终态只由返回的状态决定**：界面显示的终态来自 `PreflightRunResult.Status`，不是"界面当时以为自己在做什么"。
  运行真正结束之后到达的取消请求不会改写已经发布的终态；而界面处于 `Cancelling` 时收到的结果若确实是
  `Completed`（取消来得太晚，评估已经跑完），界面就发布 `Completed`——因为那次评估确实产出了，
  把它显示成"已取消"才是失实。
  呈现层**没有**第二道取消判断：把取消变成 `Cancelled` 结论的**唯一**位置是编排层那个双条件 catch 过滤器，
  结果本身就是"这一轮究竟建立了什么"的权威。若在这里再判一次取消，等于用界面状态去推翻已经得到的底层事实，
  并且会把已经推导出来的 findings 从屏幕上抹掉（未完成的运行按模型**不允许**带 findings）。
  这段精确时序（后台已返回、结果尚未发布时取消）由
  `CheckViewModelTests.CancelCheck_AfterTheRunFinishedButBeforeItsResultIsShown_StillShowsTheRunThatFinished` 钉住。
- **重复运行**：编排层无状态，可复用；一次失败或取消后可以立即再跑，
  新运行拿到新的 `RequestId` 和新的 token source，不受前一次影响。

## 5. 迟到的回调与状态污染

- 进度快照携带 `RequestId`。视图模型只接受**当前运行**的快照，且只在该运行仍处于忙碌状态时接受。
- 阶段只能**前进**：一个乱序到达的、阶段更早的快照不会把界面显示的阶段倒退回去。
- 运行结束后到达的快照**不会**把 `Completed` / `Cancelled` / `Failed` 改回 `Running`。
- 最终计数与 findings 一律取自**返回的结果**，不取自"最后到达的那个快照"。
- 运行结束后修改任一路径输入 → 结果被丢弃、界面回到未检查状态（结果永远不会停留在它不是为之产生的路径下）。

## 6. 进度

- 显示**当前阶段** + 已观察的**文件数 / 目录数 / reparse point 数 / 逻辑字节数**（+ issue 数）。
- 进度条是**不确定态**（indeterminate）：没有百分比、没有 ETA、没有速度预测——
  扫描器不做预扫描计数，因此任何百分比都是伪造的。
- 运行中的数字明确标注为"observed so far"（部分观测），运行结束后的数字才是清单。
- M2 扫描器在 M7 增加了一条**报告频率**补充（单个大目录内每积累固定条数报告一次），
  见 [`SCANNER.md`](SCANNER.md) §7。它只影响快照的**出现频率**，不改变统计、issue、reparse 语义。

## 7. 窗口行为

| 行为 | 约定 |
| --- | --- |
| 源 / 目标输入 | 可编辑文本框；`Browse…` 使用 `Microsoft.Win32.OpenFolderDialog`（单选、带窗口拥有者、取消则保留原输入） |
| 开始检查 | 忙碌期间禁用；忙碌时再次点击不会叠起第二次运行 |
| 取消 | 仅从 `Running` 生效；立刻显示 `Cancelling` 并保持忙碌，直到运行**真正结束** |
| 关闭窗口 | 忙碌时**拒绝**普通关闭、请求取消、界面保持响应；运行真正结束后再次关闭才成功。不做托盘、不强制退出、`Closing` 里不同步等待 |
| 映射说明 | 界面上写明 `source\<relative-path> → target\<relative-path>` |
| 只读声明 | 界面上明确写出不复制 / 不移动 / 不删除 / 不重命名 / 不改权限 / 不写 probe 文件 |
| 长路径 | 输入**不被静默截断或重写**；超过 260 字符的路径不会被界面阻挡 |

## 8. 结果展示

- `DataGrid` 列出 findings：`Severity` / `Code` / `Title` / `SourceRelativePath`。
- **启用 UI 虚拟化**（`VirtualizationMode = Recycling`），滚动区域有上限高度，行数很大时界面不被拖垮。
- 排序稳定为 **Error → Warning → Info**，且**不依赖 enum 的数值顺序**（显式排序键）。
- 详情面板显示**选中的** finding 的 Explanation / Impact / Recommendation；
  完整路径放在**只读但可选中复制**的文本框中（不因长路径被截断而不能复制）。
- **无 relative path 的 finding 按归属标注，绝不互相冒用**：目标级（如 `TARGET_PATH_UNAVAILABLE`）标注为
  "Target-wide (not tied to a single source object)"；源自身的问题（`SOURCE_ROOT_UNUSABLE`、
  `SOURCE_DIRECTORY_ENUMERATION_FAILED`）标注为 "Source-wide (not tied to a single source entry)"。
  两者都**绝不**伪造一个相对路径。
- 不做合并、丢弃或重新分级：一条 finding 一行。
- 结论区：只有 `Completed` 且 Error 计数为 0 时才显示冻结语句
  `"No known blocking issue was detected."`；`Cancelled` / `Failed` / 运行中 / 未检查各显示各自的文本。
  覆盖缺口与结论**并列**显示，不会被结论掩盖。

## 9. 运行命令

```bash
# 构建
dotnet build CopyPreflight.sln

# 全部测试
dotnet test CopyPreflight.sln

# 手动启动界面（仅人工确认，不参与自动化测试）
dotnet run --project src/CopyPreflight.App
```

界面产物：`src/CopyPreflight.App/bin/Debug/net10.0-windows/CopyPreflight.App.exe`（WPF 可执行）。

## 10. 限制与边界

- **仍然不写任何东西**：编排层与界面都不会向源或目标写入 probe / 临时文件，
  不复制、不移动、不删除、不重命名、不改权限、不读文件内容、不计算 hash。
- **"目标可写"永远不被声称**：可写性没有、也不会通过写操作验证。
- **只读原则不变**：界面上的"未发现阻塞问题"仍然只是
  `"No known blocking issue was detected."`，不是任何形式的保证。
- 没有 UI：多源 / 批量检查、结果导出、历史对比、结果过滤与分组（只有按 Severity 的稳定排序展示）。
- 没有引入 DI 容器、事件总线、插件系统、工作流引擎、新的运行时项目或第三方依赖；
  呈现层没有为每个方法定义接口。
- 未做人工验证的部分（无法在无交互环境中确认的）在 M7 完成报告中**明确列出**，
  不声称已验证。

### 10.1 验证证据的分类（不要把其中一类说成另一类）

| 类别 | 能说明什么 | 不能说明什么 |
| --- | --- | --- |
| 启动冒烟（进程存活、主窗口句柄非 0、空闲关窗被接受） | 程序能起来、XAML 能解析、窗口能创建 | 任何一次真实控件交互 |
| 静态接线检查（读 `MainWindow.xaml` / code-behind / ViewModel） | 标记与方法的对应关系存在 | 运行起来真的接了、绑定真的收到通知 |
| 呈现层测试（无窗口的状态机断言） | 状态机规则本身 | XAML 绑定与通知链 |
| 带窗口的绑定核查（本仓库 `MainWindowBindingTests`，STA + Dispatcher） | 真实标记里的绑定确实解析、通知确实到达、`Click` 确实接到 code-behind、`Closing` 确实接到拒绝逻辑 | 人工操作手感（拖动分隔条、实际点击浏览、超大列表滚动） |
| 真实文件系统集成测试 | 真实 M2/M3/M6/规则链在真实目录上的行为 | 真实用户目录上的端到端结论 |
| 人工窗口操作 | 手感与真实交互 | 不适用于无人值守环境 |

- **对只读边界的证据边界**：源码中**没有**写入调用（`File.*` 写族 / `Directory.Create` / `Directory.Delete` /
  `OpenWrite` / `CreateFileW` / `WriteAll*` 等）的 grep 结果，以及"有限状态对比"（在最小范围的固定场景里
  对比目标目录前后状态），都只是**证据**，**不是**系统级的"零写入证明"。真正的零写入证明需要内核级审计，
  本项目不声称有。
- 测试夹具一律创建在仓库自己的 `artifacts/test-fixtures` 下，只操作这些测试自己拥有的资源；
  **不使用**真实用户目录，也不回退到系统临时目录。

## 11. M8 追加：手工验收修正与界面可读性

M8 **不是**新的检测能力里程碑。它只修三件在 M7 人工验收中被观察到的事，并做最小的界面可读性调整；
不新增任何规则、不新增导出 / 安装器 / 本地化。

### 11.1 两个浏览对话框互相独立（问题 A）

- 症状：两个框都为空时先浏览 source、再浏览 target，target 对话框会打开在刚选中的 source 目录里。
- **明确否决**的做法："target 为空就用 source 的父目录 / 从 source 推导 target"——那会让一个框的用途去
  决定另一个框的位置。source 浏览与 target 浏览是两个**互相独立**的用途。
- 采用的机制（都用 `Microsoft.Win32.OpenFolderDialog` 自带的能力，**不自建**历史 / 注册表 / JSON 路径记录）：
  - **`ClientGuid`**：source 与 target 各用一个**源码里固定的、互不相同**的 GUID 常量
    （`FolderPicker.SourceDialogId` / `TargetDialogId`）。Windows 以标识为键分别记忆"对话框尺寸 + 上次使用的文件夹"，
    因此两个框各有一份**独立**历史，谁也不能把对方拖到自己没去过的目录。
    标识是**常量**而非每次运行随机生成：随机标识不携带任何历史，而共用一个标识正是被替换掉的耦合；
    一旦发布**不得更换**（换了就等于静默丢弃用户已经习惯的位置）。
  - **`InitialDirectory`**：只有当**本框自己**已经持有完全限定路径时才设置；否则不设置，
    交由对话框自己记忆的位置，再退回到平台默认（**不**冻结"此电脑 / 桌面 / 某个盘"）。
- **浏览起点 ≠ 自动选择**：浏览**开始**不改动任何输入框；只有用户在对话框里确认后，才写回**本框自己**。
  取消对话框不写回任何东西（路径、已有结论、findings、详情选中项全部保持原样）。
- 呈现层**不做文件系统存在性判断**：起点只按 `Path.IsPathFullyQualified` 判断是否可用
  （与请求自身的输入规则一致）；对话框遇到用不了的路径时保持它自己记忆的位置。
- **没有**新增业务规则：用户仍可以让 source 与 target 相同或嵌套。
- 决策逻辑被抽成可测的 `FolderPicker`（标识 / 标题 / 起点），窗口只负责设置属性并写回答案；
  `MainWindow.PickFolder` 是一个可替换的细接缝，使"哪个框、允许从哪开始、取消不改动"能在
  **不驱动模态对话框**的前提下被断言。

### 11.2 源根不可用不再伪装成程序崩溃（问题 B）

- 症状：源指向一个不存在的目录时，界面显示 "The check failed to run to completion." 连同原始
  `CopyPreflight.Core.InvalidSourceRootException ...`，并伴随"源观测：未执行 / 目标能力：未执行"。
- 新行为：源根不可用是**输入的可预期结果**，按 **source-side Error** 报告，而不是应用程序失败。
  实现**复用**既有的"源事实 → 规则层定 Severity → finding"链路，**不绕过**规则系统：
  `PreflightWorkflow.Run` 只捕获**那一个**前置失败，把它转成 `SourceScanResult.ForUnusableRoot`，
  再由新增的 source-only 规则 `SOURCE_ROOT_UNUSABLE`（**Error**）翻译成 finding——
  见 [`RULES.md`](RULES.md) §4.13 与 [`SCANNER.md`](SCANNER.md) §6.2。
- **可预期的源侧条件 vs 真正的程序缺陷**：
  - 只有 `InvalidSourceRootException`（根不存在 / 根不是目录）走这条路，运行状态仍是 **`Completed`**。
  - 其他任何异常（含"带着本轮 token 却从未被请求取消"的 `OperationCanceledException`）**仍然**让运行进入
    **`Failed`**——没有 `catch (Exception)` 把所有异常都吞成 finding。
- **绝不**因为一次拒绝而：显示 `"No known blocking issue was detected."`、伪造完整源清单、用 `0` 填 Unknown、
  发起新的文件系统 I/O、自动创建源目录、自动改写路径。
- **绝不**出现 `InvalidSourceRootException` / `DirectoryNotFoundException` / 堆栈 / 命名空间 / 内部类型名；
  用户可见文本只在观测支持时才说 "source directory does not exist" / "source path is not a directory"。
- **不以** `Directory.Exists(source) == false` 当统一判据（该 API 在权限不足等错误下同样返回 `false`）；
  权限 / 枚举失败保持它本来的类别（`SOURCE_DIRECTORY_ENUMERATION_FAILED`，Error）与 `Unknown` 边界，
  **不**被改写成"不存在"。
- 源根不可用时**仍然**取得目标能力与目标已存在对象事实（与"目标被拒绝"时的处理一致），
  但所有依赖源清单的规则拿到**空 entry 列表**、因此**什么也不产出**（不是被跳过，也不是被塞进伪造条目）。
- 界面据此把"从未观测"与"观测到 0"分开：计数区显示 "not observed" 而不是 `: 0`，并说明源根不可用。

### 11.3 findings 表格的列宽（问题 C）

- 只用标准 `DataGrid` 尺寸机制（固定像素 + `MinWidth` + `Star` + 用户可拖动），**没有**主题 / 图标 / 配色框架。
- `Severity` 紧凑（`88` / `MinWidth 72`），`Code` 保持可见但不再霸占空间（`200` / `MinWidth 120`，过长用省略号），
  `Title` 取剩余宽度（`Star` / `MinWidth 200`，默认最宽），`Path` 保留可辨认空间（`190` / `MinWidth 120`，过长用省略号）。
- 列集合、顺序、一条 finding 一行、Severity 稳定排序、UI 虚拟化、详情面板与 `GridSplitter` **都没有变**；
  不隐藏任何 finding，也不为了宽度去改 Finding 的 Title / Code。
- 不要求像素级精确：正常窗口宽度与收窄后都应可读。这是**呈现**调整，不是检测语义。

## 12. 相关文档

- 职责边界与依赖方向：[`ARCHITECTURE.md`](ARCHITECTURE.md) §1.5、§3、§4.7
- 产品范围与冻结原则：[`PRODUCT_SPEC.md`](PRODUCT_SPEC.md)
- 各层自身契约：[`SCANNER.md`](SCANNER.md)、[`TARGET_PROBING.md`](TARGET_PROBING.md)、[`RULES.md`](RULES.md)
- 仓库工程规范：[`AGENTS.md`](../AGENTS.md)
