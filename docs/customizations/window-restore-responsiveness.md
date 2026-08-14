# 窗口恢复响应优化

## 目的

Git Extensions 主窗口从最小化恢复时，WinForms 会重新布局并绘制复杂的子窗口树；主窗体激活还会同步检查 bisect、merge、rebase、patch 和冲突状态。其中冲突检查会执行 `git ls-files -z --unmerged`，可能进一步阻塞 UI 消息循环。

Duke 版只把“最小化后真正恢复为可见窗口”这一条路径上的 Git 操作状态检查移到后台，使窗口更早显示并响应输入。非最小化状态下的普通重新激活和切换仓库仍保留官方基线的同步行为，避免扩大异步时序的影响范围。

官方行为、历史 Issue/PR 与 WinForms 重绘机制的证据见 [Git Extensions 窗口恢复性能：官方行为与证据](../research/gitextensions-window-restore-performance.md)。
与 SourceTree 的实现对比见 [SourceTree 窗口恢复性能研究](../research/sourcetree-window-restore-performance.md)。

## 当前行为

- 收到主窗口最小化的 `WM_SIZE` 后，记录下一次可见激活需要异步刷新。
- 如果窗口在仍为 `Minimized` 时收到激活事件，继续采用官方基线行为，并保留待恢复标记；等窗口实际恢复为 `Normal` 或 `Maximized` 后才走后台刷新。
- 后台任务固定使用启动时的 `IGitModule` 快照，计算 bisect 和 Git action 通知条状态，不访问 WinForms 控件。
- 回到 UI 线程后，只有刷新版本仍为最新、当前仓库仍是同一个模块对象且窗口未关闭时才应用结果。
- 再次最小化、同步激活刷新、切换仓库或关闭窗口都会使尚未应用的后台结果过期。
- 已启动的同步 Git 子进程不会被强制取消；过期任务可以自然结束，但结果不会写回当前界面。

## 用户可见取舍

- 窗口工作区仍可能短暂呈白色并逐步绘制；这是 WinForms 原生子窗口的布局/重绘路径，本魔改不缓存或重建仓库视图。
- 窗口会更早响应输入，白屏持续时间相对官方基线缩短。
- merge、rebase、patch、bisect 或 conflict 通知条可能在工作区显示后才更新。
- 连续快速恢复或切换仓库时，旧查询可能继续占用短暂的 Git/IO 资源，但不会覆盖新仓库状态。

## 已否决的恢复绘制实验

以下方案不作为当前魔改保留：

- 为整个后代窗口树启用 `WS_EX_COMPOSITED`：会造成异常的持续刷新。
- 只合并 `MainSplitContainer` 的恢复布局：仅有轻微改善。
- 跨恢复消息冻结 `toolPanel.ContentPanel` 与 `MainSplitContainer`，再统一布局：重建略快，但仍明显白屏。

这些结果说明同步 Git 检查和 WinForms 布局是两个独立成本。对布局链继续做局部 `SuspendLayout` 调整，收益不足以覆盖刷新、DPI、splitter 和首次打开仓库的回归风险。

## 验证入口

编译检查：

```powershell
dotnet build src/app/GitUI/GitUI.csproj -c Release --no-restore
```

手工验证应至少覆盖：

1. 首次进入仓库无错误、无持续刷新。
2. 同一仓库连续执行至少三次最小化后恢复。
3. 工作区白屏时间缩短，窗口能更早响应点击。
4. Git action 通知条允许延后出现，但最终状态正确。
5. 后台检查尚未结束时切换仓库，旧仓库状态不得写入新仓库界面。
6. 查询期间再次最小化或关闭窗口，不应产生错误或继续写 UI。
