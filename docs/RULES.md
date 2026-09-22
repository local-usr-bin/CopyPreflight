# CopyPreflight — 规则评估契约（M4 / M5）

> **状态：M4 与 M5 契约已冻结。** 本文件记录规则层当前**实际实现**的规则：它们的输入、Severity、
> 触发条件、边界，以及为什么某些结论只能是 Warning 或 Info。它不描述产品范围（见
> [`PRODUCT_SPEC.md`](PRODUCT_SPEC.md)），也不描述各层职责（见 [`ARCHITECTURE.md`](ARCHITECTURE.md)）。

规则层是唯一把 source facts 与 target capabilities 组合起来判断的地方。M4 是它第一次真正产出
用户可见的 `Finding`；M5 在其中加入了 Windows 命名兼容性规则，而规则层仍然是纯计算。

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

这 16 个 code 被测试逐个钉住（`WindowsPreflightFindingCodesTests`），并且有一条测试证明
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

## 5. 为什么这些结论停留在 Warning

### 5.1 为什么可用空间规则当前只能是 Warning（M4）

这不是保守措辞，而是本轮**已知事实的上限**。M4 的规则层没有：

- target collision probing（目标是否已存在同名对象）
- overwrite / skip 策略（复制程序会覆盖、跳过还是报错）
- 已存在目标文件的分析
- reparse point 的实际复制行为
- sparse / compression 的真实分配语义（逻辑大小 ≠ 占用空间）

以上任何一项都可能显著改变真实的空间需求。在这些事实缺失时给出 Error，等于声称比已观测事实
更强的确定性。等目标冲突分析完成后，可以再设计更确定的 Error 条件。

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

1. `SourceScanIssueRule` —— 源侧，始终执行
2. `ReparsePointRule` —— 源侧，始终执行
3. `WindowsNameCompatibilityRule` —— 源侧，始终执行（M5）
4. `SourceCaseCollisionRule` —— 源侧，始终执行（M5）
5. `TargetPathBlockerRule` —— 若目标被拒绝，产出 blocking Error 后**立即返回**
6. `TargetCapabilityCoverageRule` —— 仅目标可用时
7. `Fat32MaxFileSizeRule`
8. `TargetPathComponentRule`
9. `TraditionalPathLengthRule`
10. `TargetFreeSpaceRule`

分组依据是"这条规则是否依赖目标"：

- **source-only** 规则（1–4）描述源自身的事实，目标如何都成立，**始终执行**。
- **target-dependent** 规则（6–10）在目标被拒绝时**全部跳过**：在一个不能作为目标目录的路径下
  拼目标路径，或推演一个根本不会发生迁移的目标文件系统能力，都是在描述一件不会发生的事。

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

测试直接验证这一点：把目标目录指向一个**确定不存在**的随机路径，target-dependent 规则
依然按输入声明的事实产生完全相同的 finding，名称规则同样照常产生。空间的 Error 化被明确推迟，
正是因为那需要目标冲突等**新的观测事实**，而不是规则层去偷偷读磁盘。

## 10. 本阶段明确未实现的规则（延期）

以下内容**有意**不在 M4 / M5 范围内，不要顺手加入：

- **目标冲突**：目标 `relative-path` 是否已存在、file ↔ directory 类型冲突
  （需要目标侧 I/O，属于后续 milestone）
- **目标大小写敏感性 probe**：M5 的碰撞规则只描述"可能碰撞"，不去 probe 目标
- **Unicode normalization 深度兼容**：不做 normalization，名称按观察到的原样判断
- **8.3 短名碰撞**、**Alternate Data Streams 检测**（`:` 只按普通保留字符处理）
- **reparse point 精确分类**（symlink / junction / mount point / other）
- **元数据丢失规则**（ACL / ADS / 时间戳精度等）
- **FAT12 / FAT16 的文件上限**：M4 只在文件系统名恰好为 `FAT32` 时执行 §4.4。
  名称接近但不同的文件系统（例如只报告 `FAT`）不会被套用该规则——宁可漏报，也不猜
- UI 集成、规则插件系统、报告序列化

## 11. 与 M1 / M2 / M3 / M4 的关系

M4 与 M5 **都没有修改**前几个里程碑的任何模型或契约：

- `Finding` / `FindingCode` / `Severity` / `CapabilityValue<T>` / `TargetCapabilities` /
  `SourceEntry` / `SourceScanResult` / `SourceScanIssue` / `SourceEntryKind` 全部原样使用。
- `SourceScanIssue` **没有**被加上 Severity（那仍属于规则层）。
- 扫描器与目标探测的 public contract 未变；规则层只消费它们的输出。
- M4 已发布的 12 个 FindingCode 一个都没有改名、没有复用（§3 只做了追加）。
- 没有新增第三方依赖，没有新增运行时项目；仍然是 Core / Windows / App / Tests 四个项目。
