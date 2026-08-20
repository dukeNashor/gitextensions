# 仓库状态总览

该魔改在原 Dashboard 中增加面向几十个本地仓库的状态总览。应用启动默认进入总览，左侧“回到传统视图”可切回官方仓库列表。总览纳入已分类仓库，以及最近历史中有效但尚未分类的仓库；历史收藏列表中分类为空的兼容项也视为未分类仓库，但目录已不存在的未分类项不会显示。

## 当前行为

- 提供 Git Extensions 原生文件浏览器风格的分组“平铺”和“详细信息”两种 View；工具栏可切换并记住最后使用的 View。两种 View 都按分类分组，“未分类”固定在最后。未分类仓库按最近打开顺序排列，仅存在于历史收藏列表中的兼容项排在末尾。
- 详细信息 View 以单行表格显示名称、分支、工作区、同步、上次 Fetch、检查时间和路径。名称列固定在首位；其余列可调整宽度和顺序，列标题右键菜单可恢复默认列布局。首次使用时路径列填充剩余空间，之后尊重用户宽度；空间不足时横向滚动，完整内容可从提示查看。
- 每个仓库显示项目图标、分支、工作区状态、同步标签、检查时间，以及“相对时间（本地绝对时间）”格式的上次 Fetch 时间。
- 同步标签区分已同步、领先、落后、已分叉、未设置上游和分离 HEAD；工作区状态与同步状态是不同概念。
- 搜索覆盖仓库名、路径、分类和分支，并保留分组。搜索期间临时展开匹配分组且禁用排序和列排序。
- 支持已分类组内仓库排序和分组排序：鼠标拖动或 `Alt+Up/Down`；已分类仓库不允许跨组移动，未分类组不可移出末尾。折叠状态及顺序持久化；工具栏可重置排序。
- 详细信息 View 可点击列标题在组内按升序、降序和手动顺序三态切换；列排序不改写已保存的手动顺序。列排序期间禁用组内鼠标及键盘手动排序，但未分类仓库仍可拖放归类；状态变化会即时重排并保持当前选择可见。重置排序同时清除列排序，但不重置列布局。
- 未分类仓库可拖到已有分组的标题、折叠标题、展开区域或 Tile 上完成归类，归类后追加到目标组末尾；右键菜单及上下文菜单键提供与传统视图一致的“分类 / 添加项目…”入口，可选择已有分类或新建分类并归类当前仓库。搜索期间禁用拖放，但菜单仍可使用。总览不重命名或删除分组。
- `Enter` 打开仓库，`F5` 检查选中仓库。工具栏支持检查或 Fetch 选中/全部总览仓库。
- 打开仓库前再次确认目录存在且仍可作为 Git 工作区或裸仓库；目录在加载后被移除时会触发重新加载，不会启动 Git 进程。
- 两种 View 共享搜索、分组折叠和选择状态；切换后滚动到选中仓库。检查或 Fetch 期间仍可切换 View。
- 本地状态检查不访问网络；Fetch 总是处理目标仓库配置的全部远端。

## 后台 Fetch

默认启用。当系统空闲满 5 分钟后执行一次；持续空闲时每 30 分钟再次执行。后台 Fetch 处理总览中的已分类仓库和有效未分类仓库。默认最多并发处理 4 个仓库，单仓库超时 120 秒。这些值可在“多仓库状态”设置页调整。

仓库正处于 merge、rebase、cherry-pick、revert、bisect 或存在 `index.lock` 时跳过 Fetch。调度与 UI 生命周期分离，即使当前显示传统视图也可继续运行；所有 UI 更新回到 UI 线程。

## 状态与错误语义

- 状态由本地 `git status`、分支及上游引用计算；ahead/behind 反映本地已有远端跟踪引用，不代表服务器上的实时状态。
- 本地检查成功不会抹除最近一次 Fetch 错误；Fetch 成功才清除 Fetch 错误。
- Fetch 错误与本地检查错误分别保留，界面优先呈现 Fetch 错误。
- 相对时间每分钟仅重绘文本，不执行 Git 命令。

## 配置与本地数据

常规设置沿用 Git Extensions 的全局设置存储，键前缀为 `multirepositorystatus.*`：自动 Fetch 开关、空闲阈值、Fetch 周期、并发数和超时。

以下可再生成数据位于 `AppSettings.LocalApplicationDataPath`：

- `MultiRepositoryStatusCache.json`：当前总览仓库的最近状态和 Fetch 时间，用于启动时即时显示；仓库离开最近历史且未分类后会在下次保存时从缓存移除。
- `MultiRepositoryStatusLayout.json`：当前 View、详细信息列宽与列顺序、列排序、分组顺序、组内仓库顺序及折叠状态。

文件损坏、缺失或不可写不会阻止总览使用；缓存仅是本地派生数据，不应提交到仓库。

## 代码与验证入口

主要代码集中在：

- `src/app/GitUI/CommandsDialogs/BrowseDialog/DashboardControl/MultiRepositoryStatus*.cs`
- `src/app/GitUI/CommandsDialogs/BrowseDialog/DashboardControl/Dashboard.cs`
- `src/app/GitCommands/Settings/AppSettings.cs`
- `src/app/GitUI/CommandsDialogs/SettingsDialog/Pages/MultiRepositoryStatusSettingsPage.cs`

针对性测试：

```powershell
dotnet test tests/app/UnitTests/GitUI.Tests/GitUI.Tests.csproj -c Release --filter "FullyQualifiedName~MultiRepository"
```

完整构建：

```powershell
dotnet build GitExtensions.slnx -c Release
```
