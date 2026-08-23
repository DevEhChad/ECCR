namespace ECCR.Models;

public class OutputOption
{
    public string Name { get; set; } = string.Empty;
    public string Glyph { get; set; } = string.Empty;
    public string Foreground { get; set; } = "#FFFFFF";

    public OutputOption() { }

    public OutputOption(string name, string glyph, string foreground = "#FFFFFF")
    {
        Name = name;
        Glyph = glyph;
        Foreground = foreground;
    }

    public override string ToString() => Name;
}