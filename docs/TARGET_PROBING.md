# CopyPreflight — 目标能力探测契约（M3）

> 本文件记录 M3 实现的目标能力探测（target capability probing）的**行为契约**：它问 Windows 什么、
> 如何表达回答、以及在问不到时如何诚实报告。
> 它不改变 [`PRODUCT_SPEC.md`](PRODUCT_SPEC.md) 的产品范围，只把目标探测这一层的语义写清楚。
> 状态：**M3 已冻结**。

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
- 目标不可用时，后续的卷与空间查询**不会被尝试**：没有可用目标就没有可探测的卷。M3 不为目标建立完整状态机。

## 6. Windows API 与语义选择

只使用完成 M3 所需的四个调用，全部为 Unicode `W` 版本：

| API | 用途 | 关键语义 |
| --- | --- | --- |
| `GetFileAttributesW` | 确认目标路径上存在可用的目录 | 只读属性查询；失败时保留 Windows 报的 native error |
| `GetVolumePathNameW` | 取得 Windows 认为该目标实际所属的 **volume mount point** | 不使用 `Path.GetPathRoot` |
| `GetDiskFreeSpaceExW` | 取得可用空间 | 只读 `lpFreeBytesAvailableToCaller` |
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

### 6.3 原生无符号量与 `long` / `int` 模型

native 可用空间是无符号 64 位，capability 模型存 `long`；组件长度是 `DWORD`，模型存 `int`。
超出可表示范围时**不 wrap、不 clamp、不截断**：该项变 Unknown，诊断里说明观察到的数值与
模型的表示上限。这不改变 M1 的 capability 模型。

组件长度报告为 `0` 时同样**不当作已知值**：`0` 不是可用的长度。

> 上述两种情况都不产生 issue：查询本身成功了，只是观察到的值无法进入模型。限制写在 capability
> 自身的诊断里，信息不会丢失。

## 7. 只读：不做目标可写性测试

**M3 明确禁止**：`File.Create`、`FileStream` 创建、临时 probe 文件、"创建再删除"、创建目录、
写 ADS、修改权限、修改 ACL。

因此探测器**不能、也不**声称"目标确定可写"。它能表达的上限是：

- 该路径当前可访问，且是一个目录
- Windows 对这些查询给出了回答
- 当前用户报告的可用空间是多少

**真正的写入能力没有通过任何写操作验证过。** 任何文档、UI 文案或结论都不得超出这一范围。

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
- 目标已有对象冲突、file ↔ directory 类型冲突
- 元数据丢失规则
- 生成任何 `Finding`
- 枚举系统全部 volume、WMI、物理磁盘型号 / 序列号
- NTFS feature flags（sparse / compression / encryption 等）
- 目标可写性验证
- WPF 集成

上述判定属于 M4 起的规则层。**M3 只返回事实。**
