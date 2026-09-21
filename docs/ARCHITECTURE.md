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

## 2. 三个核心问题

各层之间的分工可以用三个问题概括：

| 层 | 回答的问题 |
| --- | --- |
| Scanner | **"我观察到了什么？"** |
| Target capability layer | **"目标已知能做什么？"** |
| Rules | **"二者组合意味着什么？"** |

这三句话是判断某段代码该放在哪里的第一准则。

## 3. 依赖方向

当前工程基线为：

- `CopyPreflight.Core` —— 承载 1.1–1.4 的职责，**不依赖 WPF**，**不依赖** `CopyPreflight.App`。
- `CopyPreflight.App` —— 承载 1.5，引用 `CopyPreflight.Core`。
- `CopyPreflight.Tests` —— 引用 `CopyPreflight.Core`。

依赖方向单向指向 Core。Core 保持与 UI 技术无关，从而使扫描与规则逻辑可以在没有 UI 的情况下被测试。

> M0 只固定了"Core 不依赖 UI"这一条方向性约束。M1 进一步确定了最小核心领域模型的类型与语义（见 §4.1），
> M2 确定了源扫描器的公开契约与结果模型（见 §4.2）。

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

### 4.3 仍未确定

为避免过度设计，以下内容仍然**不予确定**，留待有真实需求驱动的里程碑：

- 规则的执行接口与注册方式（RuleEngine / registry / discovery / plugin）
- 具体规则的实现（FAT32 单文件上限、空间、路径长度、文件名合法性、冲突等）
- 目标能力探测（target probing）的具体实现方式
- findings 的展示形式与序列化方式
- 扫描器在 UI 中的调度方式（在哪个线程运行、如何在展示层呈现进度与取消）

这些应在有真实需求驱动时再确定，避免在缺乏事实的情况下把接口设计死。
