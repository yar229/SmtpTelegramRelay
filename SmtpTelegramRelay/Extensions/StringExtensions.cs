using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using HtmlAgilityPack;

namespace SmtpTelegramRelay.Extensions;

internal static class StringExtensions
{
    public static string ConvertToTelegramHtml(this string html)
    {
        HtmlDocument doc = new HtmlDocument();
        doc.LoadHtml(html);

        using StringWriter sw = new(CultureInfo.InvariantCulture);
        ConvertToTelegramHtml(doc.DocumentNode, sw);
        sw.Flush();

        return sw.ToString();
    }

    private static readonly List<string> TelegramAllowedTags = new() { "pre", "u", "del", "strike", "s", "code", "em", "i", "strong", "b", "blockquote", };

    private static readonly Dictionary<string, (Func<HtmlNode, string> Before, Func<HtmlNode, string> After)> ReplaceTagsF = new()
    {
        { "td", (_ => string.Empty, _ => "    ") },
        { "tr", (_ => string.Empty, _ => "\r\n") },
        { "title", (_ => "<b>", _ => "</b>\r\n") },
        { "h1", (_ => "<b><u>", _ => "</u></b>\r\n") },
        { "div", (_ => "\r\n", _ => string.Empty) },
        { "p", (_ => "\r\n", _ => string.Empty) },
        { "br", (_ => "\r\n", _ => string.Empty) },
        { "hr", (_ => "\r\n", _ => string.Empty) },

        { "ul", (_ => "\r\n", _ => string.Empty) },
        { "li", (
            node =>
            {
                int tabCount = node.Ancestors("ul").Count() - 1;
                if (tabCount < 0)
                    tabCount = 0;
                return $"{new string(' ', 4 * tabCount)}• ";
                }, 
            _ => "\r\n") },
        { "details", (_ => "<tg-spoiler>", _ => "</tg-spoiler>") },
        { "a", (
            node =>
            {
                var href = node.GetAttributes("href").FirstOrDefault()?.Value;
                if (string.IsNullOrEmpty(href))
                    return "<a>";
                return $"<a href=\"{href}\">";
            },
            _ => "</a>") },
    };

    private static void ConvertToTelegramHtml(HtmlNode node, TextWriter outText)
    {
        switch (node.NodeType)
        {
            case HtmlNodeType.Comment:
                break;
            case HtmlNodeType.Document:
                ConvertContentToTelegramHtml(node, outText);
                break;
            case HtmlNodeType.Text:
                string parentName = node.ParentNode.Name;
                if (parentName is "script" or "style")
                    break;
                var html = ((HtmlTextNode)node).Text;
                if (HtmlNode.IsOverlappedClosingElement(html))
                    break;
                if (html.Trim().Length > 0)
                    outText.Write(HtmlEntity.DeEntitize(html)
                        .Replace("\r", "")
                        .Replace("\n", ""));
                break;

            case HtmlNodeType.Element:
                if (TelegramAllowedTags.Contains(node.Name))
                {
                    outText.Write($"<{node.Name}>");
                    ConvertContentToTelegramHtml(node, outText);
                    outText.Write($"</{node.Name}>");
                }
                else if (ReplaceTagsF.TryGetValue(node.Name, out var replacement))
                {
                    outText.Write(replacement.Before(node));
                    ConvertContentToTelegramHtml(node, outText);
                    outText.Write(replacement.After(node));
                }
                else
                    if (node.HasChildNodes)
                        ConvertContentToTelegramHtml(node, outText);
                break;
        }
    }

    private static void ConvertContentToTelegramHtml(HtmlNode node, TextWriter outText)
    {
        foreach (HtmlNode subnode in node.ChildNodes)
            ConvertToTelegramHtml(subnode, outText);
    }
}