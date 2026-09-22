# CopyPreflight — 架构

> **状态：仅记录高层职责边界。**
> M0 阶段**不**提前把未来的所有 class、interface 和 API 设计死。本文件描述的是"谁负责回答什么问题"，
> 而不是具体的类型与签名。具体设计在对应里程碑中随需求确定。

## 1. 分层职责

CopyPreflight 由五个职责构成。它们的边界是清晰的，且可以独立思考和测试。

### 1.1 source inventory / scanner

**负责观察源数据事实。**

扫描源目录，产出一份关于"源是什么样"的只读描述：文件与目录结构、容量、路径、可读性、无法枚举的目录、reparse point 及其分类等。

它**不判断**这些事实在目标上意味着什么。它只回答"我观察到了什么"。

### 1.2 target capability discovery

**负责描述目标已知能力和 Unknown。**

它描述目标位置在文件系统层面**已知**能做什么、不支持什么，以及哪些方面**无法确定**。

无法确定的能力必须明确表达为 **Unknown**，而不是被省略或被猜测。它**不读取源内容**，也**不评估**具体的源对象。

M6 起，这一层还包含一个并列的**只读**子职责（见 §4.6）：观察**每个源对象映射到的目标位置上当前已有什么**
（已存在对象 / 确证缺失 / 检查失败 / 未查询）。它同样只观察、不判断、不产出 `Finding`，也不写入任何东西。

### 1.3 rule evaluation

**负责根据 source facts + target capabilities 产生 findings。**

规则层是唯一把"源的事实"和"目标的能力"组合起来判断的地方。每条规则消费两侧的输入，产出一条或多条 findings。

规则必须是**确定性的**：相同输入产生相同输出，不依赖隐藏状态。

### 1.4 findings

**表示问题、severity、原因、影响、建议。**

finding 是规则层的输出，是可解释的结论单元，至少承载：

- 问题是什么
- Severity（Error / Warning / Info，定义见 [`PRODUCT_SPEC.md`](PRODUCT_SPEC.md)）
- 原因
- 影响
- 建议（由确定规则生成的文字建议）

finding 是**描述性的**，它不触发任何变更操作。

### 1.5 WPF presentation

**负责用户交互和结果展示。**

它收集用户的输入（源目录、目标目录），驱动扫描过程（包括进度与取消），并展示 findings。

它**不包含**判断逻辑：UI 不做规则判定，只呈现 Core 给出的结果。

M7 起，这一职责被明确拆成两半：**编排**（按固定顺序调用 scanner / 目标探测 / 评估器，并把"运行状态 /
覆盖情况 / 风险"三个维度分开表达，见 §4.7）放在 `CopyPreflight.Windows` 的 `Workflow/`，与
`WindowsPreflightEvaluator` 同属一个程序集；**呈现**（状态机、绑定与文案）放在 `CopyPreflight.App` 的
`Presentation/`。呈现层不含任何规则判定，也不自己调用 Win32；它只把编排层的产物翻译成窗口可绑定的文本与行。

## 2. 三个核心问题

各层之间的分工可以用三个问题概括：

| 层 | 回答的问题 |
| --- | --- |
| Scanner | **"我观察到了什么？"** |
| Target capability layer | **"目标已知能做什么？"** |
| Target existing-object layer（M6） | **"目标位置上当前已有什么？"** |
| Rules | **"二者组合意味着什么？"** |

这三句话是判断某段代码该放在哪里的第一准则。

## 3. 依赖方向

当前工程基线为：

- `CopyPreflight.Core` —— 承载 1.1–1.4 的职责，**不依赖 WPF**，**不依赖** `CopyPreflight.App` 或
  `CopyPreflight.Windows`；保持 `net10.0`，不含 Win32 P/Invoke。
- `CopyPreflight.Windows` —— 承载 1.2 的 Windows 具体实现（Win32 interop、Windows 卷 / 目标发现、
  M6 的已存在对象探测）、1.3 的规则评估（见 §4.4），以及 M7 的检查编排（见 §4.7），
  引用 `CopyPreflight.Core`。平台限定为 `net10.0-windows`。
- `CopyPreflight.App` —— 承载 1.5，引用 `CopyPreflight.Core` 与 `CopyPreflight.Windows`（编排层在这里）。
- `CopyPreflight.Tests` —— 引用 `CopyPreflight.Core`、`CopyPreflight.Windows` 与 `CopyPreflight.App`。

依赖方向单向指向 Core。Core 保持与 UI 技术和平台无关，从而使扫描与领域模型可以在没有 UI、
也不需要 Win32 的情况下被思考与测试。

> M0 只固定了"Core 不依赖 UI"这一条方向性约束。M1 进一步确定了最小核心领域模型的类型与语义（见 §4.1），
> M2 确定了源扫描器的公开契约与结果模型（见 §4.2），M3 确定了目标能力探测的 Windows 实现与契约（见 §4.3），
> M4 确定了规则评估的入口与已实现规则（见 §4.4），M5 在其中加入了 Windows 命名兼容性规则（见 §4.5），
> M6 加入了目标已存在对象 / 碰撞的只读探测与对应规则（见 §4.6），M7 加入了检查编排与最小 WPF 接入（见 §4.7）。
> 依赖方向自始至终未变：`App → Windows → Core`。

## 4. 本阶段已确定 / 未确定的事

### 4.1 M1 已确定：最小核心领域模型

M1 建立了后续 scanner、target probing 与 rules 共用的最小领域模型。所有类型都位于 `CopyPreflight.Core`
程序集；`Findings/`、`Capabilities/`、`Domain/` 目录只用于组织文件，命名空间仍与程序集同名。

| 职责 | 类型 | 语义 |
| --- | --- | --- |
| 1.4 findings | `Finding`、`FindingCode`、`Severity` | 可解释的结论单元：code / severity / title / explanation / impact / recommendation / 可选 source relative path |
| 1.2 target capability discovery | `CapabilityValue<T>`、`FileSystemName`、`TargetCapabilities` | 能力要么「已知」要么「显式 Unknown」；Unknown 不得退化成 `default(T)`、`false`、`0` 或空字符串 |
| 1.1 source inventory | `SourceEntry`、`SourceEntryKind` | 最小事实模型：relative path、kind，以及文件的逻辑长度（目录不伪造长度） |

这些类型都是**纯数据模型**：不做文件系统 I/O、不调用 Win32、不依赖 WPF、不持有 UI 类型。

`FileSystemName` 采用**开放**表达（规范化字符串 + 大小写不敏感比较），因为文件系统名称不是封闭集合。

### 4.2 M2 已确定：源扫描器契约

M2 实现了 §1.1 的第一版真实扫描器。它仍然是纯观察层：只读、不判断、不探测目标。

| 职责 | 类型 | 语义 |
| --- | --- | --- |
| 1.1 source inventory | `SourceScanner` | 接受 source root，递归观察出 inventory、issue 与统计；同步、可取消、可报告进度 |
| 1.1 source inventory | `SourceScanResult` | 观察结果：entries、issues，以及**由 entries 推导**的文件 / 目录 / reparse point 计数与逻辑字节数 |
| 1.1 source inventory | `SourceScanProgress` | 单调、非负的进度快照；只含已观察计数与当前相对路径，无百分比 / ETA |
| 1.1 source inventory | `SourceScanIssue`、`SourceScanIssueKind` | **观察失败的事实**，与 `Finding` 分开：无 Severity、无建议 |
| 1.1 source inventory | `InvalidSourceRootException`、`InvalidSourceRootReason` | source root 不可用时的可预期失败（不存在 / 不是目录） |

关键边界：

- `SourceScanIssue` 不是 `Finding`。扫描器只记录"我观察不到这里"，不做 Error / Warning / Info 判定。
- 取消**不**被转换成一个看起来成功的 partial result；它传播 `OperationCanceledException`。
- reparse point 只被记录为"遇到了 reparse point"：不进入、不解析 target、不分类、不归因长度。
- 遍历使用显式 stack 与逐目录惰性枚举，不依赖枚举顺序，也不做预扫描计数。
- 统计由 entries 推导，保证 summary 与 inventory 不可能不一致。

完整的扫描器契约（路径语义、统计恒等式、错误策略、reparse 边界、进度与取消）见
[`SCANNER.md`](SCANNER.md)。

### 4.3 M3 已确定：Windows 目标能力探测

M3 实现了 §1.2 的第一版真实目标能力探测。它仍然只观察、不判断，且仍然严格只读。

| 职责 | 类型 | 语义 |
| --- | --- | --- |
| 1.2 target capability discovery | `WindowsTargetCapabilityProbe` | 对给定目标目录探测 M1 冻结的三项能力：文件系统名称、当前调用用户的可用空间、单路径组件长度上限 |
| 1.2 target capability discovery | `TargetProbeResult` | 探测结果：规范化后的目标路径、三项能力、以及探测失败记录 |
| 1.2 target capability discovery | `TargetProbeIssue`、`TargetProbeIssueKind` | **探测失败的事实**，与 `Finding` 分开：无 Severity、无建议 |

关键边界：

- 三项能力**各自独立**：一项查询失败不会丢掉另一项已经取得的 capability。
  `AvailableFreeBytes = Known` 而 `FileSystemName = Unknown` 是 SMB / NAS 目标上的合法结果。
- 卷由 Windows 自己的 mount point 解析（`GetVolumePathNameW`）确定，**不是** `Path.GetPathRoot`，
  以免把 mounted folder / junction 或 UNC 目标误判成盘符 root。
- 可用空间只取 `lpFreeBytesAvailableToCaller`（当前用户配额下真正可用的量），不使用物理总剩余空间。
- 目标不可用（不存在 / 无法访问 / 不可达 / 名称非法）**不抛异常**，而是记录 issue 并让三项能力保持 Unknown；
  名称非法与"语法合法但达不到"分开报告。
- 目标可用性只采信**正面证据**：`GetFileAttributesW` 报告了目录，或 `GetDiskFreeSpaceExW` 作出了回答。
  `GetFileAttributesW` 对 network share root（如 `\\Server\Share`）按 Microsoft 文档会失败并返回
  `ERROR_BAD_NETPATH`，该失败**不**被当作目标不可用，因此 share root 本身可以作为目标被正常探测。
- **不做目标可写性测试**：不创建 probe 文件、不创建目录、不改 ACL，因此结果不声称目标可写。
- Windows 具体实现与新引入的 Win32 interop 都在 `CopyPreflight.Windows`；Core 保持 `net10.0` 无 P/Invoke。

完整的目标探测契约（输入路径语义、四个 Win32 API 及其语义选择、部分成功、只读边界、issue 模型）见
[`TARGET_PROBING.md`](TARGET_PROBING.md)。

### 4.4 M4 已确定：规则评估基线

M4 实现了 §1.3 的第一版规则层，也是第一次真正产出 §1.4 的 `Finding`。它仍然只判断、只报告：
不复制、不移动、不写探测文件。

| 职责 | 类型 | 语义 |
| --- | --- | --- |
| 1.3 rule evaluation | `WindowsPreflightEvaluator` | 消费 `SourceScanResult` 与 `TargetProbeResult`，产出 `Finding` 列表；同步、无状态、纯计算 |
| 1.3 rule evaluation | `WindowsPreflightFindingCodes` | 当前已实现规则的稳定 code 常量（M4 的 12 个 + M5 的 4 个 + M6 的 8 个 = 24 个），供日志与测试匹配；**不是** registry |

关键边界：

- 规则层是**唯一**同时消费 source facts 与 target capabilities 的地方，也是唯一决定 Severity 的地方。
  `SourceScanIssue` 与 `TargetProbeIssue` 都没有、也不会获得 Severity。
- 评估是**纯计算**：不进行文件系统 I/O，不重新扫描源，不重新探测目标，不检查目标路径是否已存在，
  不修改任何东西；相同输入必然产生相同输出。
- **source-only** 规则（源扫描失败、reparse point）始终执行；**target-dependent** 规则在目标被拒绝
  （`TargetPathInvalid` / `TargetPathUnavailable`）时全部跳过并短路。因此源侧错误与目标不可用错误可以同时出现。
- 未知目标能力产生的是 **Info**（说明哪项检查没能做成），每项最多一条 target-wide，不按 entry 重复；
  也**不**为同一件事把 `TargetProbeIssue` 再暴露一遍。
- 只有基于已知事实确定会阻止迁移的结论才是 Error。空间不足仍是 Warning，因为尚未做目标冲突、
  覆盖策略与分配语义分析。
- 实现放在 `CopyPreflight.Windows`（命名空间 `CopyPreflight.Windows`），因为本轮规则全部是 Windows
  迁移兼容性规则，且主要消费者 `TargetProbeResult` 属于该层。**没有**为此新增 `Rules` /
  `Application` / `Services` / `Infrastructure` 等项目，也没有引入 DI、registry、反射发现或插件机制。

完整的规则清单、每条规则的触发条件与边界、短路语义以及文本与确定性要求见
[`RULES.md`](RULES.md)。

### 4.5 M5 已确定：Windows 命名兼容性规则

M5 在同一个 `WindowsPreflightEvaluator` 中加入了 Windows 命名兼容性规则。它仍然只判断、只报告：
不重命名、不替换字符、不 Trim 名称，也不做任何新的文件系统 I/O。

| 职责 | 类型 | 语义 |
| --- | --- | --- |
| 1.3 rule evaluation | `WindowsNameCompatibilityRule` | 对每个 entry 自己的 leaf name 检查非法字符（Error）、保留设备名（Error）、结尾空格 / 句点（Warning） |
| 1.3 rule evaluation | `SourceCaseCollisionRule` | 同一 parent 目录内仅大小写不同的 sibling 名称 → 一条 Warning |
| 1.3 rule evaluation | `SourceRelativePathComponents` | 把 relative path 切成 leaf name 与 parent directory；纯字符串切分，不是路径抽象层 |

关键边界：

- 只判断每个 entry **自己**要创建的那一个 leaf name。祖先的名称问题不会被重复报告到它的
  descendant 上，也不会对整条 relative path 搜索 `\` 或 `:`（那是路径分隔符）。
- **仍是纯计算**：不 probe 目标已有对象、不 probe 目标大小写敏感性、不打开任何目标 handle，
  因此**没有**引入 `TargetCollisionProbe` / `TargetEntryProbe` 之类的东西。
- 这四条规则都是 **source-only**：它们不消费任何 target capability、不拼接目标路径，
  因此在目标被拒绝时**仍然产生**。判定一组规则属于哪一侧的依据是"是否消费目标事实"，
  不是 code 前缀的拼写。
- `\\?\`（extended-length namespace）只豁免 M4 的传统 `MAX_PATH` 判断，**不豁免**任何名称规则：
  名称一律按用户级 Windows 命名语义判断。
- 大小写碰撞只能是 **Warning**：典型 Windows 目标不区分大小写，但 NTFS 支持 per-directory
  case sensitivity，且 M5 没有 probe 目标，因此不能声称"一定冲突"。
- 仍然**没有**为此新增项目、DI、registry、反射发现或插件机制，也没有新增第三方依赖。

### 4.6 M6 已确定：目标已存在对象 / 碰撞

M6 在 Windows 层新增了一个只读探测器与一条规则，把"目标位置上已有什么"接进评估。数据流为：

```text
SourceScanResult + TargetProbeResult
        │
        ▼
WindowsTargetExistingObjectProbe        （只读观测；不产出 Finding）
        │  产出 TargetExistingObjectProbeResult
        ▼
WindowsPreflightEvaluator               （纯计算；不做 I/O）
        │  经 TargetExistingObjectRule
        ▼
Finding
```

| 职责 | 类型 | 语义 |
| --- | --- | --- |
| 1.2 target discovery | `WindowsTargetExistingObjectProbe` | 按固定映射 `target\<relative-path>` 只读查询每个映射位置与所需祖先；产出事实，不产出 Finding |
| 1.2 target discovery | `TargetExistingObjectProbeResult` / `ExistingObjectObservation` / `TargetObjectPresence` / `TargetRootObservation` | 已存在对象的事实与覆盖信息（观测 / 确证缺失 / 推导缺失 / 检查失败 / 未查询） |
| 1.3 rule evaluation | `TargetExistingObjectRule` | 把上述事实翻译成 Finding（file→file Warning、类型冲突 Error、目标 reparse Warning、检查失败 Warning、覆盖 / 未执行 / 取消 Info） |

关键边界：

- **探测与判断分离**：探测器只产出事实，`TargetObjectPresence` 与 `ExistingObjectGapReason` 都不带 Severity；
  Severity 只在 `TargetExistingObjectRule` 里决定。
- **`TargetCapabilities` 未被扩展**：逐项观测结果不放进 `FileSystemName` / `AvailableFreeBytes` /
  `MaxPathComponentLength`，而是单独用 `TargetExistingObjectProbeResult` 表达。
- **规则层仍然零 I/O**：`TargetExistingObjectRule` 不调用任何 Win32 API；做 I/O 的是探测器。
- **事实按位置对齐**：每个被确认的位置只有一条观测。挡路的普通文件会被记录在它自己的映射位置上
  （即使 inventory 没有列出该目录），规则层则把"某个 entry 的祖先"也当作目录来判定映射——
  因此结构阻断不依赖某个目录 entry 恰好存在，也不会因为源头只列了目录里面的对象而被漏掉。
- **评估入口向后兼容**：`WindowsPreflightEvaluator` 追加了一个三参数重载；
  原两参数重载保留，其语义被明确为"未做检查 → `TARGET_EXISTING_OBJECT_CHECK_NOT_PERFORMED`（Info）"，
  绝不冒充"目标干净"。
- **没有**为此新增运行时项目、第二个 Windows 程序集、或第三方依赖；探测类型全部在
  `CopyPreflight.Windows`（`Probing/`），Core 保持 `net10.0` 且无 P/Invoke。
- 语义细节见 [`TARGET_PROBING.md`](TARGET_PROBING.md) §13 与 [`RULES.md`](RULES.md) §4.12。

### 4.7 M7 已确定：检查编排与最小 WPF 接入

M7 把 M2–M6 的各层真正串成一次可执行的检查，并接上一个最小可用的 WPF 界面。它不新增任何检查规则，
也不改变任何 Finding 的语义与文本；它只决定"按什么顺序调用、如何报告、如何取消、如何展示"。

```text
PreflightRequest（两个原样保留的路径 + 运行标识）
        │
        ▼
PreflightWorkflow.RunAsync / Run            （唯一一处线程切换；阶段固定顺序；阶段之间检查取消）
        │
        ├─ SourceScanner.Scan                   → SourceScanResult    （M2，真实实现）
        ├─ WindowsTargetCapabilityProbe.Probe    → TargetProbeResult   （M3，真实实现）
        ├─ WindowsTargetExistingObjectProbe.Probe→ TargetExistingObjectProbeResult（M6，真实实现）
        └─ WindowsPreflightEvaluator.Evaluate(source, target, existingObjects)（三参数重载）
        │
        ▼
PreflightRunResult（status / coverage / findings 三个维度，互不折叠）
        │
        ▼
CheckViewModel（状态机 + 文案 + FindingRow[]）→ MainWindow（只绑定与转发）
```

上面的四个阶段**只在一个正常走到评估阶段的运行里**全部执行。取消或失败时，运行在它当时所在的阶段停下，
其后的阶段根本没有执行，结果里就记为 `NotPerformed`——"没有执行"和"执行了但没发现问题"在结果与界面上
始终是两件事。

| 职责 | 类型 | 语义 |
| --- | --- | --- |
| 1.5 编排 | `PreflightRequest` / `PreflightRequestError` | 两个路径**原样保留**（不 trim、不规范化）；只做"非空 + 完全限定路径"检查；携带 `RequestId` |
| 1.5 编排 | `PreflightWorkflow` | 同步内核 + 一次 `Task.Run` 跳转；阶段顺序固定；阶段之间 `ThrowIfCancellationRequested`；取消与异常都返回结果，不抛 faulted task |
| 1.5 编排 | `PreflightRunPhase` / `PreflightRunProgress` | 阶段枚举与进度快照；快照携带 `RequestId`，因此迟到/跨运行的快照不会污染新运行 |
| 1.5 编排 | `PreflightRunStatus` / `PreflightPhaseCoverage` / `PreflightCoverage` | 运行状态、单阶段覆盖度、整体覆盖度。`HasGaps` 可单独查询 |
| 1.5 编排 | `PreflightRunFailure` / `PreflightRunResult` | 失败原因与最终结果；构造函数拒绝自相矛盾的组合 |
| 1.5 呈现 | `CheckViewModel` / `CheckState` / `PreflightText` / `FindingRow` | 状态机、文案与结果行；**不含任何 WPF 类型**，可在没有窗口的情况下测试 |
| 1.5 呈现 | `CoalescingProgress` | 进入界面队列前的进度合并：只保留最新快照，任何时刻队列里最多一个回调 |
| 1.5 呈现 | `MainWindow` | 只做绑定与事件转发（浏览文件夹、开始、取消、关闭拦截） |

关键边界：

- **三个维度分开表达，绝不折叠成一个 `Success` 布尔值**。`PreflightRunResult` 同时给出运行状态
  （Completed / Cancelled / Failed）、每个阶段实际覆盖到了什么、以及 findings；构造函数据此拒绝
  "已完成但没有 inventory""未完成却带 findings""失败却没有原因"等自相矛盾的组合。
  `CheckViewModel` 同样分开呈现，但**没有任何属性**把三者合成一句话。
- **未经执行的阶段不会被当成"干净"**：漏做 M6（两参数重载）仍然只产出
  `…CHECK_NOT_PERFORMED`（Info），覆盖率显示为 `NotPerformed`。
- **"已开始但未返回" ≠ "从未执行"**：一次中断的源观测覆盖率记为 `Incomplete`（不是 `NotPerformed`），
  因为它确实走过了一部分树，并且旁边的"observed so far"计数与它一致。`Incomplete` 只说明**这一阶段被进入过、
  没有产出清单**，它本身**不**证明其中任何对象被观察到——那些计数是"看到过什么"，不是"有什么"。
- **未完成的运行不产出 findings**：取消或失败的运行保留已确立的事实和未到达的阶段，但不做评估——
  否则"部分评估"和"完整评估"在界面上无法区分。
- **只有真正完成的运行才可达冻结语句**：`"No known blocking issue was detected."` 仅在运行状态为
  Completed 且 Error 计数为 0 时出现。覆盖缺口会与它**并列**显示，而不是被它取代。
- **取消是诚实的**：每次运行一个 `CancellationTokenSource`；只有属于本次运行的
  `OperationCanceledException`（同时要求 `token.IsCancellationRequested` 与
  `exception.CancellationToken == token`）才算用户取消，其他 token 的取消、以及"带着本轮 token 但本轮并未
  被取消"的异常都按失败处理；取消后绝不进入下一阶段、绝不重新观测来"补齐"。
- **进度合并发生在进入界面队列之前**：`CoalescingProgress` 只保留最新快照，任何时刻排队等待界面线程的
  回调最多一个；被更新快照取代的中间快照直接丢弃，不进入队列。这不等同于"在队列里逐条执行后再丢弃显示"——
  前者控制的是**投递量**，后者控制不了。
- **关窗不放弃正在运行的工作**：忙碌时拒绝关闭并请求取消，窗口保持响应，直到运行真正结束。
- **仍然只读**：编排与界面都**不**向源或目标写入任何 probe / 临时文件，不复制、不移动、不删除、不重命名、
  不改权限、不读内容。窗口上明确写着这一点。
- **没有**为此新增运行时项目、DI 容器、事件总线、插件系统或工作流引擎；呈现层没有为每个方法定义接口。
  唯一被改动的既有实现是 M2 `SourceScanner` 的**进度报告频率**（见 §4.8），其扫描语义、统计与 reparse 行为未变。

### 4.8 仍未确定

为避免过度设计，以下内容仍然**不予确定**，留待有真实需求驱动的里程碑：

- 规则的插件化：规则层只需要固定的一组规则，因此规则由 `WindowsPreflightEvaluator` 直接按名字调用；
  registry / discovery / plugin / 规则脚本仍然不存在，也不打算在没有真实需求时建立
- 尚未实现的规则：overwrite / skip 策略、目标内容比较、按已存在目标文件扣减空间、
  目标大小写敏感性 probe、二阶大小写碰撞、完整目标索引器 / 递归扫描目标、8.3 短名碰撞、
  ADS 检测、reparse point 精确分类、元数据（ACL / 时间戳）丢失规则、Unicode normalization 深度兼容
- findings 的展示形式与序列化方式（导出、保存、报告生成）
- 多源目录 / 批量检查、检查历史与对比
- 结果行的排序、过滤与分组策略（M7 只按 Severity 稳定排序展示）

这些应在有真实需求驱动时再确定，避免在缺乏事实的情况下把接口设计死。
