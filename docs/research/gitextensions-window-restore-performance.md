# Git Extensions 窗口恢复性能：官方行为与证据

## 结论

Git Extensions 官方上游并非没有注意“最小化后恢复不顺畅”这个问题。相反，官方从 2020 年起就有意在窗口最小化时暂停昂贵的 `git status`，2022—2023 年又连续处理“窗口无法恢复”和“恢复时异步 Git 命令干扰窗口激活”的问题。当前行为是这些修复后的折中：

- 最小化期间，周期性的 `GitStatusMonitor` 不运行，避免 CPU/IO 消耗。
- 主窗口重新激活时，仍检查 bisect、merge、rebase、patch 和冲突状态，以便显示准确的操作/冲突通知条。
- `git ls-files -z --unmerged` 在窗口仍处于 `Minimized` 状态时默认跳过，但在窗口恢复并进入 `Normal`/`Maximized` 后仍会执行。
- WinForms/Windows 在恢复、最大化时本来就会产生 resize、layout 和 paint；Git Extensions 的复杂控件树因此会重排、重绘。这会造成“像 reload”的观感，但没有证据表明官方在恢复时故意重新加载完整提交历史。

因此，“官方为什么没做这个基础优化”的准确回答是：**官方已经优化了最危险、最昂贵的一部分，而且确实尝试过把激活检查异步化；但该尝试在 WinForms/JoinableTask 的激活时序上无法稳定工作，并使 CI UI 测试挂死，最终没有合并。维护者明确把现存实现称为 workaround；截至官方基线 `v7.2.0`，大仓库上的 `git ls-files -z --unmerged` 激活卡顿仍有一条开放 Issue。**

## 调查范围与基线

本地源码核对以 Duke 版已经吸收并验证的官方基线 `v7.2.0` / `501f831ed25127e4a301b7649d5d4e6524f53bba` 为准。相关 `FormBrowse`、`InteractiveGitActionControl`、`GitStatusMonitor` 和 `GitExtensionsFormBase` 在 Duke 版没有产品代码差异，因此下文可以视为官方基线行为。

网络资料仅采用以下一手来源：

- `gitextensions/gitextensions` 官方仓库的源码、提交、Issue 和 PR；
- Git Extensions 官方 4.1 文档；
- Microsoft Learn 的 WinForms/Win32 文档。

## 确认事实

### 1. 激活窗口时的状态检查是有意设计，历史可追溯到 2014 年

官方提交 [`381f750` — CheckForMergeConflicts on activate window](https://github.com/gitextensions/gitextensions/commit/381f75096a13a9a674ecbda35bc89ac052b5e25f) 在 2014 年把 merge-conflict、stash 和 submodule 更新统一放到 `OnActivate()`，并从 `FormBrowse.Activated` 调用。当前实现缩小为 bisect 与 Git action 通知条检查：

- [`FormBrowse.OnActivated`](https://github.com/gitextensions/gitextensions/blob/501f831ed25127e4a301b7649d5d4e6524f53bba/src/app/GitUI/CommandsDialogs/FormBrowse.cs#L560-L577) 在每次主窗体激活时调度 `OnActivate()`；
- [`FormBrowse.OnActivate`](https://github.com/gitextensions/gitextensions/blob/501f831ed25127e4a301b7649d5d4e6524f53bba/src/app/GitUI/CommandsDialogs/FormBrowse.cs#L1174-L1182) 刷新 bisect 和 merge/rebase/patch/conflict 通知；
- [`InteractiveGitActionControl.RefreshGitAction`](https://github.com/gitextensions/gitextensions/blob/501f831ed25127e4a301b7649d5d4e6524f53bba/src/app/GitUI/UserControls/InteractiveGitActionControl.cs#L63-L108) 的 XML 注释直接说明用途是“after reactivation”刷新 revision grid 上方的 banner；
- 冲突检查走 [`GitModule.InTheMiddleOfConflictedMerge`](https://github.com/gitextensions/gitextensions/blob/501f831ed25127e4a301b7649d5d4e6524f53bba/src/app/GitCommands/Git/GitModule.cs#L503-L516)，执行 `git ls-files -z --unmerged`；rebase、merge、patch、bisect 本身主要是文件/目录存在性检查。

这说明激活检查的产品目的不是重新加载提交图，而是发现用户在 Git Extensions 失去焦点期间由其他程序或终端造成的仓库操作状态变化。Microsoft 的 [`Form.Activated` 文档](https://learn.microsoft.com/en-us/dotnet/api/system.windows.forms.form.activated) 也把“根据窗体未激活期间的数据变化更新内容”列为该事件的正常用途。

### 2. 官方早已避免最小化期间的周期性 `git status`

2020 年 PR [`#7976`](https://github.com/gitextensions/gitextensions/pull/7976) 的合并提交标题就是“GitStatusMonitor: Avoid background updates if GUI is not visible”。2022 年 PR [`#10088`](https://github.com/gitextensions/gitextensions/pull/10088) 又把“是否最小化”的检测换成 WinForms `WindowState`；PR 说明明确写道，`GitStatusMonitor` 在最小化时跳过更新，因为 `git status` 会消耗可观的 CPU/IO。

当前 [`GitStatusMonitor.Update`](https://github.com/gitextensions/gitextensions/blob/501f831ed25127e4a301b7649d5d4e6524f53bba/src/app/GitUI/CommandsDialogs/BrowseDialog/GitStatusMonitor.cs#L422-L451) 在 `_isMinimized()` 为真时直接进入 `Inactive` 并返回；恢复后再调度下一次更新。因此，恢复时观察到 Git 子进程，并不等于应用在整个最小化期间持续 reload。

### 3. 官方确认“激活期间启动异步 Git”会干扰窗口恢复，并已做过默认规避

这一点有直接、强证据：

- Issue [`#9752`](https://github.com/gitextensions/gitextensions/issues/9752) 报告从 .NET 5 迁移后，某些环境下 Git Extensions 无法由任务栏或 Alt-Tab 带回前台。
- PR [`#10119`](https://github.com/gitextensions/gitextensions/pull/10119) 增加可选的 `WM_ACTIVATEAPP` 恢复 workaround。作者明确称它“probably not the correct solution”和“hack”，并说没有找到 core issue；另一个维护者也认为像 hack，但因问题严重仍同意接收。
- Issue [`#10398`](https://github.com/gitextensions/gitextensions/issues/10398)、[`#10557`](https://github.com/gitextensions/gitextensions/issues/10557)、[`#10582`](https://github.com/gitextensions/gitextensions/issues/10582) 和 [`#10841`](https://github.com/gitextensions/gitextensions/issues/10841) 继续报告最小化后不能恢复或多实例激活异常，说明问题真实存在且具有环境/时序依赖。
- PR [`#10802`](https://github.com/gitextensions/gitextensions/pull/10802) 更换进程退出等待实现后，参与者报告问题显著减少但没有消失，并推测在 Windows 等待应用处理 `WM_ACTIVATE` 时启动外部进程可能不安全。维护者一度建议移除 `WM_ACTIVATE` workaround，等待更好的方案。
- PR [`#10847`](https://github.com/gitextensions/gitextensions/pull/10847) 随后增加 `GitAsyncWhenMinimized`，明确目的是在“窗口最小化但收到 activation”时不运行 `git ls-files`。作者称问题在 4.0 中频繁出现、`#10802` 只“mostly fixes”它，并提出 UI refresh check 也许可以延后到 grid 刷新之后。
- PR [`#10880`](https://github.com/gitextensions/gitextensions/pull/10880) 把“不在最小化时运行 `git ls-files`”改成默认行为。作者同时记录代价：冲突状态不会总是立刻检测到，而且永久保留 workaround 可能掩盖 core problem。

当前默认值仍为 `GitAsyncWhenMinimized = false`，见 [`AppSettings`](https://github.com/gitextensions/gitextensions/blob/501f831ed25127e4a301b7649d5d4e6524f53bba/src/app/GitCommands/Settings/AppSettings.cs#L2134-L2143)。这证明官方做过性能/可靠性优化；只是它保护的是“仍处于 Minimized 的激活阶段”。窗口一旦已经变为 Normal/Maximized，`FormBrowse.OnActivate()` 仍允许做冲突检查，以换取通知条的新鲜度。

### 4. 当前 `InvokeAndForget(OnActivate)` 不等于把检查放到后台线程

[`TaskManager.InvokeAndForget`](https://github.com/gitextensions/gitextensions/blob/501f831ed25127e4a301b7649d5d4e6524f53bba/src/app/GitExtUtils/GitUI/TaskManager.cs#L106-L130) 的官方注释和实现都说明 action 是在 UI thread 上运行；它提供的是异步调度和异常转发，而不是 `Task.Run` 式后台执行。`OnActivate()` 又是同步 `void`，所以 `git ls-files` 的进程启动与等待仍可能占用 UI 消息处理时段。

官方 PR [`#10001` — async conflicted state](https://github.com/gitextensions/gitextensions/pull/10001) 正是针对这一点。PR 说明直接承认 Git 调用和 IO 在主线程执行是不应该的，并尝试把 `FormBrowse.OnActivate` 中的检查移出主线程。但它在 2022 年开启后长期未合并，最终于 2024 年关闭：UI integration tests / AppVeyor 会挂住，JoinableTask 仍可能阻塞 UI，维护者也无法完全解释 WinForms `Create` / `Show` / `Activate` 连续事件下的异步时序。

Issue [`#10449` — GitExtensions performance issue "git ls-files -z --unmerged"](https://github.com/gitextensions/gitextensions/issues/10449) 则直接报告大仓库每次点击/激活可卡 4 秒以上，要求异步执行；截至本次调查它仍为开放状态。维护者在讨论中解释，该命令用于外部变化后及时显示 resolve bar；禁用会导致工具条不显示，而简单限流又可能造成部分激活刷新、部分不刷新的不一致行为。讨论也明确指向 `#10001`：正确方向是离开 UI 线程，但当时测试失败。

### 5. 最大化/恢复导致的布局与重绘是 WinForms/Win32 的正常机制

Microsoft 文档说明：

- [`WM_SIZE`](https://learn.microsoft.com/en-us/windows/win32/winmsg/wm-size) 会在窗口尺寸改变后发送，并区分 `SIZE_MINIMIZED`、`SIZE_MAXIMIZED` 与 `SIZE_RESTORED`；
- WinForms [`Control.Resize`](https://learn.microsoft.com/en-us/dotnet/api/system.windows.forms.control.resize) 说明 Resize 会引发 Layout；
- [`Control.Layout`](https://learn.microsoft.com/en-us/dotnet/api/system.windows.forms.control.layout) 在控件 bounds 改变时发生，职责就是重排子控件；
- [`WM_PAINT`](https://learn.microsoft.com/en-us/windows/win32/gdi/wm-paint) 在系统或应用请求重画窗口区域时发送；
- WinForms 官方建议用 [double buffering](https://learn.microsoft.com/en-us/dotnet/desktop/winforms/advanced/how-to-reduce-graphics-flicker-with-double-buffering) 降低多次绘制引起的闪烁，但它降低的是可见闪烁，不会取消恢复后必须进行的布局/绘制，也不会消除同步 Git 工作。

Git Extensions 主窗体包含多个 `SplitContainer`、revision grid、left panel、tab 与 diff 控件。恢复到最大化尺寸自然会让这些容器重新 layout/paint。官方 changelog 也持续包含局部优化，例如 [`#11647`](https://github.com/gitextensions/gitextensions/pull/11647) 给 native tree 开启 double buffering、[`#11445`](https://github.com/gitextensions/gitextensions/pull/11445) 优化 left panel reload、[`#11621`](https://github.com/gitextensions/gitextensions/pull/11621) 优化 revision grid。这说明官方采用的是逐个热点修复，而不是将整个复杂窗体一次性合成或缓存为位图。

### 6. 官方用户文档没有把恢复延迟记录成一个可配置的已知性能问题

Git Extensions 4.1 的官方 [Settings 文档](https://git-extensions-documentation.readthedocs.io/en/release-4.1/settings.html#performance) 会提示关闭 changed-files/artificial-commit 计数以缓解 slowdown，也记录了 commit 数限制、submodule status 等性能选项；但没有列出“最小化恢复慢”、`GitAsyncWhenMinimized` 或 `WorkaroundActivateFromMinimize`。后二者在源码里是隐藏/手工设置，其中恢复 workaround 的 PR 也明确要求手改 `GitExtensions.settings`。

所以这个问题的官方知识主要存在于 PR/Issue/源码，而非面向用户的手册。

## 合理推断

以下不是维护者直接给出的完整设计说明，而是由上述一手证据支持的推断。

### 推断 A：官方保留激活检查，是在“立即显示”与“仓库状态准确”之间选择后者

证据权重：**高**。

`OnActivate()` 的历史提交、当前注释以及 PR `#10847` 所说“banner 可能要等恢复后才出现”的代价，都说明这项检查承担用户可见正确性：外部 merge/rebase 或冲突发生后，用户回到 GE 时应看到正确操作条。彻底删除检查很容易提高恢复速度，却会产生陈旧 UI。

### 推断 B：没有一个简单且低风险的“一行异步化”修复

证据权重：**很高**。

这不只是架构推测，未合并的 `#10001` 已提供直接实验结果：异步化导致 UI 集成测试挂死，WinForms 激活事件与 JoinableTask 的时序难以解释。官方也经历过进程等待、`WM_ACTIVATEAPP`、最小化状态判断和 Git 命令启动时序的多轮修改，并观察到环境和多实例差异。若直接把 `RefreshGitAction()` 丢到后台，还必须处理：

- `GitModule` 查询和进程生命周期；
- 切仓库、关闭窗口、再次激活时的取消与结果过期；
- 回到 UI thread 更新 notification bar；
- 多次 Activated 事件的合并/去重；
- 不让旧仓库结果写入新仓库 UI。

这些问题并非 WinForms 完全做不到，而是需要完整的异步会话与取消设计，不能只把调用包在 `Task.Run`。

### 推断 C：明显的“reload 感”很可能由两个独立成本叠加，而非一个完整 reload

证据权重：**中高**。

一部分成本是 WinForms 的 restore/maximize layout + paint；另一部分是激活后同步仓库状态检查以及恢复后的 `GitStatusMonitor` 调度。两者恰好都发生在窗口重新可见的时间附近，视觉上会像整个页面被重新加载。当前调用链没有在 `OnActivated()` 中调用 `RefreshRevisions()` 或 `ForceRefreshRevisions()`，所以“完整提交图 reload”不符合源码证据。

### 推断 D：官方优先修了“无法恢复/资源浪费”，而非对恢复动画做端到端性能工程

证据权重：**中**。

PR `#10088`、`#10802`、`#10847`、`#10880` 的目标分别是资源消耗、进程等待/死锁式恢复失败、最小化时异步 Git 干扰。官方 changelog 也显示大量控件级性能和闪烁修复。没有看到对“从点击任务栏到首帧完整呈现”的正式性能预算、trace 或 benchmark，因此更像按可复现 bug 和具体热点渐进优化。

## 未找到的证据

- 未找到官方文档或维护者声明称“最小化恢复时重新加载完整提交历史”是设计行为。
- 未找到 `v7.2.0` 官方基线中 `OnActivated()` 直接调用 `RefreshRevisions()` / `ForceRefreshRevisions()` 的证据。
- 未找到一个仍开放、标题直接对应“窗口能恢复但纯 WinForms 首帧/最大化重绘太慢”的官方 Issue；但 `#10449` 仍开放，并直接覆盖激活时 `git ls-files -z --unmerged` 造成的可见卡顿。
- 未找到 Microsoft 文档声称 WinForms 窗口恢复必然需要明显延迟。Microsoft 文档只确认 resize/layout/paint 是正常机制，并提供 double buffering 等缓解手段；具体延迟仍取决于应用自己的控件树和 UI-thread 工作。
- 未找到官方对 `#10847` 中“把 UI refresh check 延后”的设想做了完整实现或明确否决的后续说明。
- 未找到官方用户手册对隐藏设置 `GitAsyncWhenMinimized`、`WorkaroundActivateFromMinimize` 的解释。

## “为何尚未彻底优化”的证据权重

| 解释 | 权重 | 依据 |
| --- | --- | --- |
| 激活检查有状态正确性职责，不能无条件删除 | 高 | 2014 提交、当前 `after reactivation` 注释、`#10847`/`#10880` 对通知延迟的明确取舍 |
| 激活/外部进程/WinForms 时序问题难稳定复现，官方尚未找到 core issue | 很高 | `#9752`、`#10119`、`#10802` 中维护者直接表述；`#10001` 异步方案导致 UI 测试挂死并最终关闭 |
| 现存 workaround 已把最小化阶段最危险的 Git 命令默认关掉，因此剩余问题优先级下降 | 高 | `#10847`、`#10880` 及当前默认值 |
| 复杂 WinForms 控件树恢复时必然有 layout/paint，局部 double buffering 只能减闪烁 | 中高 | Microsoft 文档、官方局部性能/闪烁 PR |
| 缺少端到端恢复性能基准与专门 Issue，优化以具体 bug/热点为主 | 中 | changelog 与 Issue/PR 范围；属于材料归纳，不是维护者声明 |
| 官方认为目前体验已经足够好、因此刻意不优化 | 低/无证据 | 未找到此类表述；不应如此断言 |

## 对 Duke 版后续验证的启示

在决定是否魔改前，建议先把“恢复耗时”拆成两段测量：

1. `WM_SIZE` / Layout / Paint 到首帧的纯 UI 成本；
2. `OnActivated` → `OnActivate` → `git ls-files --unmerged` 及 `GitStatusMonitor` 恢复调度的 Git 成本。

只有第 2 段明显占主导时，延后、去重或异步化 `RefreshGitAction` 才会直接改善体验；若第 1 段占主导，应该继续定位具体控件的 layout/paint 热点。两类优化的风险和验证方案不同，不能只凭“看起来像 reload”判断。

## 补充：WinForms 子控件树的恢复重绘机制

后续手测观察到：窗口边框、菜单栏会立即恢复，而仓库工作区先呈白色，再逐步显示 revision grid、面板等 UI 元素。顶层窗体的 `WM_PAINT` 通常不到 1 ms；因此只观察顶层窗口消息不足以覆盖实际体验，还必须观察子控件的窗口定位、布局与绘制消息。

Microsoft 的 Win32 文档确认，每个窗口化子控件都有独立的 update region。父窗口需要重绘时，系统通常先向父窗口发送 `WM_PAINT`，再依次向受影响的子窗口发送 `WM_PAINT`。这使复杂 WinForms 控件树可能在屏幕上呈现为逐步填充，而不是一次性原子显示：

- [Child Window Update Region](https://learn.microsoft.com/en-us/windows/win32/gdi/child-window-update-region)
- [Painting the Window](https://learn.microsoft.com/en-us/windows/win32/learnwin32/painting-the-window)
- [WM_PAINT message](https://learn.microsoft.com/en-us/windows/win32/gdi/wm-paint)

WinForms 的 `DoubleBuffered` / `OptimizedDoubleBuffer` 只针对支持相应绘制风格的单个控件，可减少该控件内部复杂绘制的闪烁；它不自动把整个窗体及所有原生子 HWND 合成为一个原子帧。部分系统控件还会重置这些绘制风格：

- [Double Buffered Graphics](https://learn.microsoft.com/en-us/dotnet/desktop/winforms/advanced/double-buffered-graphics)
- [ControlStyles](https://learn.microsoft.com/en-us/dotnet/api/system.windows.forms.controlstyles?view=windowsdesktop-10.0)

`WS_EX_COMPOSITED` 会用双缓冲按自底向上的顺序绘制窗口的所有后代，理论上最接近“整树绘完再显示”。Duke 版实验确认它能改变恢复呈现，但同时产生异常的持续刷新，因此不适合直接作为产品修复。这与该样式会改变整个后代窗口绘制顺序和行为的官方定义一致：

- [Extended Window Styles: WS_EX_COMPOSITED](https://learn.microsoft.com/en-us/windows/win32/winmsg/extended-window-styles)

因此，更准确的定性是：**逐个子控件重绘属于 WinForms/Win32 的架构机制，但约 0.5 秒的具体延迟不是 Microsoft 文档声明的固定框架成本。** 实际耗时仍取决于应用的控件数量、嵌套层级、原生控件类型、无效区域和各控件的绘制实现。若要继续优化，应捕获子 HWND 的 ETW/WPA 绘制时间或逐个隐藏主要区域做差分实验，再定位具体热点；不应继续对顶层窗口启用全局合成 hack。

### 本地子 HWND 探针结果

为区分布局与绘制，Duke 版曾临时对 `FormBrowse` 的 112 个托管及原生子 HWND 记录 `WM_WINDOWPOSCHANGED`、`WM_SIZE`、`WM_PAINT` 和 `WM_ERASEBKGND`，并用 16 ms UI heartbeat 观察消息循环停顿。固定同一仓库和布局，连续进行三次最小化后恢复；每次均得到 222 组消息统计，诊断错误为 0。探针在结论形成后已从产品代码删除。

三次结果具有一致性：

| 路径或消息 | 第 1 次 | 第 2 次 | 第 3 次 |
| --- | ---: | ---: | ---: |
| `FormBrowse` `WM_WINDOWPOSCHANGED` | 487.0 ms | 417.7 ms | 417.1 ms |
| `toolPanel` `WM_WINDOWPOSCHANGED` | 481.5 ms | 411.9 ms | 411.2 ms |
| `MainSplitContainer` 两次 `WM_WINDOWPOSCHANGED` 合计 | 332.4 ms | 286.7 ms | 287.3 ms |
| `RightSplitContainer` 两次合计 | 129.0 ms | 138.2 ms | 136.7 ms |
| `LeftSplitContainer` 两次合计 | 60.3 ms | 50.8 ms | 42.5 ms |
| revision grid 最慢一次 `WM_PAINT` | 28.4 ms | 24.9 ms | 25.0 ms |
| left tree 一次 `WM_PAINT` | 6.8 ms | 6.6 ms | 7.5 ms |
| 首个 UI heartbeat gap | 622.7 ms | 523.5 ms | 525.3 ms |

`WM_WINDOWPOSCHANGED` 的父控件耗时包含同步处理子控件消息的时间，因此表中各行不能相加；对 112 个 HWND 的全量探针也会引入额外开销，绝对值不应视为发布版基准。但三次相同的相对排序足以否定“某个重型子控件绘制占据约 0.5 秒”的假设：最重的 revision grid 单次绘制只有约 25—28 ms，left tree 约 7 ms，而主要停顿稳定落在 `FormBrowse` → `ToolStripContainer:toolPanel` → `ToolStripContentPanel` → `SplitContainer:MainSplitContainer` 的同步窗口定位/布局级联中。`MainSplitContainer` 恢复时又固定发生两轮定位，并继续传递到左右两组嵌套 `SplitContainer`。

因此，白色工作区逐步填充是布局链完成后各子 HWND 陆续擦除背景和绘制的可见结果，但不是总延迟的主要计算热点。当前最具体的定位是：**WinForms `ToolStripContainer` 与多层 `SplitContainer` 在从最小化尺寸恢复到最大化尺寸时进行同步、重复的 bounds/layout 传播。** 本地源码没有为这些容器绑定恢复专用的 `Layout`、`Resize` 或 `SizeChanged` 业务事件；控件均以 `DockStyle.Fill` 嵌套，说明主导链来自 WinForms 容器布局本身，而不是 Git Extensions 在恢复事件里显式重建工作区。

若继续做修复实验，优先方向应是把恢复期间的多轮根布局合并成一轮，并验证 splitter distance、DPI、停靠布局和首次打开仓库均不回归；单纯给 revision grid 或 tree 加双缓冲，最多改善局部闪烁，不会消除这条约 0.4—0.5 秒的同步布局链。
