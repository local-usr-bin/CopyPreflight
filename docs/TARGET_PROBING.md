# CopyPreflight — 目标能力探测契约（M3 / M6）

> 本文件记录目标探测的**行为契约**：它问 Windows 什么、如何表达回答、以及在问不到时如何诚实报告。
> 它不改变 [`PRODUCT_SPEC.md`](PRODUCT_SPEC.md) 的产品范围，只把目标探测这一层的语义写清楚。
> 状态：**M3（能力探测）与 M6（已存在对象探测）均已冻结。**
>
> - §1–§12 描述 M3 的能力探测（`WindowsTargetCapabilityProbe`）。
> - §13 描述 M6 新增的已存在对象探测（`WindowsTargetExistingObjectProbe`），它与能力探测并列、
>   同样只读、同样不产出 `Finding`。

## 1. 职责

目标探测器回答唯一一个问题：

> **"对于用户选定的目标目录，Windows 当前能够可靠告诉我们什么？"**

它产出能力值（已知 / 显式 Unknown）与探测失败记录。它**不判断**这些事实对某次迁移意味着什么，因此：

- 不产生 [`Finding`](../src/CopyPreflight.Core/Findings/Finding.cs)，也不带 `Severity`
- 不生成任何面向用户的建议文字
- 不读取源目录，不评估任何源对象
- **不向目标目录写入任何内容**（见 §7）

它回答的是"我探测到了什么"和"哪些探测失败了"。判断"这意味着 Error / Warning / Info"属于规则层。

## 2. 只探测三个能力

M3 只探测 M1 已冻结的三个 capability，且它们**互相独立**：

| Capability | 类型 | 含义 |
| --- | --- | --- |
| `FileSystemName` | `CapabilityValue<FileSystemName>` | 目标卷上的文件系统名称，按 Windows 报告的原样 |
| `AvailableFreeBytes` | `CapabilityValue<long>` | 当前调用用户在目标上**仍可使用的**字节数 |
| `MaxPathComponentLength` | `CapabilityValue<int>` | 目标文件系统接受的单个路径组件（文件 / 目录名）最大长度 |

每一项单独处于 `Known(value)` 或 `Unknown(reason)`。**不存在**"部分已知"这种中间态，
也不存在任何隐藏的默认值。

## 3. Known / Unknown 原则

- 无法确定的能力保持 **Unknown**，绝不退化为 `false` / `0` / 空字符串 / `default(T)`。
- **猜默认值是被禁止的**：不在 `GetVolumeInformationW` 失败后按盘符类型猜 NTFS，不在网络盘上默认猜 NTFS，
  不使用"Windows 本地盘通常是 NTFS"这类启发式，也不在查不到时硬编码 255。
- **一个查询失败不得丢掉另一个已经成功的 capability。** 每项能力各自探测，失败只影响它自己。
- Unknown 携带一段**诊断**说明为什么不知道。诊断是解释，不是替代值。
- 成功取得 metadata **不代表**目标可写、可容纳或可迁移。见 §7。

## 4. 部分成功是合法结果（M3 最关键的语义）

**典型合法结果**：

```text
AvailableFreeBytes   = Known(...)
FileSystemName       = Unknown(...)
MaxPathComponentLength = Unknown(...)
```

这在 SMB / NAS 等目标上可能完全正常：Windows 的 `GetDiskFreeSpaceExW` 对网络目标通常可用，
而 `GetVolumeInformationW` 在某些共享上不受支持或会失败。这不是错误，探测器不因此抛异常、
也不丢弃已经取得的可用空间。

因此：

- `GetVolumeInformationW` 失败 → 只有 `FileSystemName` 与 `MaxPathComponentLength` 变 Unknown，
  `AvailableFreeBytes` 保持它已经取得的值。
- `GetDiskFreeSpaceExW` 失败 → 只有 `AvailableFreeBytes` 变 Unknown，另外两项保持已知。
- `GetVolumePathNameW` 失败 → 卷无法确定，两项卷相关能力变 Unknown，`AvailableFreeBytes` 不受影响。

一次调用回答的两项能力也彼此独立：卷报告了名称但没有可用的组件长度时，结果就是"名称已知、长度 Unknown"。

## 5. 输入路径语义

target path 预期为**目标目录**。

| 情形 | 行为 |
| --- | --- |
| `null` / 空 / 全空白 | `ArgumentException` |
| 相对路径 | 按进程当前目录解析 |
| `Path.GetFullPath` 无法解析的畸形路径 | 抛该 API 自己的异常（`ArgumentException` / `NotSupportedException` / `PathTooLongException`） |
| 名称语法非法（Windows 报 `ERROR_INVALID_NAME`） | `TargetPathInvalid` issue，三项能力全 Unknown |
| 不存在 / 无法访问 / 网络不可达 / 权限拒绝 | `TargetPathUnavailable` issue，三项能力全 Unknown |
| 存在但不是目录 | `TargetPathUnavailable` issue，三项能力全 Unknown |
| 存在且是目录 | 正常探测三项能力 |

要点：

- 目标不可用**不是异常**，而是"这个目标不可用"这一事实。探测结果仍然返回，只是三项能力都是 Unknown。
- 名称非法与"名字合法但通向虚无"是**两种不同事实**，Windows 自己就区分它们（`ERROR_INVALID_NAME` vs
  `ERROR_FILE_NOT_FOUND` / `ERROR_PATH_NOT_FOUND` / 网络错误码），因此分开报告而不是合并。
- 目标不可用时只产生**一个** issue：不可达不同时算作一次"可用空间查询失败"，同一件事不重复计数。
- 目标不可用时，卷相关查询**不会被尝试**：`GetVolumePathNameW` 只看名字就能给出 root，对不可达目标问它
  会描述一个目标从未到达的卷。M3 不为目标建立完整状态机。

### 5.1 "可用目录"如何判定：只采信正面证据

**一项查询失败，不等于目标不可用。**判定按以下顺序进行：

1. 先问 `GetFileAttributesW`（对象的身份与类型）：
   - **成功**且是目录 → 目标可用，继续。
   - **成功**但不是目录 → 目标不可用（`TargetPathUnavailable`），停止。
   - **失败** → **不作结论**，继续（原因见 §6.4）。
2. 再问 `GetDiskFreeSpaceExW`（可用空间，见 §6.2）：
   - 成功 → 取得 `AvailableFreeBytes`；并且"Windows 对一个目录回答了这个问题"本身就是目标可达的正面证据。
   - 失败 → 记录 `FreeSpaceQueryFailed`，但**不因此停止**。
3. 若第 1 步没得到"是目录"、第 2 步也没有回答，则**没有任何正面证据**表明此处存在可用目录：
   报 `TargetPathUnavailable`（名称为 `ERROR_INVALID_NAME` 时是 `TargetPathInvalid`），三项能力全 Unknown，
   并且不再问卷。
4. 否则目标可用，卷相关查询照常进行，三项能力仍各自独立。

因此判据是：**目标可用** ⇔ `GetFileAttributesW` 报告了目录 **或** `GetDiskFreeSpaceExW` 作出了回答；
**目标不可用** ⇔ 有正面证据表明存在但不是目录，**或** 完全没有任何正面证据。

`GetVolumePathNameW` **不参与**这个判定：它对不存在的路径也能给出 root
（实测 `C:\cp-no-such-dir-xyz` → `C:\`），是纯语法解析，不构成可达性证据。若把它当作可用性判据，
不存在的路径会因为卷被解析出来而错误地报告 `FileSystemName = Known(...)`。

## 6. Windows API 与语义选择

只使用完成 M3 所需的四个调用，全部为 Unicode `W` 版本：

| API | 用途 | 关键语义 |
| --- | --- | --- |
| `GetFileAttributesW` | 确认目标路径上对象的身份与类型（目录 / 非目录） | 只读属性查询；**失败不等于目标不可用**，见 §6.4 |
| `GetVolumePathNameW` | 取得 Windows 认为该目标实际所属的 **volume mount point** | 不使用 `Path.GetPathRoot`；对不可达路径也作答，因此不作为可用性判据 |
| `GetDiskFreeSpaceExW` | 取得可用空间 | 只读 `lpFreeBytesAvailableToCaller`；只对目录作答，故同时是可达性证据 |
| `GetVolumeInformationW` | 取得文件系统名称与组件长度上限 | 只读 `lpFileSystemNameBuffer` / `lpMaximumComponentLength` |

### 6.1 为什么 volume root 用 `GetVolumePathNameW` 而不是 `Path.GetPathRoot`

`Path.GetPathRoot` 只是**字符串**操作：它把 `C:\Mount\Vol\Data` 的 root 认成 `C:\`，
但该目标实际可能在另一个卷上。mounted folder / junction 场景下两者答案不同，而 UNC 目标具有
share 语义，也不是一个盘符 root 能表达的。

Windows 自己的 mount point 解析才是"这个路径实际属于哪个卷"的答案。该 API 失败时**记录失败**，
不猜 root。

> 实测确认 volume root 是**必须**的：`GetVolumeInformationW` 对卷内子目录直接失败
> （`ERROR_DIR_NOT_ROOT`，即"目录不是根目录下的子目录"），只有卷根可用。因此传给它的必须是
> `GetVolumePathNameW` 报告的路径。

### 6.2 AvailableFreeBytes 用 available-to-caller

`GetDiskFreeSpaceExW` 有三个输出。**只取 `lpFreeBytesAvailableToCaller`**：

它是**当前调用用户**在配额约束下仍可使用的空间，也就是这次迁移真正能用的量。
`lpTotalNumberOfFreeBytes`（忽略配额、卷上物理剩余）**不被用作替代**。

UNC 语义：Windows 文档要求 UNC 名称带结尾反斜杠。探测器对**所有**目标统一保证路径以目录分隔符结尾
（对目录路径是幂等的），而不是只对 UNC 特判：这样本地、挂载点与网络目标走同一条代码路径，
并且不会把 UNC 路径转写成任何别的东西。

`GetDiskFreeSpaceExW` **官方支持 UNC share root 本身**（`\\Server\Share\`），所以
`\\NAS\Backup` 这样的目标可以被正常询问可用空间。这也是 share root 能通过 §5.1 判定为目标可用的原因。

### 6.3 原生无符号量与 `long` / `int` 模型

native 可用空间是无符号 64 位，capability 模型存 `long`；组件长度是 `DWORD`，模型存 `int`。
超出可表示范围时**不 wrap、不 clamp、不截断**：该项变 Unknown，诊断里说明观察到的数值与
模型的表示上限。这不改变 M1 的 capability 模型。

组件长度报告为 `0` 时同样**不当作已知值**：`0` 不是可用的长度。

> 上述两种情况都不产生 issue：查询本身成功了，只是观察到的值无法进入模型。限制写在 capability
> 自身的诊断里，信息不会丢失。

### 6.4 为什么 `GetFileAttributesW` 的失败不能当作"目标不可用"（M3 修正）

Microsoft 的 `GetFileAttributesW` 文档明确说明：直接对 network share root（例如 `\\Server\Share`）调用，
函数**会失败并返回 `ERROR_BAD_NETPATH`**，必须改为指定 share 下的子目录。

但 CopyPreflight 必须支持用户把 UNC share root 本身作为目标（例如 `\\NAS\Backup`）。因此：

- `GetFileAttributesW` 的**成功是结论性的**：对象存在，且能分辨它是不是目录；
- `GetFileAttributesW` 的**失败是非结论性的**：不得据此中止后续探测。

`GetDiskFreeSpaceExW` 与 `GetVolumePathNameW` 都具备 network share 语义，所以 share root 仍能被正常探测；
`GetDiskFreeSpaceExW` 恰好又只对目录作答（对文件返回 `ERROR_DIRECTORY`），因此它同时提供了
"此处存在可用目录"的正面证据（见 §5.1）。

`GetFileAttributesW` 因此仍然保留，但职责收窄为**正面证据**：区分"存在的目录"与"存在但不是目录的对象"，
并给出名称被拒（`ERROR_INVALID_NAME`）时的 native error。它的失败不再是一道闸门。

> 实测：本机 `GetFileAttributesW(@"\\localhost\C$")` 返回**成功**（loopback 特例），因此这条文档行为
> **无法**在本开发机上真实复现；它由 seam 测试以 `ERROR_BAD_NETPATH` 驱动验证。行为按 Microsoft
> 官方说明处理，不依赖本机巧合。

## 7. 只读：不做目标可写性测试

**M3 明确禁止**：`File.Create`、`FileStream` 创建、临时 probe 文件、"创建再删除"、创建目录、
写 ADS、修改权限、修改 ACL。

因此探测器**不能、也不**声称"目标确定可写"。它能表达的上限是：

- 该路径当前可访问，且是一个目录
- Windows 对这些查询给出了回答
- 当前用户报告的可用空间是多少

**真正的写入能力没有通过任何写操作验证过。** 任何文档、UI 文案或结论都不得超出这一范围。

**M6 的已存在对象探测遵守同一条只读边界**（见 §13.7）：它只调用只读的属性查询，同样**不**创建、
**不**写入、**不**删除、**不**重命名、**不**改权限、**不**读取任何文件内容。

## 8. Issue 模型

`TargetProbeIssue` 表达**探测失败的事实**，与 `Finding` 严格分开：没有 `Severity`、没有标题、没有建议。

| `TargetProbeIssueKind` | 含义 |
| --- | --- |
| `TargetPathInvalid` | Windows 拒绝该路径的名称语法 |
| `TargetPathUnavailable` | 名称合法，但无法到达可用的目录 |
| `VolumePathQueryFailed` | 无法确定目标所属卷，因此无法描述其文件系统 |
| `FreeSpaceQueryFailed` | 无法报告当前用户在目标上的可用空间 |
| `VolumeInformationQueryFailed` | 卷已确定，但 Windows 没有描述其上的文件系统 |

每个 issue 记录：

- `Kind` — 上面这张表
- `AffectedPath` — **失败操作实际被施加的路径**：目标级操作为规范化后的目标目录路径，卷级操作为 volume root
- `Operation` — 失败的操作，有 Win32 入口点时按入口点命名（如 `GetVolumeInformationW`）
- `NativeErrorCode` — Windows 报的错误码；**没有错误时报 `null`**（`ERROR_SUCCESS` 不是错误，不被伪造成错误码）
- `Diagnostic` — 简短、确定性的说明；不含本地化消息，便于跨语言环境比较

issue **不**长期保存异常对象，也**不**替代 capability 值：查询失败时对应能力是 Unknown，issue 只解释原因。

## 9. 平台边界

- `CopyPreflight.Windows` 是 **`net10.0-windows`** 程序集。
- `WindowsTargetCapabilityProbe` 标注 `[SupportedOSPlatform("windows")]`，因此平台契约由类型系统表达，
  调用方在非 Windows 目标上会被分析器指出，而不是运行到一半才发现。
- 没有通过关闭 analyzer、`NoWarn` 或 `#pragma` 来掩盖这条边界。
- `CopyPreflight.Core` 保持 `net10.0`：不含 Win32 P/Invoke，不引用 Windows 程序集。

## 10. 错误处理

- 只捕获本层**明确知道如何解释**的失败（Win32 错误码 / 已知异常）；`catch (Exception) { 全 Unknown }` 被禁止。
- 编程缺陷、`OutOfMemoryException`、`StackOverflowException` 等非预期异常一律向上传播。
- 每次查询独立处理：记录 native error → 对应能力 Unknown → 仍有意义的其他独立查询继续执行。

## 11. 实现边界

- 探测是**同步**的，不包装 `Task.Run`：本层不决定线程。
- 不引入任何 runtime 依赖：无第三方 interop 包（Vanara / CsWin32 等）、无 DI、无日志框架、
  无 virtual filesystem、无 plugin 接口、无 service locator。
- 为了在开发机上稳定复现无法真实制造的结果（网络目标的 volume-info 失败、超出模型的 native 值、
  组件长度为 0），`CopyPreflight.Windows` 内部有一个极小的 `IWindowsFileSystemApi` seam，
  只覆盖本层实际调用的四个查询。它**不是**虚拟文件系统，也不是通用 OS 抽象。

## 12. M3 明确不做（延期）

- source size 与可用空间的比较
- FAT32 单文件 4 GiB 上限
- 完整目标路径长度 / `MAX_PATH` 规则
- 单组件长度违规判定
- 文件名合法性 / 保留名称规则
- 元数据丢失规则
- 生成任何 `Finding`
- 枚举系统全部 volume、WMI、物理磁盘型号 / 序列号

> **目标已有对象冲突、file ↔ directory 类型冲突**原本列在这里，现已由 **M6** 实现（见 §13）。
> M6 实现的是"目标位置是否已被占用、被什么种类占用"的**只读观测**；它仍然**不**做
> overwrite / skip 策略、目标内容比较、按已存在目标文件扣减空间、目标大小写敏感性探测。

## 13. 目标已存在对象的只读探测（M6）

M6 在**同一个 Windows 层**新增了一个只读探测器 `WindowsTargetExistingObjectProbe`，回答第二个问题：

> **"按 `source\<relative-path> → target\<relative-path>` 这个固定映射，每个源对象的目标位置上
> 当前已有什么？"**

它与 M3 的能力探测器一致：**不产生 `Finding`**、不带 `Severity`、不生成面向用户的建议。它只产出事实——
`TargetExistingObjectProbeResult`，其中每个 source entry 对应一条 `ExistingObjectObservation`。
判断"这意味着 Error / Warning / Info"仍属于规则层（见 [`RULES.md`](RULES.md) §4.12）。

### 13.1 映射与查询范围

- 映射固定为 `Path.Join(targetDirectoryPath, relativePath)`。**不**在目标下再加一层源目录名，
  **不**通过改变映射来消解冲突。
- 只查询**每个 entry 自己的映射位置**以及**其所需祖先**；**不**递归扫描整个目标、**不**枚举无关目标内容、
  **不**重新扫描源。
- **父先于子**：即使 inventory 的到达顺序被打乱，也先解析所需祖先、再解析 leaf，因此结论与到达顺序无关。
- 一次运行内按**原始路径字符串 `Ordinal`** 缓存已解析的位置；**不**合并仅大小写不同的映射。
- 所需祖先可以包含**不在 inventory 里**的中间目录——它们是为解析映射而查询的，不影响映射本身。
- **一个位置只有一条观测**：同一个位置既可能作为它自己的 source entry 出现，也可能作为挡住更深分支的
  祖先出现，两条路径都只产生一条观测，同一个事实**不**会被说两遍。
- **挡住分支的那个普通文件会被记录在它自己的映射位置上**，即使 inventory 没有列出该目录：它是一个独立的
  位置事实，也是结构性冲突最终要被报告的地方。反过来，如果只在后代上留下"被某个祖先挡住"的记录，
  这条冲突就无处可报，整条分支会被无声吞掉。

### 13.2 事实模型

| `ExistingObjectObservationKind` | 含义 |
| --- | --- |
| `Observed` | 查询了该位置，那里有一个对象；`Presence` 说明是 File / Directory / ReparsePoint |
| `ObservedMissing` | 查询了该位置，**有直接证据**表明当前没有对象 |
| `InferredMissing` | 未查询该位置，但**已确证某个祖先缺失**，因此该位置不可能有对象——是**推导**，不是观测 |
| `CheckFailed` | 查询了该位置，但 Windows 没有作答 |
| `NotQueried` | 有意未查询；`GapReason` 说明原因 |

- **直接确证缺失（D）与由缺失祖先推出（E）分开表达**，两者都**不**伪装成"逐位置查询成功"。
- `Presence` **仅在 `Observed` 时非 null**；其余 kind 都没有对象可描述。
- **reparse point 先于目录判定**：一个 junction 同时带 `Directory` 与 `ReparsePoint` 属性，
  这里只记录 `ReparsePoint` 这一事实，**不分类**（symlink / junction / mount point / other）、
  **不解析 target**、**不跟随**。
- 每条观测保留证据：source relative path、被检查的映射位置、直接还是祖先推导、失败或跳过原因、
  适用的**原始 Win32 错误码**、阻塞祖先。**未查询**的项**不**伪造错误码。
- `MappedTargetPath` 永远是**映射位置**（`Path.Join` 的结果），**绝不**是内部查询形式。

### 13.3 读取方式与错误处理

- 用 `GetFileAttributesW`（复用 M3 已有的 `IWindowsFileSystemApi` seam，不新增 interop）。
- **先**分支"成功 vs `INVALID_FILE_ATTRIBUTES`"。**禁止**用
  `File.Exists == false && Directory.Exists == false` 作为"不存在"的证据。
- **只有 `ERROR_FILE_NOT_FOUND`(2)** 在"路径语义可靠且没有祖先阻断"时算作**缺失证据**。
  `ERROR_PATH_NOT_FOUND`(3) **不**无条件等于 leaf 缺失——它也可能是中间组件不存在。
- 访问被拒绝 / 共享冲突 / 网络错误 → **Unknown**（`CheckFailed`），事实不退化成"不存在"。
- 非法名称 / 超出长度 → **不是** Missing（属于无法按该名称查询，记覆盖缺口）。
- 只处理本层**明确知道如何解释**的路径 / 文件系统失败；**不**用大范围 catch 把取消或编程错误
  变成 Missing（与 §10 同一原则）。

### 13.4 父路径、分支与 UNC

- 所需祖先逐级判定：**普通目录 → 继续**；**是普通文件 → 结构性阻断，停止该分支**，根因出在该祖先处，
  其后代记录"被哪个祖先把分支挡住"；**已确证缺失的祖先 → 推导整支缺失**；
  **reparse 祖先 → 记录边界、停止、不解析**；**祖先检查失败 → Unknown 并停止该分支**。
- 被阻断分支里的后代**永不**默认成 Missing，而是保留"阻塞祖先 + 原因"。
- **阻断不依赖某个目录 entry 恰好存在**：源里某个 entry 的祖先必然是目录（文件下面不可能再有对象），
  因此目标在该位置上的普通文件就是结构性冲突，即使 inventory 只列了目录**里面**的对象。
- **互不依赖的分支继续**：一个分支失败不会让其他分支停摆。
- **UNC share root 保留 M3 的边界**：Microsoft 文档化的 `ERROR_BAD_NETPATH`(53) **不得**变成一道闸门——
  既**不**断言共享不存在，也**不**因此停止其下的子项；具体子项各自由自己的查询结果判定。
  **并非所有网络错误都是 share-root 例外**：share root 之下的一个真实网络失败仍然保持 Unknown。
- 一项 capability 为 Unknown（例如 `FileSystemName`）**不**阻断本探测；
  `GetVolumePathNameW` 成功**不**证明任何东西存在。

### 13.5 名称与长路径

- 保留**原始** `SourceEntry.RelativePath` 与目标根；**不** trim、**不**替换、
  **不**"归一化之后再查询"。查询的位置要么就是映射表达的位置，要么观测明确说"没有查询"。
- 非法字符 / 保留设备名 / 结尾空格或句点：保留 **M5 的 Finding**，本探测只记录"M6 未覆盖 + 原因"
  （`AmbiguousName`），**不**把 `report.` 归一化成 `report` 再查询。
- 长路径：语义明确的路径用**适当的内部查询形式**（extended-length；UNC 与盘符分别处理）。
  **不**机械加前缀、**不**改变语义、**不**重复前缀；`\\.\` ≠ `\\?\`；
  **绝不**把内部查询形式写回成原始目标路径。真正超出范围 → Unknown / 覆盖缺口，**不是** Missing。
  查询成功也**不**意味着其他工具不受 `MAX_PATH` 限制。

### 13.6 覆盖与取消

- `TargetExistingObjectProbeOutcome`：`NotPerformed` / `Completed` / `CompletedWithCoverageGaps` / `Cancelled`。
- 批量结果必须能区分"未执行 / 完成且完整覆盖 / 完成但有缺口 / 已取消"。
  **空结果集 ≠ 检查完整**；**取消的项永不默认成 Missing**；M6 的覆盖 ≠ M2 的完整枚举。
- `Completed` 的确切含义是：**本轮需要评估的位置都已经有了结论**——包括"某条分支被普通文件结构性阻断"
  这类**已确定**的分支结论，以及"由已确证缺失的祖先推导出的整支缺失"。它**不是**"每个位置都成功执行了
  一次属性查询"：被阻断的后代根本没有被查询，也**不**计入"查询成功"的数量；分母同样**不是** M2 的枚举总数。
- 取消遵循仓库既有模式：在入口与祖先边界检查 `CancellationToken`，传播 `OperationCanceledException`；
  **不**承诺中断阻塞中的 native 调用，**不**把取消吞成"完成"，也**不**起后台线程。
- 本层**不**承诺原子性 / 并发安全，也不做并发探测或重试风暴。

### 13.7 只读边界

与 M3 相同（见 §7）：**只读**。不复制、不移动、不删除、不重命名、不改权限、
**不**在源目录或目标目录写入任何 probe / 临时文件、**不**读取任何文件的内容、**不**计算 hash。

### 13.8 与规则层的关系

- 本层**不**决定 Severity，**不**产出 `Finding`。它把事实交给 `WindowsPreflightEvaluator`
  （经 `TargetExistingObjectRule`，见 [`RULES.md`](RULES.md) §4.12）。
- 逐项结果**不**放进 `TargetCapabilities` 的三项（`FileSystemName` / `AvailableFreeBytes` /
  `MaxPathComponentLength`）里；它们是与 capability 无关的另一类事实。
- NTFS feature flags（sparse / compression / encryption 等）
- 目标可写性验证
- WPF 集成

上述判定属于 M4 起的规则层。**M3 只返回事实。**
