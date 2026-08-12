using GitCommands.UserRepositoryHistory;

namespace GitUI.CommandsDialogs.BrowseDialog.DashboardControl;

internal static class MultiRepositoryStatusRepositories
{
    public static IEqualityComparer<string> PathComparer { get; } = new RepositoryPathEqualityComparer();

    public static List<Repository> Combine(
        IEnumerable<Repository> recentRepositories,
        IEnumerable<Repository> categorisedRepositories,
        Func<string, bool> isValidRecentRepository)
    {
        ArgumentNullException.ThrowIfNull(recentRepositories);
        ArgumentNullException.ThrowIfNull(categorisedRepositories);
        ArgumentNullException.ThrowIfNull(isValidRecentRepository);

        List<Repository> categorised = DistinctByPath(categorisedRepositories
            .Where(repository => !string.IsNullOrWhiteSpace(repository.Category)));
        List<Repository> uncategorisedStored = DistinctByPath(categorisedRepositories
            .Where(repository => string.IsNullOrWhiteSpace(repository.Category)));
        Dictionary<string, Repository> uncategorisedStoredByPath = uncategorisedStored
            .ToDictionary(repository => NormalizePath(repository.Path), StringComparer.OrdinalIgnoreCase);

        HashSet<string> includedPaths = new(
            categorised.Select(repository => NormalizePath(repository.Path)),
            StringComparer.OrdinalIgnoreCase);
        List<Repository> result = [.. categorised];

        foreach (Repository recent in recentRepositories)
        {
            if (string.IsNullOrWhiteSpace(recent.Path))
            {
                continue;
            }

            string path = NormalizePath(recent.Path);
            if (includedPaths.Contains(path) || !isValidRecentRepository(recent.Path))
            {
                continue;
            }

            result.Add(uncategorisedStoredByPath.GetValueOrDefault(path) ?? new Repository(recent.Path));
            includedPaths.Add(path);
        }

        foreach (Repository repository in uncategorisedStored)
        {
            if (includedPaths.Add(NormalizePath(repository.Path)))
            {
                result.Add(repository);
            }
        }

        return result;
    }

    public static bool PathsEqual(string left, string right)
        => string.Equals(NormalizePath(left), NormalizePath(right), StringComparison.OrdinalIgnoreCase);

    private static List<Repository> DistinctByPath(IEnumerable<Repository> repositories)
    {
        HashSet<string> paths = new(StringComparer.OrdinalIgnoreCase);
        return [.. repositories.Where(repository => !string.IsNullOrWhiteSpace(repository.Path)
                                                     && paths.Add(NormalizePath(repository.Path)))];
    }

    private static string NormalizePath(string path)
    {
        string trimmed = path.Trim();
        string root = Path.GetPathRoot(trimmed) ?? "";
        return trimmed.Length > root.Length
            ? trimmed.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            : trimmed;
    }

    private sealed class RepositoryPathEqualityComparer : IEqualityComparer<string>
    {
        public bool Equals(string? left, string? right)
            => left is not null && right is not null && PathsEqual(left, right);

        public int GetHashCode(string path)
            => StringComparer.OrdinalIgnoreCase.GetHashCode(NormalizePath(path));
    }
}
