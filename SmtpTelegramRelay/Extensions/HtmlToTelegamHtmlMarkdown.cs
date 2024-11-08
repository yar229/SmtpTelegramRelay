using System.Globalization;
using HtmlAgilityPack;

namespace SmtpTelegramRelay.Extensions;

internal static class HtmlToTelegamHtmlMarkdown
{
    public static string ConvertToTelegramHtml(this string html)
    {
        HtmlDocument doc = new HtmlDocument();
        doc.LoadHtml(html);

        using StringWriter sw = new(CultureInfo.InvariantCulture);
        ConvertTo(doc.DocumentNode, sw);
        sw.Flush();

        return sw.ToString();
    }

    private const string CrLf = "\r\n";
    private const string Tab = "    ";

    private static Action<HtmlNode, TextWriter> ToTag(string tag)
        => (node, tw) =>
            {
                tw.Write($"<{tag}>");
                ConvertContentTo(node, tw);
                tw.Write($"</{tag}>"); ;
            };
    private static Action<HtmlNode, TextWriter> Frame(string before, string after)
        => (node, tw) =>
        {
            tw.Write(before);
            ConvertContentTo(node, tw);
            tw.Write(after);
        };

    private static Action<HtmlNode, TextWriter> ToTag(string[] tags, string after)
        => (node, tw) =>
        {
            foreach (var tag in tags)
                tw.Write($"<{tag}>");
            ConvertContentTo(node, tw);
            foreach (var tag in tags.Reverse())
                tw.Write($"</{tag}>");
            tw.Write(after);
        };

    private static Action<HtmlNode, TextWriter> ToTag(string tag, string after)
        => (node, tw) =>
        {
            tw.Write($"<{tag}>");
            ConvertContentTo(node, tw);
            tw.Write($"</{tag}>");
            tw.Write(after);
        };

    private static Action<HtmlNode, TextWriter> Before(string before)
        => (node, tw) =>
        {
            tw.Write(before);
            ConvertContentTo(node, tw);
        };

    private static Action<HtmlNode, TextWriter> After(string after)
        => (node, tw) =>
        {
            ConvertContentTo(node, tw);
            tw.Write(after);
        };

    private static readonly Dictionary<string, Action<HtmlNode, TextWriter>> TagConverters = new()
    {   
        { "td",    After(Tab) },
        { "tr",    After(CrLf) },
        { "title", ToTag("b", CrLf) },
        { "h1",    ToTag(["b", "u"], CrLf) },
        { "q",     ToTag("blockquote") },
        { "div",   Before(CrLf) },
        { "p",     Before(CrLf) },
        { "br",    Before(CrLf) },
        { "hr",    Before(CrLf) },
        { "ul",    Before(CrLf) },
        { "details", (node, tw) =>
            {
                var summaryNode = node.ChildNodes.FindFirst("summary");
                if (summaryNode != null)
                {
                    ConvertContentTo(summaryNode, tw);
                    node.RemoveChild(summaryNode);
                }
                tw.Write("<tg-spoiler>");
                ConvertContentTo(node, tw);
                tw.Write("</tg-spoiler>");
            }
        },
        { "li", (node, tw) =>
            {
                int tabCount = node.Ancestors("ul").Count() - 1;
                if (tabCount < 0)
                    tabCount = 0;
                tw.Write($"{new string(' ', Tab.Length * tabCount)}• ");
                ConvertContentTo(node, tw);
                tw.Write(CrLf);
            }
        },
        { "a", (node, tw) =>
            {
                var href = node.GetAttributes("href").FirstOrDefault()?.Value;
                if (string.IsNullOrEmpty(href))
                    ConvertContentTo(node, tw);
                else
                {
                    tw.Write($"<a href=\"{href}\">");
                    ConvertContentTo(node, tw);
                    tw.Write("</a>");
                }
            }
        }
    };

    private static readonly HashSet<string> AllowedTags = new() { "pre", "u", "del", "strike", "s", "code", "em", "i", "strong", "b", "blockquote", "tg-spoiler" };

    private static void ConvertTo(HtmlNode node, TextWriter outText)
    {
        switch (node.NodeType)
        {
            case HtmlNodeType.Comment:
                break;
            case HtmlNodeType.Document:
                ConvertContentTo(node, outText);
                break;
            case HtmlNodeType.Text:
                string parentName = node.ParentNode.Name;
                if (parentName is "script" or "style")
                    break;
                var html = ((HtmlTextNode)node).Text;
                if (HtmlNode.IsOverlappedClosingElement(html))
                    break;
                if (html.Trim().Length > 0)
                    outText.Write(HtmlEntity.DeEntitize(html
                        .Replace("\r", "", StringComparison.OrdinalIgnoreCase)
                        .Replace("\n", "", StringComparison.OrdinalIgnoreCase)));
                break;

            case HtmlNodeType.Element:
                if (AllowedTags.Contains(node.Name))
                    ToTag(node.Name)(node, outText);
                else if (TagConverters.TryGetValue(node.Name, out var replacement))
                    replacement(node, outText);
                else
                    ConvertContentTo(node, outText);
                break;
        }
    }

    private static void ConvertContentTo(HtmlNode node, TextWriter outText)
    {
        foreach (HtmlNode subnode in node.ChildNodes)
            ConvertTo(subnode, outText);
    }
}