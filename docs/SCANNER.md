# CopyPreflight — 源扫描器契约（M2）

> 本文件记录 M2 实现的源扫描器（source scanner）的**行为契约**：它观察什么、如何表达观察结果、
> 以及在观察不到时如何诚实报告。
> 它不改变 [`PRODUCT_SPEC.md`](PRODUCT_SPEC.md) 的产品范围，只把扫描这一层的语义写清楚。
> 状态：**M2 已冻结**。

## 1. 职责

扫描器回答唯一一个问题：

> **"源目录里我实际观察到了什么？"**

它产出 inventory（观察到的对象）、scan issue（观察失败）与统计。它**不判断**这些事实在目标上意味着什么，
因此：

- 不产生 [`Finding`](../src/CopyPreflight.Core/Findings/Finding.cs)，也不带 `Severity`
- 不生成任何面向用户的建议文字
- 不访问目标目录，不做任何目标能力探测
- 不打开文件、不读取文件内容、不计算 hash、不做可读性 probe
- 不向源目录或目标目录写入任何内容

**成功取得文件 metadata ≠ 证明文件内容可读。** 扫描器只读取目录列表与基础 metadata。

## 2. Source root

| 情形 | 行为 |
| --- | --- |
| `null` / 空 / 全空白 | `ArgumentException` |
| 输入的是相对路径 | 按进程当前目录解析 |
| 解析后不存在 | `InvalidSourceRootException`（`Reason = NotFound`） |
| 解析后存在但不是目录 | `InvalidSourceRootException`（`Reason = NotADirectory`） |
| 存在但无法枚举 | **不抛异常**：返回带 `CannotEnumerateDirectory` issue 的 partial result |
| 本身是 reparse point | 视为用户明确指定的根，照常枚举；不对它做特殊处理 |

先检查取消令牌，再校验 source root：预先取消的令牌会先抛 `OperationCanceledException`。

### 2.1 relative path 语义

- inventory 中的路径**永远相对于 source root**，不泄漏绝对根路径。
- 路径按观察到的原样保存：**不改大小写、不做 `\` ↔ `/` 转换、不做 Windows 文件名校验**。
- source root **自身永远不是 entry**，也不计入任何统计。
- 空源目录是合法结果：`0` files、`0` child directories、`0` logical bytes。

路径规范化、目标路径拼接、`MAX_PATH` 判断、文件名合法性属于后续里程碑，M2 不做。

## 3. 遍历策略

- 使用**显式 stack** 的迭代式遍历，不使用递归调用栈，因此极深的目录树不会导致栈溢出。
- **逐个目录**惰性枚举，一次只持有一个目录的枚举器 + 待访问目录的栈；
  不会先把整棵树的路径一次性读进内存。
- **不**使用 `SearchOption.AllDirectories` / `RecurseSubdirectories` 这类无法控制每层错误边界与
  reparse 行为的全自动递归。
- 不做**预扫描计数**，因此不伪造百分比，也不报告 ETA / 速度预测。
- 枚举顺序是文件系统的产物，**无业务含义**：不做排序，也不依赖顺序。

## 4. Reparse point 安全边界

遇到 `FileAttributes.ReparsePoint` 的对象时：

1. **保留事实**：产出 `SourceEntry.ReparsePoint(relativePath, kind)`，`IsReparsePoint == true`。
2. **不递归进入**：directory reparse point 不会被压入待访问栈。
3. **不解析、不跟随 target**：从不读取 `LinkTarget` / `ResolveLinkTarget`。
4. **不分类**：不判断它是 symlink、junction、mount point 还是别的类型，精确分类延期。
5. **不伪造长度**：不给 reparse 文件写 `0`，也不把它报告的长度当作自身长度——
   那个数值可能描述的是链接目标。M2 对该对象**不作长度归因**（`LengthInBytes == null`）。

`SourceEntry.Kind` 对 reparse point 表达的是文件系统报告的**外形**（file-shaped / directory-shaped），
不是 reparse point 的分类。

## 5. 统计语义

`SourceScanResult` 的计数**全部由 `Entries` 推导**，因此 summary 不可能与 inventory 矛盾。
三个计数互斥且穷尽：

| 计数 | 含义 |
| --- | --- |
| `FileCount` | 普通文件（`Kind == File` 且非 reparse point） |
| `DirectoryCount` | 普通目录（`Kind == Directory` 且非 reparse point），**不含 source root** |
| `ReparsePointCount` | reparse point，file-shaped 与 directory-shaped 都算 |
| `LogicalFileBytes` | 已归因的逻辑文件长度之和（即 `FileCount` 那些对象） |

不变量：

```text
FileCount + DirectoryCount + ReparsePointCount == Entries.Count
LogicalFileBytes == Entries.Where(e => e.LengthInBytes is not null).Sum(e => e.LengthInBytes)
```

reparse point 单独计数而不并入文件 / 目录，正是为了让上面第一条恒等成立、
且 `LogicalFileBytes` 不会悄悄排除已经被计入 `FileCount` 的对象。
一棵只由 reparse point 组成的树会得到 `ReparsePointCount > 0` 而 `LogicalFileBytes == 0`。

## 6. Scan issue 与 partial result

`SourceScanIssue` 表达**观察失败的事实**，与 `Finding` 严格分开：
它没有 `Severity`、没有标题、没有建议。判断"某个目录无法枚举"对某次迁移意味着 Error / Warning / Info，
属于规则层。

| `SourceScanIssueKind` | 含义 |
| --- | --- |
| `CannotEnumerateDirectory` | 某目录的子项无法列出，其内容未知 |
| `CannotReadEntryMetadata` | 某对象被列出，但描述它所需的基础 metadata 读不到，因此它不在 inventory 中 |

每个 issue 记录：kind、尽可能得到的 relative path（根目录自身的问题为 `null`）、
异常类型全名、`HResult`、简短 diagnostic。**异常对象本身不被长期保存**，也不建立庞大的异常分类体系。

### 6.1 错误策略

- 只捕获 `UnauthorizedAccessException`、`IOException`、`SecurityException`。
- **不使用** `catch (Exception) { continue; }`：参数错误、程序缺陷、`OutOfMemoryException` 等一律向上传播。
- `EnumerationOptions.IgnoreInaccessible` **不被使用**：访问失败必须被记录，不能静默跳过。
- 某个子目录失败时，记录 issue 并继续扫描仍然可访问的兄弟目录。
- 已成功观察到的内容不会因为后续失败而丢弃。
- **取消不是 issue**：取消必须抛 `OperationCanceledException`，不得被转换成 partial result。

`SourceScanResult.IsComplete` 等价于"没有任何 issue"，即"每个到达的目录都列得出来、
列表里每个对象的基础 metadata 都读得到"。它**不代表**文件内容可读，也不代表复制会成功。

## 7. 进度

`SourceScanProgress` 是"目前观察到哪里"的单调快照：

- 计数器：`EntriesObserved` / `FilesObserved` / `DirectoriesObserved` /
  `ReparsePointsObserved` / `LogicalBytesObserved` / `IssuesObserved`
- `CurrentRelativePath`：最近完成列举的目录的相对路径；在 source root 处为 `null`
- **没有**百分比、ETA、速度预测、GUI 状态或 WPF 类型

约束：

- 计数非负（由类型构造器强制）且扫描期间**不倒退**。
- 回调粒度为**目录级**：每个成功列举完的目录报告一次，因此回调次数与目录数同阶，而不是与 entry 数同阶。
- 一次扫描**至少报告一次**；最后一次快照与返回的 `SourceScanResult` 计数**完全一致**。

## 8. 取消

- 扫描开始前检查一次；遍历中每个目录检查一次；**每个被观察的 child 检查一次**。
- 因此即使单个目录包含极大量 entry，取消也能及时生效，而不是等它列完。
- 传播标准 `OperationCanceledException`，不返回看起来"成功"的结果，也不把它记为 issue。

## 9. 实现边界

- 扫描器是**同步**的，不包装 `Task.Run`：Core 不决定线程，由后续 UI / orchestration 决定在哪个线程运行。
- Core 目标框架保持 `net10.0`，不使用 `net10.0-windows`，不使用 Win32 handle / PInvoke。
- 不引入任何 runtime 依赖：无 `System.IO.Abstractions`、无 DI、无日志框架、无 Rx、无 Channels、
  无 PLINQ、无并行扫描、无数据库、无序列化器、无 MVVM 框架。
- 为了测试"列表失败"与"file-shaped reparse point"，Core 内部有一个极小的
  `ISourceDirectoryReader` seam；它**不是** virtual file system，也不是可插拔文件系统抽象。

## 10. M2 明确不做（延期）

target probing、RuleEngine、FAT32 单文件上限规则、可用空间规则、路径长度规则、命名规则、
冲突规则、WPF 集成、ADS、ACL、hash、文件内容完整读取、文件可读性 probe、
reparse tag / symlink / junction 的精确分类、Win32 handle / PInvoke。
