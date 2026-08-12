using GitCommands.UserRepositoryHistory;
using GitExtUtils.GitUI;
using GitUI.CommandsDialogs.BrowseDialog.DashboardControl;

namespace GitUITests.CommandsDialogs.BrowseDialog;

[Apartment(ApartmentState.STA)]
public sealed class MultiRepositoryStatusViewTests
{
    [Test]
    public void Hover_grip_does_not_paint_over_path_text()
    {
        using MultiRepositoryStatusView view = new();
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
        using MultiRepositoryStatusView view = new();
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
        using MultiRepositoryStatusView view = new();
        Repository uncategorised = new(@"C:\uncategorised");
        view.SetContent([uncategorised], new Dictionary<string, MultiRepositoryStatus>());

        ToolStripMenuItem menu = view.CreateCategoriesMenu(uncategorised);

        menu.DropDownItems.Cast<ToolStripItem>().Should().ContainSingle().Which.Text.Should().Be("Add new...");
    }
}
