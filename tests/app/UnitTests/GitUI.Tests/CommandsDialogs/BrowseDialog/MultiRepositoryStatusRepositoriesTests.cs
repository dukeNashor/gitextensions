using GitCommands.UserRepositoryHistory;
using GitUI.CommandsDialogs.BrowseDialog.DashboardControl;

namespace GitUITests.CommandsDialogs.BrowseDialog;

public sealed class MultiRepositoryStatusRepositoriesTests
{
    [Test]
    public void Combine_prefers_categorised_entry_and_normalises_trailing_separator()
    {
        Repository recent = new(@"C:\work\repo\");
        Repository categorised = new(@"c:\work\repo") { Category = "Work" };

        List<Repository> result = MultiRepositoryStatusRepositories.Combine([recent], [categorised], _ => true);

        result.Should().ContainSingle().Which.Should().BeSameAs(categorised);
    }

    [Test]
    public void Combine_orders_recent_uncategorised_before_stored_compatibility_entries()
    {
        Repository mostRecent = new(@"C:\recent-one");
        Repository nextRecent = new(@"C:\recent-two");
        Repository storedRecent = new(@"C:\recent-two\");
        Repository storedOnly = new(@"C:\stored-only");

        List<Repository> result = MultiRepositoryStatusRepositories.Combine(
            [mostRecent, nextRecent],
            [storedOnly, storedRecent],
            _ => true);

        result.Select(repository => repository.Path).Should().Equal(mostRecent.Path, storedRecent.Path, storedOnly.Path);
        result[1].Should().BeSameAs(storedRecent);
    }

    [Test]
    public void Combine_filters_invalid_recent_but_keeps_stored_uncategorised_repository()
    {
        Repository invalidRecent = new(@"C:\invalid-recent");
        Repository stored = new(@"C:\stored");

        List<Repository> result = MultiRepositoryStatusRepositories.Combine(
            [invalidRecent],
            [stored],
            path => !MultiRepositoryStatusRepositories.PathsEqual(path, invalidRecent.Path));

        result.Should().Equal(stored);
    }

    [Test]
    public void Combine_filters_invalid_stored_uncategorised_repository()
    {
        Repository invalidStored = new(@"C:\invalid-stored");

        List<Repository> result = MultiRepositoryStatusRepositories.Combine(
            [],
            [invalidStored],
            _ => false);

        result.Should().BeEmpty();
    }

    [Test]
    public void Combine_keeps_categorised_order_and_recent_order()
    {
        Repository groupB = new(@"C:\group-b") { Category = "B" };
        Repository groupA = new(@"C:\group-a") { Category = "A" };
        Repository recentTwo = new(@"C:\recent-two");
        Repository recentOne = new(@"C:\recent-one");

        List<Repository> result = MultiRepositoryStatusRepositories.Combine(
            [recentTwo, recentOne],
            [groupB, groupA],
            _ => true);

        result.Select(repository => repository.Path).Should().Equal(groupB.Path, groupA.Path, recentTwo.Path, recentOne.Path);
        result[0].Should().BeSameAs(groupB);
        result[1].Should().BeSameAs(groupA);
    }

    [Test]
    public void PathComparer_matches_cache_keys_with_different_trailing_separators()
    {
        Dictionary<string, string> cache = new(MultiRepositoryStatusRepositories.PathComparer)
        {
            [@"C:\work\repo\"] = "status"
        };

        cache[@"c:\work\repo"].Should().Be("status");
    }
}
