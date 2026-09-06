using System.Collections.Generic;
using System.Text;

namespace ECCR.Services.GameDiscovery;

/// <summary>
/// Minimal recursive-descent parser for Valve's VDF/KeyValues text format, just enough to read
/// <c>libraryfolders.vdf</c> and <c>appmanifest_*.acf</c> - both are plain <c>"key" "value"</c>
/// pairs with <c>{ }</c> nesting, no arrays/comments/conditionals. Hand-rolled rather than a
/// NuGet dependency since the grammar needed here is this small and stable.
/// </summary>
public static class VdfParser
{
    /// <summary>Parses a VDF document's outermost object into a tree of <c>Dictionary&lt;string, object&gt;</c> nodes, where each value is either a <c>string</c> or a nested <c>Dictionary&lt;string, object&gt;</c>. Returns an empty dictionary on any malformed input rather than throwing.</summary>
    public static Dictionary<string, object> Parse(string text)
    {
        try
        {
            int pos = 0;
            SkipWhitespaceAndComments(text, ref pos);
            // The file's root is itself one "key" { ... } block (e.g. "libraryfolders").
            if (pos < text.Length && text[pos] == '"')
            {
                ReadQuotedString(text, ref pos); // discard the root key name
                SkipWhitespaceAndComments(text, ref pos);
                if (pos < text.Length && text[pos] == '{')
                {
                    return ParseObject(text, ref pos);
                }
            }
            return new Dictionary<string, object>();
        }
        catch
        {
            return new Dictionary<string, object>();
        }
    }

    private static Dictionary<string, object> ParseObject(string text, ref int pos)
    {
        var result = new Dictionary<string, object>();
        pos++; // consume '{'

        while (true)
        {
            SkipWhitespaceAndComments(text, ref pos);
            if (pos >= text.Length) break;
            if (text[pos] == '}') { pos++; break; }
            if (text[pos] != '"') { pos++; continue; }

            string key = ReadQuotedString(text, ref pos);
            SkipWhitespaceAndComments(text, ref pos);
            if (pos >= text.Length) break;

            if (text[pos] == '{')
            {
                result[key] = ParseObject(text, ref pos);
            }
            else if (text[pos] == '"')
            {
                result[key] = ReadQuotedString(text, ref pos);
            }
        }

        return result;
    }

    private static string ReadQuotedString(string text, ref int pos)
    {
        pos++; // consume opening quote
        var sb = new StringBuilder();
        while (pos < text.Length && text[pos] != '"')
        {
            if (text[pos] == '\\' && pos + 1 < text.Length)
            {
                pos++;
                sb.Append(text[pos]);
            }
            else
            {
                sb.Append(text[pos]);
            }
            pos++;
        }
        pos++; // consume closing quote
        return sb.ToString();
    }

    private static void SkipWhitespaceAndComments(string text, ref int pos)
    {
        while (pos < text.Length)
        {
            if (char.IsWhiteSpace(text[pos])) { pos++; continue; }
            if (text[pos] == '/' && pos + 1 < text.Length && text[pos + 1] == '/')
            {
                while (pos < text.Length && text[pos] != '\n') pos++;
                continue;
            }
            break;
        }
    }

    /// <summary>Finds every direct string-valued child whose value looks like a filesystem path under a given key prefix - used to pull all "path" entries out of libraryfolders.vdf's numbered sub-objects ("0", "1", ...) without knowing how many there are.</summary>
    public static IEnumerable<string> FindAllValuesForKey(Dictionary<string, object> node, string key)
    {
        foreach (var kv in node)
        {
            if (string.Equals(kv.Key, key, System.StringComparison.OrdinalIgnoreCase) && kv.Value is string s)
            {
                yield return s;
            }
            else if (kv.Value is Dictionary<string, object> child)
            {
                foreach (var nested in FindAllValuesForKey(child, key)) yield return nested;
            }
        }
    }
}
