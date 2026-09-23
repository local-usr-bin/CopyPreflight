# CopyPreflight — 规则评估契约（M4 / M5 / M6）

> **状态：M4、M5 与 M6 契约已冻结。** 本文件记录规则层当前**实际实现**的规则：它们的输入、Severity、
> 触发条件、边界，以及为什么某些结论只能是 Warning 或 Info。它不描述产品范围（见
> [`PRODUCT_SPEC.md`](PRODUCT_SPEC.md)），也不描述各层职责（见 [`ARCHITECTURE.md`](ARCHITECTURE.md)）。
> **M8 只追加了一条"源根本身不可用"的 source-only 规则（§4.13）与它的 FindingCode，并把它插在评估顺序最前（§6）；**
> 既有规则的触发条件、Severity 与文本**一律未改**。

规则层是唯一把 source facts 与 target capabilities 组合起来判断的地方。M4 是它第一次真正产出
用户可见的 `Finding`；M5 在其中加入了 Windows 命名兼容性规则，M6 又加入了"目标已存在对象 /
碰撞"这一组规则；而规则层仍然是纯计算。M6 的观测事实由 [`TARGET_PROBING.md`](TARGET_PROBING.md)
描述的只读探测产出，规则层只消费它，不自己做任何 I/O。

## 1. 位置与依赖

- 代码在 `src/CopyPreflight.Windows/Evaluation/`（命名空间 `CopyPreflight.Windows`，与程序集同名）。
- 原因：本轮规则全部是 **Windows 迁移兼容性**规则（`MAX_PATH`、FAT32 单文件上限、名称组件长度、
  Windows 文件 / 目录名称规则、reparse point 语义），且主要消费者 `TargetProbeResult` 本身就属于 Windows 层。
- 依赖方向不变：`CopyPreflight.Windows` → `CopyPreflight.Core`。**Core 不反向引用 Windows**，
  M4 也**没有**新增 `Rules` / `Application` / `Services` / `Infrastructure` 等项目。

## 2. 公开 API

```csharp
[SupportedOSPlatform("windows")]
public sealed class WindowsPreflightEvaluator
{
    public IReadOnlyList<Finding> Evaluate(SourceScanResult source, TargetProbeResult target);
    public IReadOnlyList<Finding> Evaluate(
        SourceScanResult source,
        TargetProbeResult target,
        TargetExistingObjectProbeResult? existingObjects);
}
```

- 两参数重载等价于 `existingObjects: null`。**`null` 不是"目标上什么都没有"**，而是"没有做过这项检查"，
  因此会产出一条 `TARGET_EXISTING_OBJECT_CHECK_NOT_PERFORMED`（Info，见 §4.12）。省略检查绝不能
  与"检查通过、目标干净"混为一谈——这正是 M6 引入三参数重载的原因。
- **纯计算、同步、无状态**：不进行任何文件系统 I/O，不重新扫描源，不重新探测目标，
  不检查 `target\relative-path` 是否存在，不修改任何东西。同一实例可重复使用。
  目标上已有什么，是**由完成观测的一方作为事实传进来**的（`TargetExistingObjectProbeResult`）。
- 相同输入必然产生相同输出。
- 输入为 `null`（`source` / `target`）→ `ArgumentNullException`。`existingObjects == null` 是合法输入，
  它表达"未做检查"，不是错误。
- 空结果的含义永远是 **"No known blocking issue was detected."**，绝**不是** "复制一定会成功"。
- 返回序列的**顺序不是产品契约**（见 §6）。展示层可以按 Severity / code / path 自行排序。

内部组织：每个规则是一个 `internal static` 类，暴露一个 `Evaluate(...)` 方法，由
`WindowsPreflightEvaluator` **直接按名字调用**。没有 DI 容器、没有 `RuleRegistry`、没有反射发现、
没有动态插件加载、没有 service locator、没有规则脚本。`WindowsPreflightFindingCodes.All`
只是一个供测试与诊断枚举的常量清单，**不参与**发现、排序或分发。

名称规则共用一个极小的 `internal static SourceRelativePathComponents`（把 relative path 切成
leaf name 与 parent directory）。它只是字符串切分，**不是**路径抽象层、不是 plugin 接口，
也不做任何 normalization。M5 只为一个规则类新增了它，没有新增规则注册、发现或分发机制。

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
| `TargetNameInvalidCharacter` | `TARGET_NAME_INVALID_CHARACTER` |
| `TargetNameReservedDevice` | `TARGET_NAME_RESERVED_DEVICE` |
| `TargetNameTrailingSpaceOrPeriod` | `TARGET_NAME_TRAILING_SPACE_OR_PERIOD` |
| `SourceCaseCollisionRisk` | `SOURCE_CASE_COLLISION_RISK` |
| `TargetExistingFileCollision` | `TARGET_EXISTING_FILE_COLLISION` |
| `TargetExistingTypeCollision` | `TARGET_EXISTING_TYPE_COLLISION` |
| `TargetExistingReparsePoint` | `TARGET_EXISTING_REPARSE_POINT` |
| `SourceReparsePointExistingTargetObject` | `SOURCE_REPARSE_POINT_WITH_EXISTING_TARGET_OBJECT` |
| `TargetExistingObjectCheckFailed` | `TARGET_EXISTING_OBJECT_CHECK_FAILED` |
| `TargetExistingObjectCoverageIncomplete` | `TARGET_EXISTING_OBJECT_COVERAGE_INCOMPLETE` |
| `TargetExistingObjectCheckNotPerformed` | `TARGET_EXISTING_OBJECT_CHECK_NOT_PERFORMED` |
| `TargetExistingObjectCheckCancelled` | `TARGET_EXISTING_OBJECT_CHECK_CANCELLED` |

这 25 个 code 被测试逐个钉住（`WindowsPreflightFindingCodesTests`），并且有一条测试证明
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

### 4.9 Windows 名称兼容性 —— 三项检查（M5）

只检查每个 `SourceEntry` 自己要在目标创建的**那一个 leaf name**：

```text
folder\sub\a.txt   →   只检查 a.txt
```

`folder` / `sub` 各有自己的 `SourceEntry`，在它们自己那里检查；这样避免一个祖先的名称问题
在每个 descendant 上重复报告成千上万次。同理，**不会**对整个 relative path 字符串搜索 `\` 或 `:`
——反斜杠本身是路径分隔符，对整条路径搜索它只会把每一个嵌套路径都报成"名称含非法字符"。

leaf name 由字符串自身的分隔符切分得到（`SourceRelativePathComponents`），**不使用**
`Path.GetDirectoryName`：它会把 `a:.txt` 的目录解析成卷限定符 `a:`，还会折叠重复分隔符，
而这里要判断的恰是可能含这类字符的名称。

同一名称同时命中多项时，**每一项各出一条 finding**（例如 `NUL.` 既是保留设备名又结尾是句点）。
两个事实互相独立，只报第一个会让用户"修一个、再跑一次、又发现下一个"。

| 检查 | FindingCode | Severity |
| --- | --- | --- |
| 名称含普通 Windows 名称不能使用的字符 | `TARGET_NAME_INVALID_CHARACTER` | Error |
| 名称是 Windows 保留设备名 | `TARGET_NAME_RESERVED_DEVICE` | Error |
| 名称以空格或句点结尾 | `TARGET_NAME_TRAILING_SPACE_OR_PERIOD` | Warning |

**（1）非法字符 → Error**

- 集合：`< > : " / \ | ? *`，以及 `U+0000` ～ `U+001F`。
- 边界精确：`U+0020`（普通空格）**不是**非法字符；它只有在**结尾**时被第（3）项捕获。
- `/` 与 `\` 保留在集合里，是为了忠实表达 Microsoft 文档给出的那份字符集；由分隔符切分得到的
  leaf name 实际上不可能含有其中一个，因此这一项不会造成误报。
- `:` 在 ADS 语法中有特殊含义，但 M5 **不处理** Alternate Data Streams：作为普通文件 / 目录
  leaf name 的组成部分，它按保留字符处理。
- 文本：说明名称含哪些字符（控制字符渲染为 `U+XXXX`，不把控制字符原样送进用户可见文本），
  影响段说明该对象**无法按当前名称通过普通 Windows 文件命名语义**创建到目标位置，
  建议段请用户在迁移前自行重命名，或按适合其实际环境的方式处理。
- **不**自动重命名、**不**替换字符、**不**猜替换方案。

**（2）保留设备名 → Error**

- 集合（**只**包含 Microsoft 明确文档化的那些）：
  `CON`、`PRN`、`AUX`、`NUL`、`COM1`–`COM9`、`LPT1`–`LPT9`，
  以及文档化的大写上标形式 `COM¹`/`COM²`/`COM³` 与 `LPT¹`/`LPT²`/`LPT³`（`U+00B9`/`U+00B2`/`U+00B3`）。
- 匹配大小写不敏感，对文件与目录同样适用；reparse point 也一样——名称是否含非法字符 /
  是否为保留设备名，与是否知道它的 reparse target 无关（见 §4.10 末）。
- **扩展名不能解除保留语义**：判断基于 leaf name 中**第一个 `.` 之前**的 base portion，
  因此 `NUL`、`nul`、`NUL.txt`、`NUL.tar.gz`、`COM1.log`、`lpt9.bin`、`COM¹.txt` 都触发。
- 以 `.` 开头不算"base portion 是保留名"：`.temp` 的 base portion 为空，是普通名称。
- **不误报**：`COM0`、`COM10`、`LPT0`、`LPT10`、`CONSOLE`、`NULX`、`AUXILIARY` 都是普通名称。
  这是文档给出的**固定清单**，不是一个通用的 DOS device name parser。
- 文本：指明 base portion 是哪个设备名，并说明扩展名不解除保留语义（Microsoft 文档把
  `NUL.txt` 与 `NUL.tar.gz` 都表述为等价于 `NUL`）。

**（3）结尾空格 / 句点 → Warning**

- 条件：leaf name 的**最后一个字符**是普通空格 `U+0020` 或句点 `.`。
- **只看结尾**：`.temp`、`file.txt`、`file name.txt` 都不触发。
- 文本必须同时说清三件事：这是 **Windows Shell / 应用兼容性**风险；
  **底层文件系统**可能支持这类名称；**实际复制工具**的行为也会不同。
- **不得**声称"一定不能复制"。措辞只能是"可能被拒绝、被截断，或以其他方式被不同处理"，
  因此对象可能以**不同于源名称**的名字到达，甚至不再是一个独立对象。
- **不** Trim 后偷偷改名、**不**自动改名。

### 4.10 仅大小写不同的兄弟名称碰撞 → Warning

- 检查范围严格限定为**同一个 parent relative directory 内**的 sibling entries。
  分组键是「parent relative path + leaf name」，其中：
  - **parent 按 `Ordinal` 精确相同**——碰撞说的是"同一目录里的两个名字"，`dir1\A.txt` 与
    `dir2\a.txt` 分属两个目录，不构成碰撞。两个**父目录名本身**仅大小写不同是另一个问题，
    由这两个目录名各自作为 entry 时产生的碰撞 finding 回答。
  - **leaf name 按 `OrdinalIgnoreCase` 相同**——这是把大小写变体归入同一组的唯一依据。
- 只有当某个 group 里存在**两个以上 `Ordinal` 上互不相同**的名称时才产生 finding。
  同一个名字出现两次不算碰撞（它就是一个名字）。
- **一个 group 一条 finding**，不是两两配对：`A.txt` + `A.TXT` + `a.txt` 是一条，而不是三条。
  分组用 dictionary 完成，不做 O(n²) 两两比较。
- 名称列表按 `Ordinal` 排序后写进 `Explanation`，因此文本只取决于观察到了哪些名称，
  与 inventory 的到达顺序无关。
- `Finding.SourceRelativePath` 是该 finding 所属的 **parent relative directory**；
  碰撞发生在 source root 的直接子项之间时为 `null`（source root 自身没有 relative path）。
- 文件、目录、reparse point **都参与**分组：每一个都占用一个名称。
  规则**不跟随** reparse point、**不分类**、**不读** `LinkTarget`。
- 对 Windows 上大小写不敏感的最常见情形，这类名称会映射到同一个对象，因此值得提醒；
  但它**只能**是 Warning，理由见 §5.2。

### 4.11 名称规则与 extended-length namespace（`\\?\`）

M4 的 `\\?\` 判断（§4.6）只作用于**传统 `MAX_PATH` 兼容性**这一条规则。M5 的名称规则
**不因**目标路径以 `\\?\` 开头而跳过：

- `\\?\` 的文档含义只是让路径**绕过传统路径解析**，它并不把保留设备名或非法字符
  变成普通用户迁移时可以依赖的名称。
- 因此 M5 一律按**用户级 Windows 命名语义**判断名称，而不是按"底层 API 有办法构造出特殊路径"。
  底层 namespace 能构造出什么，与用户把文件迁移到某处时会发生什么，是两件事。
- 这三项名称检查**不读**目标、**不拼接**目标路径，因此即使目标被拒绝也照常产生（见 §6）。

### 4.12 目标已存在对象 / 碰撞（M6）

输入是探测层产出的 `TargetExistingObjectProbeResult`：它对每个 source entry 的映射位置给出一条
`ExistingObjectObservation`（观测到的对象 / 已确证的缺失 / 由缺失祖先推出的缺失 / 检查失败 /
未查询），并额外为**挡住分支却不在 inventory 里的普通文件**给出一条观测——每个位置只有一条。
规则层**不做任何 I/O**，只把这些事实翻译成 finding。观测与"事实 / 覆盖"语义的完整
定义见 [`TARGET_PROBING.md`](TARGET_PROBING.md) §13。

映射固定为 `target\<source relative path>`（target 就是目标根，**不再**多一层源目录名；
**不**通过改变映射来消解冲突）。找出对象**不是**碰撞结论：下面第一行只说明"该位置已被占用"。

| 观测到的情形 | FindingCode | Severity |
| --- | --- | --- |
| 源文件 → 目标已存在文件 | `TARGET_EXISTING_FILE_COLLISION` | Warning |
| 源目录 → 目标已存在目录 | ——（不产生） | —— |
| 源文件 → 目标已存在目录 | `TARGET_EXISTING_TYPE_COLLISION` | Error |
| 源目录 → 目标已存在文件 | `TARGET_EXISTING_TYPE_COLLISION` | Error |
| 目标位置是 reparse point（源为普通文件 / 目录） | `TARGET_EXISTING_REPARSE_POINT` | Warning |
| 源本身是 reparse point → 目标已存在普通文件 / 目录 | `SOURCE_REPARSE_POINT_WITH_EXISTING_TARGET_OBJECT` | Warning |
| 目标根本身是 reparse point | `TARGET_EXISTING_REPARSE_POINT`（target-wide） | Warning |
| 可靠证据表明该位置无对象（已确证 / 由缺失祖先推出） | ——（不产生） | —— |
| 该位置的检查未作答 | `TARGET_EXISTING_OBJECT_CHECK_FAILED` | Warning |
| 一次未覆盖全量的运行 | `TARGET_EXISTING_OBJECT_COVERAGE_INCOMPLETE`（一条汇总） | Info |
| 整个检查未执行（未提供结果） | `TARGET_EXISTING_OBJECT_CHECK_NOT_PERFORMED` | Info |
| 检查被取消 | `TARGET_EXISTING_OBJECT_CHECK_CANCELLED` | Info |

关键语义：

- **源目录 → 目标已存在目录不产生碰撞 finding**，而是继续按各自的映射检查其子项；**不**为每个已存在
  目录出一条 Info（两个目录是合并而不是冲突）。
- **类型冲突**（上表第 3、4 行）是 Error：它由对象**自身的种类**决定，不依赖任何复制程序假设——
  在另一个对象占着该路径时，对象无法按映射到达那里。文本只说"当前精确映射在结构上被阻断，
  必须先解决它"，**不**提出删除 / 改名 / 改路由，也**不**改变映射。
- **reparse point 优先于普通矩阵**：目标位置是 reparse point 时，**不**把它当成普通文件或目录
  参与上表的文件 / 目录判定，**不**猜它是 symlink / junction / mount point，**不**解析它的 target；
  并且声明"其下对象依赖它指向哪里，本工具未检查"这一覆盖边界。
- **源自身是 reparse point 时，不因源形状把冲突升级为精确 Error**：源 reparse 的迁移语义未定，
  因此只报"映射位置已被占用"。M4 的源 reparse Warning（§4.7）**照常保留**（两者可同时出现）。
- **有可靠证据表明无对象时，不产生碰撞 finding**，也**不**声称"目标可写"或"复制会成功"——"没找到"
  不等于"不会失败"。
- **检查失败时，事实保持 Unknown**：文本**不得**变成"目标不存在"或"无法写入"，并保留原始 Win32 错误码
  作为与语言无关的证据（`GetFileAttributesW reported Windows error N`）。
- **分支被阻断**（祖先已是普通文件 / 祖先检查失败 / 祖先为 reparse point）：根因只报一次，
  **不**为每个 descendant 重复同一条 finding；受影响的子树由**一条** target-wide 覆盖汇总（Info）覆盖。
  其中"**祖先已是普通文件**"是唯一**不算覆盖缺口**的阻断——它是已确定的结构性冲突，因此**必须**在根因
  位置上产出一条 Error，而**不**是让整条分支无声通过（另外两种阻断本身就会产生覆盖汇总）。
  **该祖先不需要在 inventory 里恰好有一条目录 entry**：源里某个 entry 的祖先必然是目录，
  规则层据此判定该位置的映射；否则"只列了目录里面的对象"就会把冲突漏掉。
- **目标根**：根是普通文件 → 与"目标无效"语义一致（blocking，见 §4.2）；根是 reparse point →
  一条 target-wide Warning 并停止；根检查失败 → **不**为所有子项重复报告。
- 只有**部分**位置被检查 → 一条 `TARGET_EXISTING_OBJECT_COVERAGE_INCOMPLETE`，说明被遗漏的**数量与原因**，
  而不是每个受影响对象一条。这里的"数量"以**本次检查记录的位置**为分母（每条 source entry，加上被记录
  下来的阻断祖先），**不是** M2 的枚举总数。空结果集 **不等于**"检查完整"；被取消的项**永不**被默认成"缺失"。
- **本里程碑明确不做**：overwrite / skip / keep-both 策略、目标内容比较、按已存在目标文件扣减空间、
  目标大小写敏感性探测、二阶大小写碰撞、reparse 分类 / 解析、ACL / ADS / 时间戳能力分析、
  Unicode 归一化深度、8.3 别名、报告导出、最终 GUI。

### 4.13 源根本身不可用 → Error（M8 追加）

| 源根事实 | FindingCode | Severity |
| --- | --- | --- |
| 解析后不存在（`UnusableRootReason = NotFound`） | `SOURCE_ROOT_UNUSABLE` | Error |
| 解析后存在但不是目录（`UnusableRootReason = NotADirectory`） | `SOURCE_ROOT_UNUSABLE` | Error |

- 输入不是"扫描失败"，而是扫描器**在任何遍历开始之前**就拒绝了根：`SourceScanResult.UnusableRootReason`
  有值、`HasUsableSourceRoot == false`、没有 entry 也没有 issue（见 [`SCANNER.md`](SCANNER.md) §6.2）。
  因此本规则**不读**任何新事实，只把这个已经确定的原因翻译成用户可见的 finding。
- 两个原因**共用一个 code、分开的文本**：`Reason == NotADirectory` 时标题为
  "Source path is not a directory"，其余为 "Source directory does not exist"。
  把两者写成同一句，等于对事实做了超出观测的断言——"不存在"与"存在但不是目录"是不同的结论，
  只有扫描器确定了哪一个，才说哪一个。
- 两种情形都是 **Error**：根不可用使**所有**依赖 inventory 的规则失去立足点
  （路径长度、名称兼容性、文件系统上限、目标已有什么，都要描述从未被观察到的内容），
  这是本次检查能报告的最根本的阻塞。
- `Finding.SourceRelativePath == null`——根不是 entry，**不给它编造一个相对路径**。
  呈现层据此把它标为 **source-wide**，而不是 target-wide（见 [`WORKFLOW_UI.md`](WORKFLOW_UI.md) §11）。
- **不**以 `Directory.Exists(...) == false` 作为统一判据：这与 Microsoft 文档不符——该 API 在权限不足、
  路径过长或其他错误时同样返回 `false`。本规则只消费 `InvalidSourceRootException.Reason`
  这个已经由扫描器作出的判定，**不**做第二次文件系统判断，也**不**把权限 / I/O / 设备失败并入"不存在"。
- **根不可用不是短路条件**：它由 source-only 规则报告，目标侧阶段照常进行（与"目标被拒绝"的处理一致，
  见 §6）。被**扣住**的是任何需要源清单的结论——它们拿到的是空 entry 列表，因此什么也不产出，
  而不是被塞进伪造的条目或悄悄跳过。
- 与 §4.1 的区别：§4.1 说的是"根能列出来，但某个目录 / 某个对象观察失败"（inventory 不完整，
  `SOURCE_DIRECTORY_ENUMERATION_FAILED`）；本规则说的是"根本没有 inventory"。
  两者**不会**同时为同一个根产生（根不可用时根本没有枚举发生，见 §6.2 与 `PreflightWorkflowSourceRootTests`）。
- 内部异常类型（`InvalidSourceRootException`）**绝不**出现在任何用户可见文本里。

## 5. 为什么这些结论停留在 Warning

### 5.1 为什么可用空间规则当前只能是 Warning（M4；M6 之后仍然如此）

这不是保守措辞，而是本轮**已知事实的上限**。空间规则至今没有：

- **overwrite / skip 策略**（复制程序会覆盖、跳过还是报错）——M6 只报告"该位置已被占用"，
  并不决定会被覆盖，因此 **不**据此扣减空间需求
- **已存在目标文件的分析**：M6 读了目标位置是否存在、是什么种类，**没有**读取任何文件内容或大小，
  所以它不知道一个已存在的目标文件会占用多少、会不会被替换
- **reparse point 的实际复制行为**
- **sparse / compression 的真实分配语义**（逻辑大小 ≠ 占用空间）

以上任何一项都可能显著改变真实的空间需求。在这些事实缺失时给出 Error，等于声称比已观测事实
更强的确定性。**M6 加入目标碰撞观测后，空间规则仍然只是 Warning**：知道"位置被占用"并不等于
知道复制程序会不会覆盖它、也不等于知道已存在对象的大小，因此"按已存在目标文件扣减空间"
被明确推迟（见 §10）。等这些事实补齐后，才可以再设计更确定的 Error 条件。

### 5.2 为什么仅大小写不同的名称碰撞只能是 Warning（M5）

同样不是措辞上的退让，而是"没有 probe 目标"这一事实的上限：

- 典型 Windows 文件系统、以及大多数复制程序所使用的 Win32 环境**不区分大小写**，
  因此这些源对象**可能**映射到同一个名称。这是真实存在的迁移风险，也正是这条规则要提醒的事。
- 但 **NTFS 支持 per-directory case sensitivity**：同一个卷上，不同目录的行为可以不同。
- 某些目标文件系统与某些应用**确实能够区分**这类名称。
- M5 **没有** probe 目标目录具体的大小写敏感性——M1 冻结的三项 capability 里没有这一项，
  而且 M5 不得新增任何 target I/O。

在这些事实缺失时给出 Error，等于声称"一定冲突"，而这一点恰恰没有被观测到。更糟的是，
"一定冲突"会推动用户去做一次并非必需的改名。因此它是 **Warning**：问题真实存在，
但是否成为故障取决于复制程序、目标文件系统与运行环境。

> 与之对照：非法字符与保留设备名是 Error，因为它们是**名称本身**在普通 Windows 命名语义下
> 的确定性缺陷，不依赖任何尚未观测的目标事实。而结尾空格 / 句点仍然是 Warning，
> 因为底层文件系统与复制工具的行为确实可能不同（见 §4.9 第 3 项）。

## 6. 执行顺序与短路语义

评估顺序（实现固定，**但顺序不是产品契约**，测试不得依赖"某条 finding 排第几"）：

1. `SourceRootUnusableRule` —— 源侧，始终执行（M8）
2. `SourceScanIssueRule` —— 源侧，始终执行
3. `ReparsePointRule` —— 源侧，始终执行
4. `WindowsNameCompatibilityRule` —— 源侧，始终执行（M5）
5. `SourceCaseCollisionRule` —— 源侧，始终执行（M5）
6. `TargetPathBlockerRule` —— 若目标被拒绝，产出 blocking Error 后**立即返回**
7. `TargetCapabilityCoverageRule` —— 仅目标可用时
8. `TargetExistingObjectRule` —— 仅目标可用时（M6）
9. `Fat32MaxFileSizeRule`
10. `TargetPathComponentRule`
11. `TraditionalPathLengthRule`
12. `TargetFreeSpaceRule`

分组依据是"这条规则是否依赖目标"：

- **source-only** 规则（1–5）描述源自身的事实，目标如何都成立，**始终执行**。
  其中 `SourceRootUnusableRule` 排在最前，因为"根根本不可用"是比"某个目录列不出来"更基本的事实；
  它对**所有**可用输入都是空操作（`UnusableRootReason == null` 时不产出任何 finding）。
- **target-dependent** 规则（7–12）在目标被拒绝时**全部跳过**：在一个不能作为目标目录的路径下
  拼目标路径，或推演一个根本不会发生迁移的目标文件系统能力，都是在描述一件不会发生的事。
  M6 的 `TargetExistingObjectRule` 属于这一组，因为它消费的是"目标位置上已有什么"这一目标侧事实。
  当目标本身被拒绝时，已经产出的 blocking finding 已经说明"无法进行任何目标侧检查"，因此
  目标已存在对象的覆盖情况**不**再叠加重复（见 §4.12）。

**M5 的四条名称规则属于哪一组，是由"它是否消费目标事实"决定的，而不是由它的 code 前缀拼写决定的。**
非法字符、保留设备名、结尾空格 / 句点描述的是**名称自身的属性**——它们既不读取任何一项 target
capability，也不拼接目标路径，因此在目标被拒绝时**仍然产生**（`SOURCE_CASE_COLLISION_RISK`
同理）。一条 `TARGET_NAME_*` finding 讲的是"这个名称在普通 Windows 命名语义下会怎样"，
不需要一个可用的目标目录才能成立。

这样的取舍是有意的：如果因为目标碰巧不可用就丢掉名称 finding，用户会先修好目标、再跑一次，
然后才第一次看到源里本来就存在的问题。

因此"源扫描错误 + 源名称错误 + 目标不可用错误"**可以同时出现**，目标失败不会丢掉已经知道的源问题。

## 7. 与 TargetProbeIssue 的关系

- 非 blocking 的 probe issue —— `VolumePathQueryFailed`、`FreeSpaceQueryFailed`、
  `VolumeInformationQueryFailed` —— **不**逐条直接变成 finding。
  它们各自只导致**一项** capability 变为 Unknown，用户通过 §4.3 的 coverage Info 得知"这项检查没做成"。
- 目的：避免同一件事出现两份用户可见信息（"Win32 API 报错" + "该能力 Unknown"）。
  `TargetProbeIssue` 保留其技术诊断用途（kind / affected path / Win32 入口点名 / native error code），
  仍是可比较的普通值对象，供日志与后续里程碑使用。
- 唯一的例外是 blocking 的两类，它们本身就是"目标不能用作目标"这一结论（§4.2）。
- M6 的"检查失败"（`ExistingObjectObservation.CheckFailed`）遵循的是**另一条**原则，值得写清楚：
  一个映射位置的属性查询没作答时，**会**为该位置产出一条 `TARGET_EXISTING_OBJECT_CHECK_FAILED`
  （Warning，§4.12）。原因不是"把 Win32 错误直接抛给用户"，而是**事实不同**：M3 的一次查询失败只会让
  *一项 capability* 变成 Unknown，而一项 capability 的 Unknown 已经由 coverage Info 完整表达；
  但 M6 的失败是**针对一个具体映射位置**的，若只汇总成一条覆盖说明，用户就无法知道**是哪个位置**
  没查成，而那正是他需要去确认的东西。两者都只陈述"事实仍是 Unknown"，都**不**退化成"不存在"，
  文本也都**不**变成"无法写入"。

## 8. 文本语言与确定性

- 用户可见文本当前使用**英文**，与仓库内既有的诊断字符串（M2 的 scan issue diagnostic、
  M3 的 probe diagnostic、异常消息）一致。**不为 M4 建立 localization 框架。**
- 规则输出必须**确定**：相同输入必然产生相同文本。因此：
  - 源扫描失败 finding 里描述证据时只使用**与语言无关**的 `ExceptionTypeName` 与 `HResult`
    （如 `System.UnauthorizedAccessException, HResult 0x80070005`），
    **不**把 `SourceScanIssue.Diagnostic` 里那条本地化的异常消息嵌进 finding 文本
    （与 M3 对 `TargetProbeIssue.Diagnostic` 的处理原则一致）。
  - 数字格式化一律走 invariant 方式，不依赖当前区域设置。
- M5 的名称规则遵守同一条原则：控件字符与保留名的匹配结果只取决于字符串本身，
  `COM1`–`COM9` / `LPT1`–`LPT9` 由字符拼接构造而不是格式化整数，因此不受区域设置影响。
- 不使用 AI、不联网、不生成长篇教育文章、不使用过度绝对的措辞。`Finding.Title` 保持短。

## 9. 纯计算边界

规则层**没有**任何 I/O：

- 不 `File.Exists` / `Directory.Exists` / `GetFileAttributes`，不枚举目标树
- 不重新扫描源，不重新探测目标
- 不使用 `Directory` / `File` 的写 API，不创建任何 probe 文件

M5 **没有**改变这一点：名称规则与大小写碰撞规则只读 `SourceScanResult` 里已有的字符串，
既不检查目标对象是否存在，也不打开任何目标 handle，也没有引入 `TargetCollisionProbe` /
`TargetEntryProbe` 这类东西。

M6 **也没有**改变这一点：`TargetExistingObjectRule` 只消费传进来的 `TargetExistingObjectProbeResult`，
不自己调用任何 Win32 API、不打开任何路径、不判断任何对象是否存在。做那件事的是**另一个**组件
（`WindowsTargetExistingObjectProbe`，见 [`TARGET_PROBING.md`](TARGET_PROBING.md)），它位于探测层，
发生在评估之前；规则层与它之间只通过"事实值对象"耦合。因此"两参数 `Evaluate` 会产出
`TARGET_EXISTING_OBJECT_CHECK_NOT_PERFORMED`"这件事，是规则层如实陈述"没有检查事实传进来"，
而不是规则层自己去补一次检查。

测试直接验证这一点：把目标目录指向一个**确定不存在**的随机路径，target-dependent 规则
依然按输入声明的事实产生完全相同的 finding，名称规则同样照常产生。空间的 Error 化被明确推迟，
正是因为那需要目标冲突等**新的观测事实**，而不是规则层去偷偷读磁盘。

## 10. 本阶段明确未实现的规则（延期）

以下内容**有意**不在 M4 / M5 / M6 范围内，不要顺手加入：

- **overwrite / skip / keep-both 策略引擎**：M6 只报告"目标位置已被占用"，**不**决定复制程序会怎么做，
  也**不**据此得出"会覆盖 / 会跳过"的结论
- **目标内容比较 / 去重**：不读取任何一个文件的内容，不做 hash，不判断两个文件是否相同
- **按已存在目标文件扣减空间**：M6 观测了存在性与种类，**没有**读取已存在对象的大小，
  因此空间规则（§4.8）不据此调整，仍然只是 Warning
- **目标大小写敏感性 probe**：M5 的碰撞规则（§4.10）只描述"源侧可能碰撞"，
  M6 **也**不探测目标目录的大小写敏感性
- **二阶大小写碰撞**：目标上**已存在**的对象与源名称仅大小写不同时，M6 不做专门判断
- **完整目标索引器 / 递归扫描整个目标**：M6 只按需查询映射位置与所需祖先，**不**枚举无关目标内容
- **Unicode normalization 深度兼容**：不做 normalization，名称按观察到的原样判断与查询
- **8.3 短名碰撞**、**Alternate Data Streams 检测**（`:` 只按普通保留字符处理）
- **reparse point 精确分类**（symlink / junction / mount point / other）与 **target 解析 / 跟随**：
  源侧（§4.7）与目标侧（§4.12）都只记录"这是 reparse point"这一事实
- **元数据丢失规则**（ACL / ADS / 时间戳精度等）
- **extended-length namespace 的相对路径安全框架**：M6 只为语义明确的长路径选择内部查询形式，
  不建立通用的路径安全抽象
- **FAT12 / FAT16 的文件上限**：M4 只在文件系统名恰好为 `FAT32` 时执行 §4.4。
  名称接近但不同的文件系统（例如只报告 `FAT`）不会被套用该规则——宁可漏报，也不猜
- UI 集成、规则插件系统、报告序列化

## 11. 与 M1 / M2 / M3 / M4 的关系

M4、M5 与 M6 **都没有修改**前几个里程碑的任何模型或契约：

- `Finding` / `FindingCode` / `Severity` / `CapabilityValue<T>` / `TargetCapabilities` /
  `SourceEntry` / `SourceScanResult` / `SourceScanIssue` / `SourceEntryKind` 全部原样使用。
- `SourceScanIssue` **没有**被加上 Severity（那仍属于规则层）。
- 扫描器与目标探测的 public contract 未变；规则层只消费它们的输出。
- 已发布的 16 个 FindingCode（M4 的 12 个 + M5 的 4 个）一个都没有改名、没有复用（§3 只做了追加）。
- M6 追加的 8 个 code（§3）同样只做追加，且**没有**修改任何既有 code 的拼写或含义。
- **`TargetCapabilities` 的三项没有被扩展**：M6 的逐项观测结果（每个映射位置上的对象）
  **不**塞进 `FileSystemName` / `AvailableFreeBytes` / `MaxPathComponentLength` 里。它们是与
  capability 无关的另一类事实，单独用 `TargetExistingObjectProbeResult` 表达。
- 规则层 API 的唯一变化是给 `WindowsPreflightEvaluator` **追加**了一个三参数重载；
  原来的两参数重载保留，且行为被明确定义为"未做检查"（§2）。
- M6 新增的观测类型（`TargetObjectPresence`、`ExistingObjectObservation`、
  `TargetExistingObjectProbeResult` 等）全部位于 **Windows 层的 `Probing/`**，
  **没有**进入 `CopyPreflight.Core`，Core 仍然是无 Win32 / 无 I/O 的领域模型。
- 没有新增第三方依赖，没有新增运行时项目；仍然是 Core / Windows / App / Tests 四个项目。

**M8 与本节的关系。** M8 是手工验收修正，它**首次**最小地改动了 Core 的**扫描侧**契约（不是规则层语义）：
`InvalidSourceRootException` 带上"已解析路径"，`SourceScanResult` 增加
`UnusableRootReason` / `HasUsableSourceRoot` 与 `ForUnusableRoot` 工厂，并把
`IsComplete` 收紧为 `Issues.Count == 0 && HasUsableSourceRoot`。理由与边界见
[`SCANNER.md`](SCANNER.md) §6.2——**扫描器的观察语义、错误策略、统计恒等式与 reparse 边界都没有变**，
变的只是"根在遍历开始前就被拒绝"这件事有了自己的表达。规则层这一侧**只是追加**：
一个 FindingCode 加一条 source-only 规则；M4/M5/M6 的 24 个 code、它们的 Severity 与文本一个都没有动。
