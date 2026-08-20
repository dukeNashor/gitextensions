using System.Runtime.InteropServices;
using System.Text.Json;
using GitCommands.UserRepositoryHistory;
using GitExtUtils.GitUI;
using GitUI.CommandsDialogs.BrowseDialog.DashboardControl;

namespace GitUITests.CommandsDialogs.BrowseDialog;

[Apartment(ApartmentState.STA)]
public sealed class MultiRepositoryStatusViewTests
{
    private const uint ListViewDeleteItemMessage = 0x1008;

    [Test]
    public void SetViewMode_uses_details_columns_and_preserves_selection()
    {
        using MultiRepositoryStatusView view = new(new());
        view.CreateControl();
        view.Controls.OfType<ListView>().Single().CreateControl();
        Repository first = new(@"C:\work\alpha") { Category = "Work" };
        Repository second = new(@"C:\work\beta") { Category = "Work" };
        view.SetContent([first, second], new Dictionary<string, MultiRepositoryStatus>());
        view.SelectRepository(second.Path);

        view.SetViewMode(MultiRepositoryStatusViewMode.Details);

        ListView list = view.Controls.OfType<ListView>().Single();
        view.ViewMode.Should().Be(MultiRepositoryStatusViewMode.Details);
        list.View.Should().Be(View.Details);
        list.HeaderStyle.Should().Be(ColumnHeaderStyle.Clickable);
        list.OwnerDraw.Should().BeTrue();
        list.Columns.Cast<ColumnHeader>().Select(header => header.Text).Should().Equal(
            "Name", "Branch", "Working tree", "Synchronization", "Last fetch", "Checked", "Path");
        view.SelectedRepository.Should().BeSameAs(second);

        view.SetViewMode(MultiRepositoryStatusViewMode.Tile);

        list.View.Should().Be(View.Tile);
        view.SelectedRepository.Should().BeSameAs(second);
    }

    [Test]
    public void ColumnClick_cycles_ascending_descending_and_manual_order_within_group()
    {
        using MultiRepositoryStatusView view = new(new());
        Repository beta = new(@"C:\work\beta") { Category = "Work" };
        Repository alpha = new(@"C:\work\alpha") { Category = "Work" };
        view.SetContent([beta, alpha], new Dictionary<string, MultiRepositoryStatus>());
        view.SetViewMode(MultiRepositoryStatusViewMode.Details);
        ListView list = view.Controls.OfType<ListView>().Single();

        InvokeColumnClick(view, list, 0);
        RepositoryPaths(list).Should().Equal(alpha.Path, beta.Path);
        list.Columns[0].Text.Should().EndWith("▲");

        InvokeColumnClick(view, list, 0);
        RepositoryPaths(list).Should().Equal(beta.Path, alpha.Path);
        list.Columns[0].Text.Should().EndWith("▼");

        InvokeColumnClick(view, list, 0);
        RepositoryPaths(list).Should().Equal(beta.Path, alpha.Path);
        list.Columns[0].Text.Should().Be("Name");
    }

    [Test]
    public void ColumnWidthChanged_with_stale_column_index_should_not_throw()
    {
        using MultiRepositoryStatusView view = new(new());
        view.SetViewMode(MultiRepositoryStatusViewMode.Details);
        ListView list = view.Controls.OfType<ListView>().Single();

        Action act = () => typeof(MultiRepositoryStatusView)
            .GetMethod("List_ColumnWidthChanged", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .Invoke(view, [list, new ColumnWidthChangedEventArgs(14)]);

        act.Should().NotThrow();
    }

    [Test]
    public void ColumnClick_with_stale_column_index_should_not_throw()
    {
        using MultiRepositoryStatusView view = new(new());
        view.SetViewMode(MultiRepositoryStatusViewMode.Details);
        ListView list = view.Controls.OfType<ListView>().Single();

        Action act = () => typeof(MultiRepositoryStatusView)
            .GetMethod("List_ColumnClick", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .Invoke(view, [list, new ColumnClickEventArgs(14)]);

        act.Should().NotThrow();
    }

    [Test]
    public void GroupHeaderHitTest_with_collapsed_group_should_not_throw()
    {
        MultiRepositoryStatusLayout layout = new();
        layout.CollapsedGroups.Add("Work");
        using MultiRepositoryStatusView view = new(layout);
        using Form form = new() { ShowInTaskbar = false };
        form.Controls.Add(view);
        view.Dock = DockStyle.Fill;
        form.Show();
        Application.DoEvents();
        view.CreateControl();
        ListView list = view.Controls.OfType<ListView>().Single();
        list.CreateControl();
        view.SetViewMode(MultiRepositoryStatusViewMode.Details);
        view.SetContent(
            Enumerable.Range(0, 15)
                .Select(index => new Repository($@"C:\work\repository-{index}") { Category = "Work" })
            .ToList(),
            new Dictionary<string, MultiRepositoryStatus>());
        Application.DoEvents();

        ListViewGroup collapsedGroup = list.Groups[0];
        collapsedGroup.CollapsedState.Should().Be(ListViewGroupCollapsedState.Collapsed);
        // Simulate the native list being refreshed while the managed group still contains the old item.
        SendMessage(list.Handle, ListViewDeleteItemMessage, new(14), IntPtr.Zero).Should().NotBe(IntPtr.Zero);

        Action act = () => typeof(MultiRepositoryStatusView)
            .GetMethod("GetGroupHeaderBounds", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .Invoke(view, [collapsedGroup]);

        act.Should().NotThrow();
        form.Close();
    }

    [Test]
    public void Details_populates_status_columns()
    {
        using MultiRepositoryStatusView view = new(new());
        Repository repository = new(@"C:\work\alpha") { Category = "Work" };
        MultiRepositoryStatus status = new()
        {
            RepositoryPath = repository.Path,
            Branch = "main",
            Upstream = "origin/main",
            ModifiedCount = 2,
            Ahead = 1,
            LastCheckedUtc = new DateTimeOffset(2026, 8, 12, 1, 2, 0, TimeSpan.Zero),
            LastFetchUtc = new DateTimeOffset(2026, 8, 12, 1, 0, 0, TimeSpan.Zero)
        };
        view.SetContent([repository], new Dictionary<string, MultiRepositoryStatus> { [repository.Path] = status });

        view.SetViewMode(MultiRepositoryStatusViewMode.Details);

        ListViewItem item = view.Controls.OfType<ListView>().Single().Items[0];
        item.SubItems.Cast<ListViewItem.ListViewSubItem>().Select(subItem => subItem.Text).Should().ContainInOrder(
            "alpha", "main", "Modified 2", "Ahead 1");
        item.SubItems[^1].Text.Should().Be(repository.Path);
    }

    [Test]
    public void Details_drop_position_uses_only_vertical_half()
    {
        Rectangle row = new(10, 20, 400, 24);

        MultiRepositoryStatusView.IsDropAfter(new Point(row.Right - 1, row.Top + 5), row, MultiRepositoryStatusViewMode.Details).Should().BeFalse();
        MultiRepositoryStatusView.IsDropAfter(new Point(row.Left + 1, row.Bottom - 5), row, MultiRepositoryStatusViewMode.Details).Should().BeTrue();
        MultiRepositoryStatusView.IsDropAfter(new Point(row.Right - 1, row.Top + 11), row, MultiRepositoryStatusViewMode.Tile).Should().BeTrue();
    }

    [Test]
    public void ResetColumnLayout_restores_default_order_and_keeps_sorting()
    {
        MultiRepositoryStatusLayout layout = new()
        {
            ViewMode = MultiRepositoryStatusViewMode.Details,
            ColumnOrder = [MultiRepositoryStatusColumn.Name, MultiRepositoryStatusColumn.Path, MultiRepositoryStatusColumn.Branch],
            ColumnWidths = new() { [MultiRepositoryStatusColumn.Path] = 777 },
            SortColumn = MultiRepositoryStatusColumn.Name,
            SortDirection = MultiRepositoryStatusSortDirection.Descending
        };
        using MultiRepositoryStatusView view = new(layout);

        view.ResetColumnLayout();

        layout.ColumnOrder.Should().Equal(MultiRepositoryStatusLayout.DefaultColumnOrder);
        layout.ColumnWidths.Should().BeEmpty();
        layout.SortColumn.Should().Be(MultiRepositoryStatusColumn.Name);
        layout.SortDirection.Should().Be(MultiRepositoryStatusSortDirection.Descending);
    }

    [Test]
    public void Layout_json_round_trip_preserves_view_columns_and_sorting()
    {
        MultiRepositoryStatusLayout expected = new()
        {
            ViewMode = MultiRepositoryStatusViewMode.Details,
            ColumnOrder = [MultiRepositoryStatusColumn.Name, MultiRepositoryStatusColumn.Path, MultiRepositoryStatusColumn.Branch],
            ColumnWidths = new() { [MultiRepositoryStatusColumn.Path] = 640 },
            SortColumn = MultiRepositoryStatusColumn.Checked,
            SortDirection = MultiRepositoryStatusSortDirection.Descending
        };

        MultiRepositoryStatusLayout actual = JsonSerializer.Deserialize<MultiRepositoryStatusLayout>(JsonSerializer.Serialize(expected))!;

        actual.ViewMode.Should().Be(expected.ViewMode);
        actual.ColumnOrder.Should().Equal(expected.ColumnOrder);
        actual.ColumnWidths.Should().ContainKey(MultiRepositoryStatusColumn.Path).WhoseValue.Should().Be(640);
        actual.SortColumn.Should().Be(expected.SortColumn);
        actual.SortDirection.Should().Be(expected.SortDirection);
    }

    [Test]
    public void Hover_grip_does_not_paint_over_path_text()
    {
        using MultiRepositoryStatusView view = new(new());
        Repository repository = new(@"D:\dev\ADFinalPosition") { Category = "Work" };
        view.SetContent([repository], new Dictionary<string, MultiRepositoryStatus>());

        ListView list = view.Controls.OfType<ListView>().Single();
        ListViewItem item = list.Items[0];
        item.Selected = true;
        Rectangle tileBounds = new(0, 0, 647, 118);
        using Bitmap bitmap = new(tileBounds.Width, tileBounds.Height);
        using Graphics graphics = Graphics.FromImage(bitmap);

        typeof(MultiRepositoryStatusView)
            .GetField("_hoveredItem", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .SetValue(view, null);
        DrawListViewItemEventArgs args = new(graphics, item, tileBounds, item.Index, ListViewItemStates.Selected);
        typeof(MultiRepositoryStatusView)
            .GetMethod("List_DrawItem", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .Invoke(view, [list, args]);

        Rectangle hoverGripBounds = new(tileBounds.Right - DpiUtil.Scale(24), tileBounds.Top + DpiUtil.Scale(8), DpiUtil.Scale(18), 16);
        int pathPixelsUnderHoverGrip = 0;
        for (int y = hoverGripBounds.Top; y < hoverGripBounds.Bottom; y++)
        {
            for (int x = hoverGripBounds.Left; x < hoverGripBounds.Right; x++)
            {
                if (bitmap.GetPixel(x, y).ToArgb() != DashboardTheme.Light.StartBackColor.ToArgb())
                {
                    pathPixelsUnderHoverGrip++;
                }
            }
        }

        pathPixelsUnderHoverGrip.Should().Be(0, "the hover grip must have dedicated space instead of overwriting the right-aligned path");
    }

    [Test]
    public void CreateCategoriesMenu_lists_existing_categories_and_add_category_action()
    {
        using MultiRepositoryStatusView view = new(new());
        Repository uncategorised = new(@"C:\uncategorised");
        view.SetContent(
            [uncategorised, new Repository(@"C:\work") { Category = "Work" }],
            new Dictionary<string, MultiRepositoryStatus>());

        Repository? requestedRepository = null;
        view.AddCategoryRequested += (_, e) => requestedRepository = e.Repository;

        ToolStripMenuItem menu = view.CreateCategoriesMenu(uncategorised);

        menu.Text.Should().Be("Categories");
        menu.DropDownItems.Cast<ToolStripItem>().Select(item => item.Text).Should().Equal("(none)", "Work", "", "Add new...");
        menu.DropDownItems[0].Enabled.Should().BeFalse();
        menu.DropDownItems[^1].Image.Should().NotBeNull();
        menu.DropDownItems[^1].PerformClick();
        requestedRepository.Should().BeSameAs(uncategorised);
    }

    [Test]
    public void CreateCategoriesMenu_offers_add_category_when_no_categories_exist()
    {
        using MultiRepositoryStatusView view = new(new());
        Repository uncategorised = new(@"C:\uncategorised");
        view.SetContent([uncategorised], new Dictionary<string, MultiRepositoryStatus>());

        ToolStripMenuItem menu = view.CreateCategoriesMenu(uncategorised);

        menu.DropDownItems.Cast<ToolStripItem>().Should().ContainSingle().Which.Text.Should().Be("Add new...");
    }

    private static void InvokeColumnClick(MultiRepositoryStatusView view, ListView list, int column)
        => typeof(MultiRepositoryStatusView)
            .GetMethod("List_ColumnClick", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .Invoke(view, [list, new ColumnClickEventArgs(column)]);

    private static IEnumerable<string> RepositoryPaths(ListView list)
        => list.Items.Cast<ListViewItem>().Select(item => ((Repository)item.Tag!).Path);

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr windowHandle, uint message, IntPtr messageParameter, IntPtr additionalParameter);
}
