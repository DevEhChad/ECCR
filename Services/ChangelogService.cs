using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Avalonia.Platform;
using ECCR.Models;

namespace ECCR.Services;

/// <summary>
/// Loads and parses the app's bundled CHANGELOG.md for the in-app changelog viewer (see
/// <c>MainWindowViewModel.OpenChangelog</c>/<c>CheckPostUpdateStatus</c>). The file is
/// embedded as an Avalonia resource at build time (see the <c>AvaloniaResource</c> entry in
/// ECCR.csproj) rather than fetched from GitHub, so the popup that fires the moment an
/// update finishes has no network dependency and always matches what actually shipped.
/// </summary>
public static class ChangelogService
{
    private static readonly Uri AssetUri = new("avares://ECCR/CHANGELOG.md");

    /// <summary>Reads the bundled CHANGELOG.md verbatim, or null if it's missing/unreadable (shouldn't happen in a normal build).</summary>
    public static string? LoadBundledMarkdown()
    {
        try
        {
            if (!AssetLoader.Exists(AssetUri)) return null;
            using var stream = AssetLoader.Open(AssetUri);
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Slices out just one version's section (from its "## [version]" header up to the next
    /// version header or end of file), for the post-update popup which should only show
    /// what's new in the version the user just landed on, not the entire history.
    /// </summary>
    public static string? ExtractVersionSection(string markdown, string version)
    {
        var lines = markdown.Replace("\r\n", "\n").Split('\n');
        var headerPattern = new Regex(@"^##\s*\[" + Regex.Escape(version) + @"\]", RegexOptions.IgnoreCase);
        var anyHeaderPattern = new Regex(@"^##\s*\[");

        int start = -1;
        for (int i = 0; i < lines.Length; i++)
        {
            if (headerPattern.IsMatch(lines[i]))
            {
                start = i;
                break;
            }
        }

        if (start < 0) return null;

        int end = lines.Length;
        for (int i = start + 1; i < lines.Length; i++)
        {
            if (anyHeaderPattern.IsMatch(lines[i]))
            {
                end = i;
                break;
            }
        }

        return string.Join('\n', lines[start..end]).TrimEnd('-', '\n', ' ');
    }

    /// <summary>
    /// Turns CHANGELOG.md's markdown into a flat list of typed rows for direct data-binding.
    /// The format is simple and consistent across every entry in this file, so a handful of
    /// line-prefix checks is enough - see <see cref="ChangelogBlock"/> for how each kind is
    /// meant to be rendered.
    /// </summary>
    public static List<ChangelogBlock> Parse(string markdown)
    {
        var blocks = new List<ChangelogBlock>();
        var lines = markdown.Replace("\r\n", "\n").Split('\n');
        var bulletWithBoldLead = new Regex(@"^[-*]\s*\*\*(.+?)\*\*:?\s*(.*)$");

        foreach (var rawLine in lines)
        {
            string line = rawLine.TrimEnd();
            if (string.IsNullOrWhiteSpace(line)) continue;

            if (line.StartsWith("## [", StringComparison.Ordinal))
            {
                blocks.Add(new ChangelogBlock { Kind = ChangelogBlockKind.VersionHeader, Text = CleanInline(line[3..]) });
            }
            else if (line.StartsWith("### ", StringComparison.Ordinal))
            {
                blocks.Add(new ChangelogBlock { Kind = ChangelogBlockKind.SectionHeader, Text = CleanInline(line[4..]) });
            }
            else if (line.Trim() == "---")
            {
                blocks.Add(new ChangelogBlock { Kind = ChangelogBlockKind.Divider });
            }
            else if (line.StartsWith("- ", StringComparison.Ordinal) || line.StartsWith("* ", StringComparison.Ordinal))
            {
                var match = bulletWithBoldLead.Match(line);
                if (match.Success)
                {
                    blocks.Add(new ChangelogBlock
                    {
                        Kind = ChangelogBlockKind.Bullet,
                        BoldLead = CleanInline(match.Groups[1].Value),
                        Rest = CleanInline(match.Groups[2].Value)
                    });
                }
                else
                {
                    blocks.Add(new ChangelogBlock { Kind = ChangelogBlockKind.Bullet, Rest = CleanInline(line[2..]) });
                }
            }
            else if (line.StartsWith("# ", StringComparison.Ordinal))
            {
                // The single top-level "# Changelog" title - treated as plain body text since
                // it only ever appears once and doesn't need its own visual tier.
                blocks.Add(new ChangelogBlock { Kind = ChangelogBlockKind.Plain, Text = CleanInline(line[2..]) });
            }
            else
            {
                blocks.Add(new ChangelogBlock { Kind = ChangelogBlockKind.Plain, Text = CleanInline(line) });
            }
        }

        return blocks;
    }

    /// <summary>
    /// Strips the handful of markdown constructs this file actually uses (bold, inline code,
    /// links) down to plain readable text, since the renderer only bothers giving real bold
    /// styling to a bullet's lead-in (see <see cref="Parse"/>) - everywhere else, showing the
    /// raw "**"/backtick/"[]()" markup would just look like noise.
    /// </summary>
    private static string CleanInline(string text)
    {
        string cleaned = Regex.Replace(text, @"\[([^\]]+)\]\([^\)]+\)", "$1");
        cleaned = cleaned.Replace("**", string.Empty);
        cleaned = cleaned.Replace("`", string.Empty);
        return cleaned.Trim();
    }
}
