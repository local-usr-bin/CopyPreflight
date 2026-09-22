# CopyPreflight — 规则评估契约（M4）

> **状态：M4 契约已冻结。** 本文件记录规则层当前**实际实现**的规则：它们的输入、Severity、
> 触发条件、边界，以及为什么某些结论只能是 Warning 或 Info。它不描述产品范围（见
> [`PRODUCT_SPEC.md`](PRODUCT_SPEC.md)），也不描述各层职责（见 [`ARCHITECTURE.md`](ARCHITECTURE.md)）。

规则层是唯一把 source facts 与 target capabilities 组合起来判断的地方。M4 是它第一次真正产出
用户可见的 `Finding`。

## 1. 位置与依赖

- 代码在 `src/CopyPreflight.Windows/Evaluation/`（命名空间 `CopyPreflight.Windows`，与程序集同名）。
- 原因：本轮规则全部是 **Windows 迁移兼容性**规则（`MAX_PATH`、FAT32 单文件上限、名称组件长度、
  reparse point 语义），且主要消费者 `TargetProbeResult` 本身就属于 Windows 层。
- 依赖方向不变：`CopyPreflight.Windows` → `CopyPreflight.Core`。**Core 不反向引用 Windows**，
  M4 也**没有**新增 `Rules` / `Application` / `Services` / `Infrastructure` 等项目。

## 2. 公开 API

```csharp
[SupportedOSPlatform("windows")]
public sealed class WindowsPreflightEvaluator
{
    public IReadOnlyList<Finding> Evaluate(SourceScanResult source, TargetProbeResult target);
}
```

- **纯计算、同步、无状态**：不进行任何文件系统 I/O，不重新扫描源，不重新探测目标，
  不检查 `target\relative-path` 是否存在，不修改任何东西。同一实例可重复使用。
- 相同输入必然产生相同输出。
- 输入为 `null` → `ArgumentNullException`。
- 空结果的含义永远是 **"No known blocking issue was detected."**，绝**不是** "复制一定会成功"。
- 返回序列的**顺序不是产品契约**（见 §6）。展示层可以按 Severity / code / path 自行排序。

内部组织：每个规则是一个 `internal static` 类，暴露一个 `Evaluate(...)` 方法，由
`WindowsPreflightEvaluator` **直接按名字调用**。没有 DI 容器、没有 `RuleRegistry`、没有反射发现、
没有动态插件加载、没有 service locator、没有规则脚本。`WindowsPreflightFindingCodes.All`
只是一个供测试与诊断枚举的常量清单，**不参与**发现、排序或分发。

## 3. FindingCode

`FindingCode` 是**规则**的稳定标识，不是消息标识：标题 / 解释 / 影响 / 建议可以改写，code 不变；
日志与测试匹配 code，**不匹配用户可见文本**。code 一旦被某个 finding 携带即永久固定——
只增不改名、不复用。

| 常量 | 字符串 |
| --- | --- |
| `SourceDirectoryEnumerationFailed` | `SOURCE_DIRECTORY_ENUMERATION_FAILED` |
| `SourceEntryMetadataFailed` | `SOURCE_ENTRY_METADATA_FAILED` |
| `TargetPathInvalid` | `TARGET_PATH_INVALID` |
| `TargetPathUnavailable` | `TARGET_PATH_UNAVAILABLE` |
| `Fat32FileTooLarge` | `FAT32_FILE_TOO_LARGE` |
| `TargetComponentTooLong` | `TARGET_COMPONENT_TOO_LONG` |
| `TraditionalPathLengthRisk` | `TRADITIONAL_PATH_LENGTH_RISK` |
| `ReparsePointPresent` | `REPARSE_POINT_PRESENT` |
| `TargetSpaceMayBeInsufficient` | `TARGET_SPACE_MAY_BE_INSUFFICIENT` |
| `TargetFileSystemUnknown` | `TARGET_FILESYSTEM_UNKNOWN` |
| `TargetComponentLimitUnknown` | `TARGET_COMPONENT_LIMIT_UNKNOWN` |
| `TargetFreeSpaceUnknown` | `TARGET_FREE_SPACE_UNKNOWN` |

这 12 个 code 被测试逐个钉住（`WindowsPreflightFindingCodesTests`），并且有一条测试证明
**每一个声明出来的 code 都确实会被某条规则产出**。

## 4. 规则清单

每条 finding 都满足 **问题 / 原因 → 影响 → 建议** 的结构，且四段文本非空（`Finding` 的构造器强制）。

### 4.1 源扫描失败 → Error

| issue kind | FindingCode | Severity |
| --- | --- | --- |
| `CannotEnumerateDirectory` | `SOURCE_DIRECTORY_ENUMERATION_FAILED` | Error |
| `CannotReadEntryMetadata` | `SOURCE_ENTRY_METADATA_FAILED` | Error |

- 两者都是 Error，因为两者都让 inventory **不完整**：CopyPreflight 无法声称自己看过了完整源数据集，
  而所有依赖 inventory 的规则，其可信度不会超过 inventory 本身。
- `Finding.SourceRelativePath` 尽量带上：`CannotReadEntryMetadata` 总是有；`CannotEnumerateDirectory`
  若是 source root 自身无法列举则为 `null`（root 不是 entry，没有相对路径）。
- **文本严格区分两种陈述**：只写"该对象/该目录未能完整检查，因此迁移风险无法可靠评估"，
  绝不写成"该文件一定无法复制"。测试直接断言 Impact 里出现 "does not mean"，
  且不出现 "will fail" / "cannot be copied"。
- `SourceScanIssue` 自身**没有**增加 Severity —— Severity 仍然只属于规则层。

### 4.2 目标被拒绝 → Error（并短路）

| probe issue kind | FindingCode |
| --- | --- |
| `TargetPathInvalid` | `TARGET_PATH_INVALID` |
| `TargetPathUnavailable` | `TARGET_PATH_UNAVAILABLE` |

- 两者的含义不同，**分开报告**：一个是 Windows 认为路径名本身非法（语法错误），
  一个是名字合法但走不到任何可用目录（不存在 / 不是目录 / 网络不可达 / 权限拒绝）。
- 均为 **target-wide**：`SourceRelativePath == null`。
- 命中时**短路**：不再运行任何 target 相关规则（见 §6）。源侧留下的 finding 会保留。
- M3 的探测契约保证这类 blocking issue **最多一个**；规则层仍按"每个 blocking issue 一条 finding"
  实现，不依赖该数量假设。

### 4.3 capability 未知 → Info（coverage）

| 未知能力 | FindingCode | Severity |
| --- | --- | --- |
| `FileSystemName` | `TARGET_FILESYSTEM_UNKNOWN` | Info |
| `MaxPathComponentLength` | `TARGET_COMPONENT_LIMIT_UNKNOWN` | Info |
| `AvailableFreeBytes` | `TARGET_FREE_SPACE_UNKNOWN` | Info |

- 未知能力**不是**目标的缺陷，而是"这次没能观测到"的空白，因此是 Info，**绝不**升格为 Error，
  也**绝不**用猜测的默认值填补。
- 每项**最多一条 target-wide** finding，不按 source entry 重复。
- 只有当目标本身可用时才会产生。目标已被拒绝时，blocking Error 已经足够，不再追加三条噪声。
- 这些 Info 同时承担了非 blocking 的 `TargetProbeIssue` 的用户可见职责（见 §7）。

### 4.4 FAT32 单文件上限 → Error

- 仅在 `FileSystemName == Known("FAT32")` 时执行，**大小写不敏感**（`FAT32` / `fat32` / `Fat32` …）。
- 上限：FAT32 用 32 位字节数描述文件，因此**可表示的最大文件**是
  `4,294,967,295 bytes` = `UInt32.MaxValue` = 4 GiB − 1 byte，该值本身**合法**。
- 触发条件：`length > UInt32.MaxValue`，即 `length >= 4,294,967,296`。
  边界精确：`4,294,967,295` → 不触发；`4,294,967,296` → Error。
  **不使用** `4,000,000,000` 这种十进制近似。
- 只检查**普通文件中被真实归因了长度的那些**。reparse point 在 M2 中没有任何可信长度
  （`LengthInBytes == null`），因此既不为它猜一个长度，也不对它应用本规则。
- `FileSystemName == Unknown` 时**不执行**——绝不假设目标是 FAT32。
- 文本：说明问题（FAT32 + 文件超出单文件能力）、影响（无法以当前大小直接存放在 FAT32 目标上）、
  建议（在设备与使用场景允许的前提下可考虑 exFAT / NTFS；若必须保留 FAT32，则分卷只能由用户自行安排），
  并**明确提醒重新格式化会破坏目标盘已有数据、应先备份**。
  建议中不出现"一键修复"，不写成"必须格式化"，也不声称 exFAT / NTFS 适合所有设备。

### 4.5 目标名称组件长度 → Error

- 仅在 `MaxPathComponentLength == Known(max)` 时执行；`Unknown` 时**整条规则不执行**，
  不猜 `255` 之类的默认值（不同文件系统、不同代码页下上限并不相同，猜测可能把合法名称报成 Error）。
- 对每个 `SourceEntry`，只检查**它自己要在目标创建的那个 leaf name**：
  `a\b\c.txt` 检查 `c.txt`。`a`、`b` 各有自己的 `SourceEntry`，在那里检查，避免一个超长祖先
  在数千个 descendant 上重复报同一个问题。
- 触发条件：`leaf.Length > max`。`leaf.Length == max` **合法，不触发**。
- 长度按 Windows 的计数方式（UTF-16 code unit）计算，**不做** Unicode normalization，**不自动改名**。
- reparse point 也是 `SourceEntry`，其名称同样受检。

### 4.6 传统 MAX_PATH 兼容性 → Warning

- 按冻结映射计算每个 entry 的最终目标路径：`Path.Join(targetDirectory, relativePath)`。
  不做额外规范化，路径按它真正会被拼成的样子判断。
- **两个边界，取决于该 entry 在目标处会被怎样创建：**

  | entry | 传统 Win32 边界 | 触发条件 |
  | --- | --- | --- |
  | 普通文件（`Kind == File`，非 reparse point） | `MAX_PATH = 260`（**含**结尾 null） | `长度 ≥ 260` |
  | 普通目录（`Kind == Directory`，非 reparse point） | `MAX_PATH - 12 = 248` | `长度 > 248` |

  - 文件：260 本身就已经没有位置放结尾 null，因此 `259 → 不触发`、`260 → 触发`。
  - 目录：传统 `CreateDirectory` 必须还能在本目录路径后追加一个 8.3 名字（12 个字符），
    因此目录路径不能超过 `MAX_PATH - 12`，即 `248 → 不触发`、`249 → 触发`。
    这也是同样的 destination 长度下，目录比文件更早告警的原因。
  - **reparse point 不属于上表任何一行的"普通"情形**：M4 不知道它会被如何重建（复制对象本身 /
    跟随 / 跳过），因此绝不为它套用"目录创建"这条更严格的结论，一律按**通用边界 260** 判断。
    这是刻意的保守选择，不是遗漏。
- 这是 **Warning，不是 Error**。文本必须同时说清：这是传统 Windows / 应用兼容性风险；
  Windows 10 1607 起系统层面有了 long path 支持，但**只有本身就 long-path aware 的程序**才能受益；
  实际结果取决于用户选用的复制程序与运行环境。
- **绝不**写成"超过 260 字符一定无法复制"；也**不读取**本机的 `LongPathsEnabled` 开关，
  再据此宣布用户未来实际会用的复制工具一定支持——系统开关本身无法推断具体复制程序的行为。
- **只有 Win32 extended-length 文件命名空间前缀 `\\?\`**（`\\?\UNC\server\share` 也包含在内，
  前缀本身大小写不敏感）才让目的地路径不经传统路径处理，本启发式不适用，**不产生**该 Warning。
  这是一次对**有文档依据的 extended-length 前缀**的最小判断，
  **不是**完整的 NT path namespace parser；除此之外一律按普通路径处理。
- `\\.\` 是 Win32 **device 命名空间**（用于设备访问），**不是** long-path 豁免。
  以 `\\.\` 开头的路径**不再**因为前缀本身被跳过，而是与普通路径一样接受上表的长度判断。

### 4.7 reparse point → Warning

- 对每个 `SourceEntry.IsReparsePoint == true` 产生 **一条** Warning。
- M4 仍然**不知道**它是 symlink / junction / mount point / 别的 reparse 对象，因此文本保持泛化
  （"检测到特殊文件系统对象（reparse point）"）。**不猜类型**、**不解析 target**、**不跟随**、
  **不读 `LinkTarget`**、**不修改 scanner**。
- 影响段说明不同复制工具的处理方式不同（复制对象本身 / 跟随目标 / 跳过 / 其他），
  建议段请用户在使用前确认复制工具的行为。
- 本规则是 **source-only**：它不读目标，因此目标被拒绝时**仍然产生**。

### 4.8 可用空间 → Warning

- 条件：`AvailableFreeBytes == Known(free)` 且 `SourceScanResult.LogicalFileBytes > free`。
- **`>` 是严格的**：`source bytes == free` → 不产生；`source bytes < free` → 不产生。
  `AvailableFreeBytes == Unknown` → 不做比较。
- `LogicalFileBytes` 只累计**普通文件**被归因的长度，reparse point 贡献 `0`
  （它本来就没有长度，也没有被伪造成 0）。
- 一条 **target-wide** finding，不按 entry 重复。
- 措辞必须诚实：只能表达"按已观察到的普通文件逻辑大小计算，可用空间可能不足"，
  **不能**说"复制一定失败"。
- 当空间足够时**不产生**"空间足够"的 Success finding —— 那不能证明真实迁移一定有足够空间。

## 5. 为什么空间规则当前只能是 Warning

这不是保守措辞，而是本轮**已知事实的上限**。M4 的规则层没有：

- target collision probing（目标是否已存在同名对象）
- overwrite / skip 策略（复制程序会覆盖、跳过还是报错）
- 已存在目标文件的分析
- reparse point 的实际复制行为
- sparse / compression 的真实分配语义（逻辑大小 ≠ 占用空间）

以上任何一项都可能显著改变真实的空间需求。在这些事实缺失时给出 Error，等于声称比已观测事实
更强的确定性。等目标冲突分析完成后，可以再设计更确定的 Error 条件。

## 6. 执行顺序与短路语义

评估顺序（实现固定，**但顺序不是产品契约**，测试不得依赖"某条 finding 排第几"）：

1. `SourceScanIssueRule` —— 源侧，始终执行
2. `ReparsePointRule` —— 源侧，始终执行
3. `TargetPathBlockerRule` —— 若目标被拒绝，产出 blocking Error 后**立即返回**
4. `TargetCapabilityCoverageRule` —— 仅目标可用时
5. `Fat32MaxFileSizeRule`
6. `TargetPathComponentRule`
7. `TraditionalPathLengthRule`
8. `TargetFreeSpaceRule`

分组依据是"这条规则是否依赖目标"：

- **source-only** 规则（1、2）描述源自身的事实，目标如何都成立，**始终执行**。
- **target-dependent** 规则（4–8）在目标被拒绝时**全部跳过**：在一个不能作为目标目录的路径下
  拼目标路径，或推演一个根本不会发生迁移的目标文件系统能力，都是在描述一件不会发生的事。

因此"源扫描错误 + 目标不可用错误"**可以同时出现**，目标失败不会丢掉已经知道的源问题。

## 7. 与 TargetProbeIssue 的关系

- 非 blocking 的 probe issue —— `VolumePathQueryFailed`、`FreeSpaceQueryFailed`、
  `VolumeInformationQueryFailed` —— **不**逐条直接变成 finding。
  它们各自只导致**一项** capability 变为 Unknown，用户通过 §4.3 的 coverage Info 得知"这项检查没做成"。
- 目的：避免同一件事出现两份用户可见信息（"Win32 API 报错" + "该能力 Unknown"）。
  `TargetProbeIssue` 保留其技术诊断用途（kind / affected path / Win32 入口点名 / native error code），
  仍是可比较的普通值对象，供日志与后续里程碑使用。
- 唯一的例外是 blocking 的两类，它们本身就是"目标不能用作目标"这一结论（§4.2）。

## 8. 文本语言与确定性

- 用户可见文本当前使用**英文**，与仓库内既有的诊断字符串（M2 的 scan issue diagnostic、
  M3 的 probe diagnostic、异常消息）一致。**不为 M4 建立 localization 框架。**
- 规则输出必须**确定**：相同输入必然产生相同文本。因此：
  - 源扫描失败 finding 里描述证据时只使用**与语言无关**的 `ExceptionTypeName` 与 `HResult`
    （如 `System.UnauthorizedAccessException, HResult 0x80070005`），
    **不**把 `SourceScanIssue.Diagnostic` 里那条本地化的异常消息嵌进 finding 文本
    （与 M3 对 `TargetProbeIssue.Diagnostic` 的处理原则一致）。
  - 数字格式化一律走 invariant 方式，不依赖当前区域设置。
- 不使用 AI、不联网、不生成长篇教育文章、不使用过度绝对的措辞。`Finding.Title` 保持短。

## 9. 纯计算边界

规则层**没有**任何 I/O：

- 不 `File.Exists` / `Directory.Exists` / `GetFileAttributes`，不枚举目标树
- 不重新扫描源，不重新探测目标
- 不使用 `Directory` / `File` 的写 API，不创建任何 probe 文件

测试直接验证这一点：把目标目录指向一个**确定不存在**的随机路径，target-dependent 规则
依然按输入声明的事实产生完全相同的 finding。空间的 Error 化被明确推迟，正是因为那需要
目标冲突等**新的观测事实**，而不是规则层去偷偷读磁盘。

## 10. 本阶段明确未实现的规则（延期）

以下内容**有意**不在 M4 范围内，不要顺手加入：

- **目标冲突**：目标 `relative-path` 是否已存在、file ↔ directory 类型冲突
  （需要目标侧 I/O，属于后续 milestone）
- **Windows 命名规则**：`< > : " / \ | ? *`、控制字符、`CON` / `PRN` / `AUX` / `NUL`、
  `COM1`–`COM9`、`LPT1`–`LPT9`、结尾句点 / 空格。
  这些涉及 Win32 namespace、源文件系统行为、shell 与 filesystem 的差异以及 NAS / SMB 差异，
  风险面很大，放在后续独立 milestone
- **reparse point 精确分类**（symlink / junction / mount point / other）
- **元数据丢失规则**（ACL / ADS / 时间戳精度等）
- **FAT12 / FAT16 的文件上限**：本轮只在文件系统名恰好为 `FAT32` 时执行 §4.4。
  名称接近但不同的文件系统（例如只报告 `FAT`）不会被套用该规则——宁可漏报，也不猜
- **Unicode normalization 深度兼容**
- UI 集成、规则插件系统、报告序列化

## 11. 与 M1 / M2 / M3 的关系

M4 **没有修改**前几个里程碑的任何模型或契约：

- `Finding` / `FindingCode` / `Severity` / `CapabilityValue<T>` / `TargetCapabilities` /
  `SourceEntry` / `SourceScanResult` / `SourceScanIssue` 全部原样使用。
- `SourceScanIssue` **没有**被加上 Severity（那仍属于规则层）。
- 扫描器与目标探测的 public contract 未变；规则层只消费它们的输出。
- 没有新增第三方依赖，没有新增运行时项目。
