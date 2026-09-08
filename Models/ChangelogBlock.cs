namespace ECCR.Models;

/// <summary>The kind of line <see cref="ECCR.Services.ChangelogService.Parse"/> recognized in one row of CHANGELOG.md.</summary>
public enum ChangelogBlockKind
{
    VersionHeader,
    SectionHeader,
    Bullet,
    Plain,
    Divider
}

/// <summary>
/// One rendered row of the in-app changelog viewer. The bool convenience properties exist so
/// <c>MainWindow.axaml</c> can select which sibling element to show per row via plain
/// <c>IsVisible</c> bindings (the same technique <see cref="MappingEntry.IsAxis"/> uses)
/// instead of needing a DataTemplate selector for the <see cref="Kind"/> enum.
/// </summary>
public class ChangelogBlock
{
    public ChangelogBlockKind Kind { get; set; }

    /// <summary>Full line text for <see cref="ChangelogBlockKind.VersionHeader"/>/<see cref="ChangelogBlockKind.SectionHeader"/>/<see cref="ChangelogBlockKind.Plain"/>.</summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>The "**bolded lead-in**" portion of a <see cref="ChangelogBlockKind.Bullet"/> line, without the asterisks.</summary>
    public string BoldLead { get; set; } = string.Empty;

    /// <summary>The remainder of a <see cref="ChangelogBlockKind.Bullet"/> line after its bold lead-in.</summary>
    public string Rest { get; set; } = string.Empty;

    public bool IsVersionHeader => Kind == ChangelogBlockKind.VersionHeader;
    public bool IsSectionHeader => Kind == ChangelogBlockKind.SectionHeader;
    public bool IsBullet => Kind == ChangelogBlockKind.Bullet;
    public bool IsPlain => Kind == ChangelogBlockKind.Plain;
    public bool IsDivider => Kind == ChangelogBlockKind.Divider;
}
