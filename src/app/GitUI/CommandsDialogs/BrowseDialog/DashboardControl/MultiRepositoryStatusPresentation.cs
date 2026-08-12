using GitCommands;
using GitExtensions.Extensibility.Translations;
using ResourceManager;

namespace GitUI.CommandsDialogs.BrowseDialog.DashboardControl;

internal enum MultiRepositorySyncLabelKind
{
    Neutral,
    Synchronized,
    Ahead,
    Behind,
    Diverged,
    Error
}

internal sealed record MultiRepositorySyncLabel(string Text, MultiRepositorySyncLabelKind Kind);

internal static class MultiRepositoryStatusPresentation
{
    private static readonly MultiRepositoryStatusPresentationTexts Strings = new();

    public static IReadOnlyList<MultiRepositorySyncLabel> GetSynchronizationLabels(MultiRepositoryStatus? status)
    {
        if (status is null)
        {
            return [new(Strings.WaitingForCheck, MultiRepositorySyncLabelKind.Neutral)];
        }

        if (status.IsDetached)
        {
            return [new(Strings.DetachedHead, MultiRepositorySyncLabelKind.Neutral)];
        }

        if (string.IsNullOrWhiteSpace(status.Upstream))
        {
            return [new(Strings.NoUpstream, MultiRepositorySyncLabelKind.Neutral)];
        }

        int ahead = status.Ahead ?? 0;
        int behind = status.Behind ?? 0;
        if (ahead == 0 && behind == 0)
        {
            return [new(Strings.Synchronized, MultiRepositorySyncLabelKind.Synchronized)];
        }

        List<MultiRepositorySyncLabel> labels = [];
        if (ahead != 0 && behind != 0)
        {
            labels.Add(new(Strings.Diverged, MultiRepositorySyncLabelKind.Diverged));
        }

        if (ahead != 0)
        {
            labels.Add(new(string.Format(Strings.Ahead, ahead), MultiRepositorySyncLabelKind.Ahead));
        }

        if (behind != 0)
        {
            labels.Add(new(string.Format(Strings.Behind, behind), MultiRepositorySyncLabelKind.Behind));
        }

        return labels;
    }

    public static string FormatWorkingTree(MultiRepositoryStatus? status)
    {
        if (status is null)
        {
            return Strings.WaitingForCheck;
        }

        if (status.IsBare)
        {
            return Strings.BareRepository;
        }

        if (!status.HasWorkingTreeChanges)
        {
            return Strings.Clean;
        }

        List<string> parts = [];
        if (status.StagedCount != 0)
        {
            parts.Add(string.Format(Strings.Staged, status.StagedCount));
        }

        if (status.ModifiedCount != 0)
        {
            parts.Add(string.Format(Strings.Modified, status.ModifiedCount));
        }

        if (status.UntrackedCount != 0)
        {
            parts.Add(string.Format(Strings.Untracked, status.UntrackedCount));
        }

        return string.Join(" · ", parts);
    }

    public static string FormatFetchTimestamp(DateTimeOffset? timestamp, DateTimeOffset now)
        => timestamp is null || timestamp == default
            ? Strings.Never
            : string.Format(Strings.RelativeAndAbsoluteTime, FormatRelativeTime(timestamp.Value, now), timestamp.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm"));

    public static string FormatCheckedTimestamp(DateTimeOffset? timestamp)
        => timestamp is null || timestamp == default
            ? Strings.Never
            : timestamp.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm");

    internal static string FormatRelativeTime(DateTimeOffset timestamp, DateTimeOffset now)
    {
        TimeSpan elapsed = now - timestamp;
        if (elapsed < TimeSpan.FromMinutes(1))
        {
            return Strings.JustNow;
        }

        if (elapsed < TimeSpan.FromHours(1))
        {
            return ResourceManager.TranslatedStrings.GetNMinutesAgoText((int)elapsed.TotalMinutes);
        }

        if (elapsed < TimeSpan.FromDays(1))
        {
            return ResourceManager.TranslatedStrings.GetNHoursAgoText((int)elapsed.TotalHours);
        }

        if (elapsed < TimeSpan.FromDays(30))
        {
            return ResourceManager.TranslatedStrings.GetNDaysAgoText((int)elapsed.TotalDays);
        }

        if (elapsed < TimeSpan.FromDays(365))
        {
            return ResourceManager.TranslatedStrings.GetNMonthsAgoText(Math.Max(1, (int)(elapsed.TotalDays / 30)));
        }

        return ResourceManager.TranslatedStrings.GetNYearsAgoText(Math.Max(1, (int)(elapsed.TotalDays / 365)));
    }

    private sealed class MultiRepositoryStatusPresentationTexts : Translate
    {
        private readonly TranslationString _ahead = new("Ahead {0}");
        private readonly TranslationString _bareRepository = new("Bare repository");
        private readonly TranslationString _behind = new("Behind {0}");
        private readonly TranslationString _clean = new("Clean");
        private readonly TranslationString _detachedHead = new("Detached HEAD");
        private readonly TranslationString _diverged = new("Diverged");
        private readonly TranslationString _justNow = new("Just now");
        private readonly TranslationString _modified = new("Modified {0}");
        private readonly TranslationString _never = new("Never");
        private readonly TranslationString _noUpstream = new("No upstream");
        private readonly TranslationString _relativeAndAbsoluteTime = new("{0} ({1})");
        private readonly TranslationString _staged = new("Staged {0}");
        private readonly TranslationString _synchronized = new("Synchronized");
        private readonly TranslationString _untracked = new("Untracked {0}");
        private readonly TranslationString _waitingForCheck = new("Waiting for check");

        public MultiRepositoryStatusPresentationTexts()
            => Translator.Translate(this, AppSettings.CurrentTranslation);

        public string Ahead => _ahead.Text;
        public string BareRepository => _bareRepository.Text;
        public string Behind => _behind.Text;
        public string Clean => _clean.Text;
        public string DetachedHead => _detachedHead.Text;
        public string Diverged => _diverged.Text;
        public string JustNow => _justNow.Text;
        public string Modified => _modified.Text;
        public string Never => _never.Text;
        public string NoUpstream => _noUpstream.Text;
        public string RelativeAndAbsoluteTime => _relativeAndAbsoluteTime.Text;
        public string Staged => _staged.Text;
        public string Synchronized => _synchronized.Text;
        public string Untracked => _untracked.Text;
        public string WaitingForCheck => _waitingForCheck.Text;
    }
}
