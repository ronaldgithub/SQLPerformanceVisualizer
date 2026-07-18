using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Layout;
using Avalonia.Media;

namespace SQLPerformanceVisualizer.Controls;

/// <summary>
/// Minimal markdown renderer for the AI analysis reports. Supports headings, paragraphs,
/// bold/italic/inline code, fenced code blocks, bullet/numbered lists and pipe tables.
/// (Markdown.Avalonia does not support Avalonia 12, so this stays dependency-free.)
/// </summary>
public class MarkdownViewer : ContentControl
{
    public static readonly StyledProperty<string?> MarkdownProperty =
        AvaloniaProperty.Register<MarkdownViewer, string?>(nameof(Markdown));

    public string? Markdown
    {
        get => GetValue(MarkdownProperty);
        set => SetValue(MarkdownProperty, value);
    }

    private static readonly FontFamily CodeFont = new("Consolas,monospace");
    private static readonly IBrush CodeBrush = Brush.Parse("#CE9178");
    private static readonly IBrush TextBrush = Brush.Parse("#DDDDDD");
    private static readonly IBrush HeadingBrush = Brush.Parse("#FFFFFF");
    private static readonly IBrush CodeBackground = Brush.Parse("#1E1E1E");
    private static readonly IBrush TableLineBrush = Brush.Parse("#454545");

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == MarkdownProperty)
            Content = string.IsNullOrWhiteSpace(Markdown) ? null : Build(Markdown!);
    }

    private static Control Build(string markdown)
    {
        var panel = new StackPanel { Spacing = 8 };
        var lines = markdown.Replace("\r\n", "\n").Split('\n');
        var i = 0;
        while (i < lines.Length)
        {
            var line = lines[i];
            if (string.IsNullOrWhiteSpace(line)) { i++; continue; }

            if (line.TrimStart().StartsWith("```"))
            {
                i++;
                var code = new List<string>();
                while (i < lines.Length && !lines[i].TrimStart().StartsWith("```"))
                    code.Add(lines[i++]);
                i++;
                panel.Children.Add(CodeBlock(string.Join("\n", code)));
                continue;
            }

            if (line.StartsWith('#'))
            {
                var level = line.TakeWhile(c => c == '#').Count();
                panel.Children.Add(Heading(line.TrimStart('#').Trim(), level));
                i++;
                continue;
            }

            if (IsTableRow(line) && i + 1 < lines.Length && IsTableSeparator(lines[i + 1]))
            {
                var rows = new List<string> { line };
                i += 2;
                while (i < lines.Length && IsTableRow(lines[i]))
                    rows.Add(lines[i++]);
                panel.Children.Add(Table(rows));
                continue;
            }

            var listMatch = Regex.Match(line, @"^(\s*)([-*]|\d+\.)\s+(.*)$");
            if (listMatch.Success)
            {
                panel.Children.Add(ListItem(
                    indent: listMatch.Groups[1].Value.Length,
                    marker: listMatch.Groups[2].Value == "-" || listMatch.Groups[2].Value == "*" ? "•" : listMatch.Groups[2].Value,
                    text: listMatch.Groups[3].Value));
                i++;
                continue;
            }

            var paragraph = new List<string> { line };
            i++;
            while (i < lines.Length && !string.IsNullOrWhiteSpace(lines[i]) && !IsBlockStart(lines[i]))
                paragraph.Add(lines[i++]);
            panel.Children.Add(Paragraph(string.Join(" ", paragraph)));
        }
        return panel;
    }

    private static bool IsBlockStart(string line) =>
        line.StartsWith('#') || line.TrimStart().StartsWith("```") || IsTableRow(line) ||
        Regex.IsMatch(line, @"^\s*([-*]|\d+\.)\s+");

    private static bool IsTableRow(string line) => line.TrimStart().StartsWith('|');

    private static bool IsTableSeparator(string line) =>
        IsTableRow(line) && Regex.IsMatch(line, @"^\s*\|?[\s:|-]+\|?\s*$") && line.Contains('-');

    private static Control Heading(string text, int level)
    {
        var tb = new SelectableTextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            FontWeight = FontWeight.Bold,
            Foreground = HeadingBrush,
            FontSize = level switch { 1 => 18, 2 => 15, _ => 13 },
            Margin = new Thickness(0, level == 1 ? 4 : 8, 0, 0),
        };
        tb.Inlines!.AddRange(ParseInlines(text));
        return tb;
    }

    private static Control Paragraph(string text)
    {
        var tb = new SelectableTextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12.5,
            Foreground = TextBrush,
            LineSpacing = 3,
        };
        tb.Inlines!.AddRange(ParseInlines(text));
        return tb;
    }

    private static Control ListItem(int indent, string marker, string text)
    {
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*"),
            Margin = new Thickness(8 + indent * 6, 0, 0, 0),
        };
        var bullet = new TextBlock
        {
            Text = marker,
            FontSize = 12.5,
            Foreground = TextBrush,
            Margin = new Thickness(0, 0, 6, 0),
            VerticalAlignment = VerticalAlignment.Top,
        };
        var content = new SelectableTextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12.5,
            Foreground = TextBrush,
            LineSpacing = 3,
        };
        content.Inlines!.AddRange(ParseInlines(text));
        Grid.SetColumn(content, 1);
        grid.Children.Add(bullet);
        grid.Children.Add(content);
        return grid;
    }

    private static Control CodeBlock(string code)
    {
        return new Border
        {
            Background = CodeBackground,
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(8),
            Child = new SelectableTextBlock
            {
                Text = code,
                FontFamily = CodeFont,
                FontSize = 12,
                Foreground = CodeBrush,
                TextWrapping = TextWrapping.Wrap,
            },
        };
    }

    private static Control Table(List<string> rows)
    {
        var grid = new Grid();
        var cells = rows
            .Where(r => !IsTableSeparator(r))
            .Select(r => r.Trim().Trim('|').Split('|').Select(c => c.Trim()).ToArray())
            .ToList();
        var columnCount = cells.Max(r => r.Length);
        for (var c = 0; c < columnCount; c++)
            grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));

        for (var r = 0; r < cells.Count; r++)
        {
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            for (var c = 0; c < cells[r].Length; c++)
            {
                var tb = new SelectableTextBlock
                {
                    FontSize = 12,
                    Foreground = TextBrush,
                    FontWeight = r == 0 ? FontWeight.Bold : FontWeight.Normal,
                };
                tb.Inlines!.AddRange(ParseInlines(cells[r][c]));
                var cell = new Border
                {
                    Padding = new Thickness(8, 3),
                    BorderBrush = TableLineBrush,
                    BorderThickness = new Thickness(0, 0, 0, r == 0 ? 1 : 0),
                    Child = tb,
                };
                Grid.SetRow(cell, r);
                Grid.SetColumn(cell, c);
                grid.Children.Add(cell);
            }
        }

        return new ScrollViewer
        {
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            Content = grid,
        };
    }

    private static readonly Regex InlinePattern = new(
        @"(\*\*(?<b>.+?)\*\*)|(`(?<c>[^`]+)`)|(\*(?<i>\S(?:[^*]*\S)?)\*)",
        RegexOptions.Compiled);

    private static IEnumerable<Inline> ParseInlines(string text, bool bold = false, bool italic = false)
    {
        var pos = 0;
        foreach (Match m in InlinePattern.Matches(text))
        {
            if (m.Index > pos)
                yield return MakeRun(text[pos..m.Index], bold, italic, code: false);
            if (m.Groups["b"].Success)
                foreach (var run in ParseInlines(m.Groups["b"].Value, bold: true, italic))
                    yield return run;
            else if (m.Groups["c"].Success)
                yield return MakeRun(m.Groups["c"].Value, bold, italic, code: true);
            else
                foreach (var run in ParseInlines(m.Groups["i"].Value, bold, italic: true))
                    yield return run;
            pos = m.Index + m.Length;
        }
        if (pos < text.Length)
            yield return MakeRun(text[pos..], bold, italic, code: false);
    }

    private static Run MakeRun(string text, bool bold, bool italic, bool code)
    {
        var run = new Run(text);
        if (bold) run.FontWeight = FontWeight.Bold;
        if (italic) run.FontStyle = FontStyle.Italic;
        if (code)
        {
            run.FontFamily = CodeFont;
            run.Foreground = CodeBrush;
        }
        return run;
    }
}
