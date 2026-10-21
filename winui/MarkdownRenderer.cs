using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using Markdig.Extensions.Tables;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;

namespace CraftLauncher.WinUI;

public sealed partial class MainWindow
{
    StackPanel RenderMarkdown(string markdown)
    {
        return RenderBlocks(Markdown.Parse(markdown, new MarkdownPipelineBuilder().UsePipeTables().Build()));
    }

    StackPanel RenderBlocks(ContainerBlock blocks)
    {
        var root = new StackPanel { Spacing = 9 };
        foreach (var block in blocks)
        {
            switch (block)
            {
                case HeadingBlock heading:
                    root.Children.Add(RenderInlines(heading.Inline, heading.Level switch { 1 => 23, 2 => 20, 3 => 17, _ => 15 }, true));
                    break;
                case ParagraphBlock paragraph:
                    root.Children.Add(RenderInlines(paragraph.Inline, 13));
                    AddImages(root, paragraph.Inline);
                    break;
                case FencedCodeBlock fenced:
                    AddCode(root, fenced.Lines.ToString());
                    break;
                case CodeBlock code:
                    AddCode(root, code.Lines.ToString());
                    break;
                case ThematicBreakBlock:
                    root.Children.Add(new Border { Height = 1, Background = Brush(lightTheme ? "#E2E3E8" : "#454653"), Margin = new Thickness(0, 4, 0, 4) });
                    break;
                case QuoteBlock quote:
                    var quoteContent = RenderBlocks(quote);
                    root.Children.Add(new Border { BorderBrush = Brush(lightTheme ? "#BFC2CD" : "#555766"), BorderThickness = new Thickness(3, 0, 0, 0), Padding = new Thickness(12, 4, 4, 4), Child = quoteContent });
                    break;
                case ListBlock list:
                    AddList(root, list);
                    break;
                case Table table:
                    var grid = new Grid { ColumnSpacing = 12, RowSpacing = 8 };
                    foreach (TableRow row in table)
                    {
                        var rowIndex = grid.RowDefinitions.Count; grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                        for (var column = 0; column < row.Count; column++)
                        {
                            if (grid.ColumnDefinitions.Count <= column) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                            var cell = RenderBlocks((TableCell)row[column]); Grid.SetRow(cell, rowIndex); Grid.SetColumn(cell, column); grid.Children.Add(cell);
                            if (row.IsHeader) foreach (var label in cell.Children.OfType<TextBlock>()) label.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
                        }
                    }
                    root.Children.Add(grid);
                    break;
                case HtmlBlock html:
                    AddHtmlImages(root, html.Lines.ToString());
                    break;
            }
        }
        return root;
    }

    static void MoveInlines(InlineCollection source, InlineCollection target)
    {
        while (source.Count > 0) { var item = source[0]; source.RemoveAt(0); target.Add(item); }
    }
    void AddImages(StackPanel root, ContainerInline? container)
    {
        if (container == null) return;
        for (var node = container.FirstChild; node != null; node = node.NextSibling)
        {
            if (node is LinkInline { IsImage: true } image) AddMarkdownImage(root, image.Url);
            else if (node is HtmlInline html) AddHtmlImages(root, html.Tag);
            else if (node is ContainerInline nested) AddImages(root, nested);
        }
    }
    void AddHtmlImages(StackPanel root, string html)
    {
        foreach (System.Text.RegularExpressions.Match match in System.Text.RegularExpressions.Regex.Matches(html, "<img\\b[^>]*?src\\s*=\\s*[\"']([^\"']+)[\"']", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            AddMarkdownImage(root, System.Net.WebUtility.HtmlDecode(match.Groups[1].Value));
    }
    void AddMarkdownImage(StackPanel root, string? url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != "https") return;
        var image = new Image { Source = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(uri), Stretch = Stretch.Uniform, MaxHeight = 340, HorizontalAlignment = HorizontalAlignment.Left };
        image.ImageFailed += (_, _) => image.Visibility = Visibility.Collapsed;
        root.Children.Add(image);
    }
    TextBlock RenderInlines(ContainerInline? inlines, double size, bool bold = false)
    {
        var text = new TextBlock { FontSize = size, FontWeight = bold ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal, TextWrapping = TextWrapping.Wrap, LineHeight = size * 1.5 };
        if (inlines == null) return text;
        for (var inline = inlines.FirstChild; inline != null; inline = inline.NextSibling)
        {
            switch (inline)
            {
                case LiteralInline literal:
                    text.Inlines.Add(new Run { Text = literal.Content.ToString() });
                    break;
                case CodeInline code:
                    text.Inlines.Add(new Run { Text = code.Content, FontFamily = new FontFamily("Cascadia Mono, Consolas") });
                    break;
                case LinkInline link when link.IsImage:
                    break;
                case LinkInline link:
                    var content = RenderInlines(link, size);
                    if (Uri.TryCreate(link.Url, UriKind.Absolute, out var uri) && uri.Scheme == "https")
                    {
                        var hyperlink = new Hyperlink { NavigateUri = uri };
                        MoveInlines(content.Inlines, hyperlink.Inlines);
                        text.Inlines.Add(hyperlink);
                    }
                    else MoveInlines(content.Inlines, text.Inlines);
                    break;
                case EmphasisInline emphasis:
                    var run = new Span();
                    if (emphasis.DelimiterCount >= 2) run.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
                    else run.FontStyle = Windows.UI.Text.FontStyle.Italic;
                    MoveInlines(RenderInlines(emphasis, size).Inlines, run.Inlines);
                    text.Inlines.Add(run);
                    break;
                case LineBreakInline:
                    text.Inlines.Add(new LineBreak());
                    break;
                case HtmlInline:
                    break;
                case ContainerInline container:
                    MoveInlines(RenderInlines(container, size).Inlines, text.Inlines);
                    break;
            }
        }
        return text;
    }

    void AddList(StackPanel root, ListBlock list)
    {
        var index = int.TryParse(list.OrderedStart, out var start) ? start : 1;
        foreach (var item in list)
        {
            if (item is not ListItemBlock listItem) continue;
            var first = listItem.OfType<ParagraphBlock>().FirstOrDefault();
            var marker = list.IsOrdered ? $"{index++}. " : "• ";
            var row = new Grid { ColumnSpacing = 8, Margin = new Thickness(8, 0, 0, 0) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.Children.Add(Text(marker, 13));
            var content = RenderBlocks(listItem); Grid.SetColumn(content, 1); row.Children.Add(content); root.Children.Add(row);
        }
    }

    void AddCode(StackPanel root, string value)
    {
        var block = new Border { Padding = new Thickness(10, 8, 10, 8), CornerRadius = new CornerRadius(4), Background = Brush(lightTheme ? "#F1F2F5" : "#171820") };
        block.Child = new TextBlock { Text = value, FontFamily = new FontFamily("Cascadia Mono, Consolas"), FontSize = 12, TextWrapping = TextWrapping.Wrap };
        root.Children.Add(block);
    }
}
