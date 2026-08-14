# SourceTree 从最小化恢复时为何通常更快

## 结论

没有找到 Atlassian 一手资料证明 SourceTree 使用了“恢复时显示窗口截图”、专门的 restore 缓存，或完全跳过布局的特殊算法。更可靠的结论是：**SourceTree 的快速恢复来自一套长期保持 UI 可复用、可低成本重绘的架构，而不是某个最小化事件中的单点优化。**

这套架构主要包括：

1. Windows 版 SourceTree 使用 WPF。WPF 保存 visual tree 与绘制指令，由保留式 composition/rendering 系统响应重新绘制；常规 WPF 内容共享窗口的一个 HWND，不需要像复杂 WinForms 窗口那样让大量子 HWND 逐个完成窗口定位和绘制。
2. Atlassian 明确为 SourceTree 减少 visual tree 节点数，并把 diff、sidebar 等重型区域改成虚拟化控件或自绘实现。
3. File Status 与 Log/History 视图会缓存，不在切换时重建；Windows 版仓库本身也是常驻 tab 模型。
4. 本机 SourceTree 3.4.28 的可验证程序集仍是 WPF + ReactiveUI + Dragablz 架构；主窗口状态变化处理没有调用仓库 reload，应用重新激活时的 in-focus process 通过异步任务恢复。

相较之下，Duke 版当前探针把约 0.4—0.5 秒的主要停顿定位到 `FormBrowse` → `ToolStripContainer` → 多层 `SplitContainer` 的同步 `WM_WINDOWPOSCHANGED`/layout 级联，而非 revision grid 的单次绘制。两者的主要结构差异不是“SourceTree 不做布局”，而是 **SourceTree 没有走同样的大量 WinForms 子 HWND 定位链，并且刻意压低了仍需参与 WPF 布局与合成的视觉元素数量。**

## 证据边界

| 分类 | 本文可以确认什么 |
| --- | --- |
| Atlassian 公开确认 | WPF、减少 visual tree、虚拟化 diff/sidebar、自绘 log pills、缓存 File Status 与 Log/History、优化 tab 首次加载 |
| 本机 3.4.28 可验证观察 | 当前安装仍是 WPF；主窗口、repo panel、repo tabs 的类型关系；窗口状态处理不 reload repo；激活任务的异步恢复方式 |
| Microsoft 框架事实 | WPF 保留 visual/drawing tree、常规内容共享一个 HWND、后台 rendering thread、WPF layout 仍然递归且可能昂贵 |
| 合理推断 | 上述机制组合使已有工作区在窗口恢复时更容易快速重新合成，并避开 Git Extensions 当前测到的子 HWND 布局级联 |
| 未知 | SourceTree 3.4.28 从任务栏点击到完整首帧的内部时间线、是否还有未公开的恢复优化、不同机器/仓库上的确切耗时 |

本次没有对 SourceTree 注入探针或做 ETW trace，因此不能把用户体感当作毫秒级测量，也不能声称已经反编译出完整源码。

## 1. WPF 的显示模型与当前 WinForms 路径不同

Microsoft 的 [WPF Architecture](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/advanced/wpf-architecture) 明确说明，整个 visual tree 和 drawing instructions 会被缓存；WPF 是 retained rendering system，composition system 可以在不阻塞等待用户代码回调的情况下重新绘制。Microsoft 的 [WPF Graphics Rendering Overview](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/graphics-multimedia/wpf-graphics-rendering-overview) 也把它与 Win32/GDI 的 immediate-mode 重绘路径直接对比。

这不是只有术语差异。Microsoft 的 [WPF and Win32 Interoperation](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/advanced/wpf-and-win32-interoperation) 说明：WPF `Window` 创建一个顶层 HWND，窗口内其余常规 WPF 内容共享该 HWND；菜单、下拉框、popup 以及显式 `HwndHost` 是例外。Microsoft 的 [WPF Threading Model](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/advanced/threading-model) 则说明典型 WPF 应用有 UI thread 与隐藏的 rendering thread。

因此，对于一个仍常驻内存、没有发生内容变化的仓库页面，WPF 已经拥有可重新合成的视觉与绘制状态。最小化恢复仍可能触发 measure/arrange，但常规控件不会分别作为一棵 Win32 child-window tree 接收和同步传播 `WM_WINDOWPOSCHANGED`、`WM_ERASEBKGND`、`WM_PAINT`。

这与 Duke 版已测路径形成直接对照：`FormBrowse` 下探测到 112 个托管及原生子 HWND，恢复时 `toolPanel` 与多层 `SplitContainer` 同步传播 bounds/layout，子 HWND 再陆续擦除背景和绘制。详细数据见 [Git Extensions 窗口恢复性能调查](gitextensions-window-restore-performance.md#本地子-hwnd-探针结果)。

### WPF 本身并不保证快

Atlassian 自己在 2017 年的 [SourceTree 2.0 performance 说明](https://www.atlassian.com/blog/sourcetree/sourcetree-2-0-for-windows-3x-faster-than-sourcetree-1-9) 中承认，WPF 元素变化通常仍会触发布局和重绘；visual tree 复杂时，用户会感到 stutter。

Microsoft 的 [WPF Layout](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/advanced/layout) 同样说明 layout 是递归过程，每次调用都会处理 children，并建议避免不必要的递归更新、避免不必要的 `UpdateLayout`、对大集合使用虚拟化。

所以“SourceTree 快，因为它用了 WPF”只说对了一半。更重要的是 Atlassian 在 WPF 之上主动控制了视觉树和视图生命周期。

## 2. Atlassian 明确做过哪些 UI 性能工程

Atlassian 的 [SourceTree 2.0 for Windows – 3x faster](https://www.atlassian.com/blog/sourcetree/sourcetree-2-0-for-windows-3x-faster-than-sourcetree-1-9) 给出了最直接的一手说明：

- 在其 CoreFX 仓库示例中，visual tree elements 从 1.9/1.10 的 1760 个降到 2.0 的 1414 个，约减少 19.7%。
- file diff 被改写为自定义虚拟化文本控件，只有可见元素进入树。
- sidebar 被改写为自定义虚拟化 tree view。
- log view 的 pills 从独立 visual elements 改成 custom draw。
- File Status 与 Log/History 视图从 2.0 起不再在切换时重建，而是缓存已有视图，消除重新刷新 file list 的停顿。

Atlassian 的 [2.0 发布说明](https://www.atlassian.com/blog/sourcetree/sourcetree-for-windows-2-0-new-ui-faster-performance-and-microsoft-git-virtual-file-system-support) 还确认它重构了 bookmarks sidebar、改造了 tabs，并把这些工作与 UI 性能提升直接关联。

后续 [SourceTree 2.4 performance 说明](https://www.atlassian.com/blog/sourcetree/making-you-faster-with-sourcetree-2-4-for-windows) 记录了 tab 首次加载再减少约 100 ms，并通过 Process Log 定位长时间、孤儿或重复后台进程。这说明其策略不仅是“框架自然更快”，而是持续测量并压缩 UI 与后台工作的首次响应成本。

Atlassian 当前的 [Windows repository tabs 支持文档](https://support.atlassian.com/sourcetree/kb/viewing-and-maneuvering-around-repository-tabs-windows/) 也确认可以同时保留多个已打开仓库 tab，并在 tab 过多时滚动或列出全部已打开仓库。这种常驻 tab 产品模型为复用 repo view/state 提供了基础；但该文档没有承诺所有 tab 的完整 visual tree 永不卸载，因此不能进一步推断隐藏 tab 的所有控件始终 instantiated。

## 3. 本机 SourceTree 3.4.28 的只读验证

检查对象：

- 路径：`%LOCALAPPDATA%\SourceTree\app-3.4.28\SourceTree.exe`
- `FileVersion`: `3.4.28.0`
- `ProductVersion`: `3.4.28`
- `CompanyName`: `Atlassian`
- SHA-256: `35480BA87C6BFB492BC91B215043CE0F9EE86BA6919EF1CA54ECB188D1CDC058`

通过安装目录自带的 `Mono.Cecil.dll` 只读解析元数据和 IL，得到以下观察：

### UI 技术栈仍是 WPF

- target framework 为 `.NETFramework,Version=v4.8`。
- `SourceTree.exe` 引用 `WindowsBase`、`PresentationFramework`、`PresentationCore`、`System.Xaml`、`System.Reactive`、`ReactiveUI`、`Dragablz` 与 `SourceTree.Api.UI.Wpf`。
- 主窗口 `SourceTree.MainWindow` 继承 `SourceTree.UI.Theme.Wpf.Controls.PerMonitorDpiWindow`。
- 主窗口字段 `LayoutRoot` 是 `System.Windows.Controls.Grid`，`RepoTabPanel` 是 `SourceTree.View.Repo.RepoTabPanel`。
- `RepoPanel` 与 `RepoTabPanel` 都继承 `System.Windows.Controls.UserControl`。
- repo tab 主控件 `STDragableTabControl` 继承 `Dragablz.TabablzControl`。
- `Atlassian.FastTree.VirtualizingTreeView` 继承 WPF `ListBox`；安装目录还包含 `SourceTree.UI.CommitContainer.Wpf.dll`、多个 file-list WPF 组件和 `SourceTree.UI.Theme.Wpf.dll`。

这组证据把 2017 年 Atlassian 对 WPF 的公开说明延伸到了本机 3.4.28：至少当前主窗口、仓库 tab 和主要 UI 组件仍是同一类 WPF 架构，而不是后来迁移成 Electron 或 WinForms。

`SourceTree.exe` 虽然也引用 `System.Windows.Forms`，但主窗口与仓库视图的基类、字段和专用组件均为 WPF；单个兼容引用不能把应用归类为 WinForms。

### 窗口状态变化本身不 reload 仓库

`SourceTree.MainWindow.MainWindow_StateChanged(object, EventArgs)` 的 IL 只读取 `WindowState`，然后调整 `Maximize`/`Restore` 按钮的 `Visibility`，以及多个 resize rectangle 的 `IsHitTestVisible`。该方法没有调用 repository manager、repo refresh、file status refresh 或 tab reconstruction。

这不能证明所有恢复路径都绝无业务工作，但至少否定了“SourceTree 在最小化恢复的 `StateChanged` 处理里重建当前仓库 UI”这一具体假设。

### 回到前台后的 process 恢复不会整体同步等待

`SourceTree.AppRoot.OnActivated(EventArgs)` 会进入 `Activate()`。本机 IL 显示 `Activate()` 在完成少量同步工作后遍历 `IInFocusProcess`；每个 process 的回调是 `async void` state machine，调用 `IInFocusProcess.Start()` 并 `await` 返回的 `Task`。`OnDeactivated` 对应地调用并等待 `IInFocusProcess.Pause()`。

这意味着 activation handler 不会同步等待所有 in-focus process 完成后才返回。不过需保留两个边界：

- `Start()` 到第一次未完成的 `await` 之前仍可能运行同步代码；
- `Activate()` 本身还会触发 onboarding notification、枚举 repository，并关闭路径已失效的 tab。

因此只能说“后台/in-focus process 的完成被异步等待”，不能说 SourceTree 激活时完全不做同步工作。

## 4. 与 Git Extensions 当前恢复路径的结构对比

| 维度 | SourceTree Windows | Git Extensions Duke 版当前路径 |
| --- | --- | --- |
| UI 框架 | WPF retained-mode | WinForms/Win32 immediate child-window painting |
| 常规内容的 HWND 结构 | WPF Window 内常规内容共享一个 HWND | 探针观察到 112 个托管及原生子 HWND |
| 工作区容器 | WPF `Grid` + `UserControl` + tab | `ToolStripContainer` + 多层 docked `SplitContainer` |
| 大集合 | diff/sidebar 明确虚拟化；部分复杂元素 custom draw | revision grid/tree 各自绘制，但本次单次 paint 不是主热点 |
| 视图生命周期 | File Status 与 Log/History 明确缓存；repo tabs 常驻 | 当前也不是恢复时重建整个 `FormBrowse`，但 bounds/layout 会重新传播 |
| 最小化恢复事件 | 本机 `StateChanged` 只改窗口 chrome | 没有显式重建工作区，但 WinForms 自动布局链耗时约 0.4—0.5 秒 |
| 恢复可见方式 | 已缓存 visual/drawing tree 交给 composition/rendering system | 父子 HWND 完成定位后陆续擦除背景和 paint，呈现白屏逐步填充 |

这张表不意味着 SourceTree 完全没有 measure/arrange，也不意味着 WPF 在所有负载下都快。真正可迁移的经验是：

1. 保持 repo view 长期存在，避免因重新可见而 reconstruct/refresh。
2. 降低参与布局的元素数量，对超大列表只 materialize 可见项。
3. 对高密度装饰元素采用一次 custom draw，而不是大量 child elements/controls。
4. 避免在前台恢复的关键路径同步等待后台扫描。
5. 最重要的是，避免根尺寸变化被多层通用容器重复同步传播。

## 5. 对 Duke 版修复方向的含义

### 不值得照搬的方向

- **把整个应用迁移到 WPF**：能改变 HWND、布局和合成模型，但代价是全 UI 架构重写，远超一个恢复性能问题的合理范围。
- **给 revision grid/tree 继续加双缓冲**：当前三次探针中 revision grid 最慢单次 paint 约 25—28 ms，left tree 约 7 ms；它们不是约 0.5 秒停顿的主体。
- **假设 SourceTree 使用截图缓存并照做**：没有一手证据支持该假设。

### 值得借鉴的最小方向

1. 先把 `MainSplitContainer` 恢复时固定出现的两轮 bounds/layout 合并成一轮。
2. 再隔离 `ToolStripContainer:toolPanel` 自身的额外布局成本，判断能否让仓库工作区从它的通用布局链中脱离。
3. 保持当前 repo view 与数据模型常驻；不要用重建控件树换取表面上的恢复一致性。
4. 只有在布局级联缩短后 paint 成为新主因，才考虑更局部的虚拟化或 custom draw。
5. 将 activation 后的 Git 状态检查作为独立问题做异步、取消和结果过期设计，不要把它与纯 UI restore layout 混在同一修复中。

## 6. 未找到的证据与反例

- 未找到 Atlassian 官方文档称 SourceTree 使用 restore screenshot、离屏整窗位图缓存或跳过恢复布局。
- 未找到 SourceTree 3.4.28 的公开源码，因此本机结论来自程序集元数据和 IL，而不是源码级完整语义审计。
- Atlassian 公开 Jira [`SRCTREEWIN-9301`](https://jira.atlassian.com/browse/SRCTREEWIN-9301) 记录过 SourceTree 3.0.x 最小化后难以恢复的问题，并标记为在 3.0.9 修复。SourceTree 并非从未遇到窗口恢复缺陷；该问题是“经常无法恢复”，不是本文讨论的正常恢复首帧耗时。
- Atlassian Community 上也有 [activation 时长时间卡住](https://community.atlassian.com/forums/Sourcetree-questions/Sourcetree-locking-up-for-30-seconds-on-activation/qaq-p/764941) 的报告与 Atlassian 回复，涉及回到前台时同步文件系统/Git 状态。因此，仓库或 Git 操作很慢时 SourceTree 仍可能卡；本文只解释同等正常负载下 UI 恢复为何更容易快速呈现。

## 最终判断

对“SourceTree 是怎么做的”的最准确回答是：

> 它没有公开的最小化恢复特例；它把仓库 UI 做成常驻、缓存、低视觉复杂度的 WPF 视图。WPF 又保留 visual/drawing tree，并让常规内容共享一个 HWND，由 composition/rendering system 负责重新显示。因此恢复时主要是在重新合成和必要的 WPF 布局，而不是像 Git Extensions 当前这样，让 `ToolStripContainer` 与多层 `SplitContainer` 对大量子 HWND 同步传播两轮位置和布局，再逐个绘制。

这是一项框架能力与多年 UI 性能工程叠加的结果。对 Duke 版最现实的借鉴不是改用 WPF，而是**减少恢复关键路径上的容器层级与重复布局，并继续保持工作区视图常驻。**
