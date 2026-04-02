using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using HERMMapperApp.ViewModels;
using Microsoft.AspNetCore.Hosting;

namespace HERMMapperApp.Services;

public sealed partial class ModelDiagramPosterSvgService(IWebHostEnvironment environment)
{
    private const double LayoutRowTolerance = 0.75;
    private const double ProductInsetX = 5.9055;
    private const double ProductTopGap = 6.0;
    private const double ProductGap = 5.9055;
    private const double ProductPaddingX = 7.5;
    private const double ProductPaddingY = 5.5;
    private const double ProductFontSize = 12.0;
    private const double ProductLineHeight = 14.0;
    private const double ProductCornerRadius = 14.0;

    public string BuildSvg(ModelDiagramReportViewModel model)
    {
        var root = LoadCanvasTemplate(model.ScopeKey);
        if (root is null)
        {
            return string.Empty;
        }

        var mappedItemsByCode = BuildMappedLookup(model);

        AttachMappedItems(root, mappedItemsByCode);
        LayoutNode(root);
        UpdateAbsolutePositions(root, 0, 0);
        TrimCanvas(root);
        UpdateAbsolutePositions(root, 0, 0);

        return RenderSvg(root);
    }

    public static string BuildDownloadFileName(string scopeKey) => $"herm-{scopeKey.Trim().ToLowerInvariant()}-poster.svg";

    private TemplateNode? LoadCanvasTemplate(string scopeKey)
    {
        var fileName = scopeKey.Trim().ToLowerInvariant() switch
        {
            "arm" => "HERM-ARM-V320-model.drawio",
            "brm" => "HERM-BRM-V320-model.drawio",
            _ => "HERM-TRM-V320-model.drawio"
        };

        var path = ResolveTemplatePath(fileName);
        if (path is null)
        {
            return null;
        }

        var document = XDocument.Load(path, LoadOptions.None);
        var cells = document
            .Descendants("mxCell")
            .Select(ParseNode)
            .ToDictionary(x => x.Id, StringComparer.Ordinal);

        foreach (var node in cells.Values)
        {
            if (!string.IsNullOrWhiteSpace(node.ParentId) && cells.TryGetValue(node.ParentId, out var parent))
            {
                node.Parent = parent;
                parent.Children.Add(node);
            }
        }

        var root = cells.TryGetValue("1", out var rootNode)
            ? rootNode
            : throw new InvalidOperationException($"The draw.io template '{fileName}' does not contain a root node.");

        UpdateAbsolutePositions(root, 0, 0);

        var background = cells.Values
            .Where(IsCanvasBackground)
            .OrderByDescending(x => x.Width * x.Height)
            .FirstOrDefault()
            ?? throw new InvalidOperationException($"The draw.io template '{fileName}' does not contain a model canvas.");

        var sourceRoot = background.Parent ?? root;

        var syntheticRoot = new TemplateNode
        {
            Id = "poster-root",
            IsGroup = true,
            Width = background.Width,
            Height = background.Height,
            OriginalWidth = background.Width,
            OriginalHeight = background.Height
        };

        foreach (var clone in sourceRoot.Children
                     .Where(x => x != background)
                     .Select(x => CloneCanvasSubtree(x, background))
                     .Where(x => x is not null)
                     .Cast<TemplateNode>()
                     .OrderBy(x => x.AbsoluteY)
                     .ThenBy(x => x.AbsoluteX))
        {
            clone.X = clone.AbsoluteX - background.AbsoluteX;
            clone.Y = clone.AbsoluteY - background.AbsoluteY;
            clone.Parent = syntheticRoot;
            syntheticRoot.Children.Add(clone);
        }

        var backgroundClone = CloneSubtree(background);
        backgroundClone.X = 0;
        backgroundClone.Y = 0;
        backgroundClone.Parent = syntheticRoot;
        syntheticRoot.Children.Insert(0, backgroundClone);

        return syntheticRoot;
    }

    private static void TrimCanvas(TemplateNode root)
    {
        var bounds = GetBounds(root);
        if (bounds is null)
        {
            return;
        }

        foreach (var child in root.Children)
        {
            child.X -= bounds.Value.Left;
            child.Y -= bounds.Value.Top;
        }

        root.Width = bounds.Value.Right - bounds.Value.Left;
        root.Height = bounds.Value.Bottom - bounds.Value.Top;
    }

    private string? ResolveTemplatePath(string fileName)
    {
        var candidates = new[]
        {
            Path.Combine(environment.ContentRootPath, "Model", fileName),
            Path.Combine(environment.ContentRootPath, ".local.data", "Model", fileName),
            Path.Combine(environment.ContentRootPath, "..", "Model", fileName),
            Path.Combine(environment.ContentRootPath, "..", ".local.data", "Model", fileName),
            Path.Combine(environment.ContentRootPath, "..", "..", "Model", fileName),
            Path.Combine(environment.ContentRootPath, "..", "..", ".local.data", "Model", fileName)
        };

        return candidates
            .Select(Path.GetFullPath)
            .FirstOrDefault(File.Exists);
    }

    private static TemplateNode ParseNode(XElement element)
    {
        var geometry = element.Element("mxGeometry");
        var x = ParseDouble(geometry?.Attribute("x")?.Value);
        var y = ParseDouble(geometry?.Attribute("y")?.Value);
        var width = ParseDouble(geometry?.Attribute("width")?.Value);
        var height = ParseDouble(geometry?.Attribute("height")?.Value);
        var style = ParseStyle(element.Attribute("style")?.Value);

        return new TemplateNode
        {
            Id = element.Attribute("id")?.Value ?? Guid.NewGuid().ToString("N"),
            ParentId = element.Attribute("parent")?.Value,
            Value = element.Attribute("value")?.Value ?? string.Empty,
            Style = style,
            IsGroup = style.ContainsKey("group"),
            X = x,
            Y = y,
            Width = width,
            Height = height,
            OriginalX = x,
            OriginalY = y,
            OriginalWidth = width,
            OriginalHeight = height
        };
    }

    private static Dictionary<string, List<MappedPosterItem>> BuildMappedLookup(ModelDiagramReportViewModel model)
    {
        var lookup = new Dictionary<string, List<MappedPosterItem>>(StringComparer.OrdinalIgnoreCase);

        foreach (var component in model.Domains.SelectMany(x => x.Capabilities).SelectMany(x => x.Components))
        {
            var normalizedCode = NormalizeCode(component.Code);
            if (string.IsNullOrWhiteSpace(normalizedCode))
            {
                continue;
            }

            lookup[normalizedCode] = component.Products
                .Select(product => new MappedPosterItem(
                    product.Name,
                    product.HasLink && !string.IsNullOrWhiteSpace(product.LinkController) && !string.IsNullOrWhiteSpace(product.LinkAction) && product.LinkId.HasValue
                        ? $"/{product.LinkController}/{product.LinkAction}/{product.LinkId.Value}"
                        : null))
                .ToList();
        }

        return lookup;
    }

    private static void AttachMappedItems(TemplateNode node, IReadOnlyDictionary<string, List<MappedPosterItem>> mappedItemsByCode)
    {
        foreach (var child in node.Children)
        {
            AttachMappedItems(child, mappedItemsByCode);
        }

        var componentCode = ExtractCode(node.Value);
        if (!IsLeafComponentCell(node) || string.IsNullOrWhiteSpace(componentCode) || !mappedItemsByCode.TryGetValue(componentCode, out var mappedItems) || mappedItems.Count == 0)
        {
            return;
        }

        var itemWidth = Math.Max(80, node.Width - (ProductInsetX * 2));
        var overlays = mappedItems
            .Select(item => CreateOverlayItem(item, itemWidth))
            .ToList();

        var extraHeight = ProductTopGap + overlays.Sum(x => x.Height) + (ProductGap * Math.Max(0, overlays.Count - 1)) + ProductTopGap;
        node.OverlayItems = overlays;
        node.Height = node.OriginalHeight + extraHeight;
    }

    private static OverlayItem CreateOverlayItem(MappedPosterItem item, double itemWidth)
    {
        var textWidth = Math.Max(24, itemWidth - (ProductPaddingX * 2));
        var lines = WrapText(item.Label, textWidth, ProductFontSize);
        var height = (ProductPaddingY * 2) + (lines.Count * ProductLineHeight);

        return new OverlayItem(item.Label, item.Href, lines, height);
    }

    private static void LayoutNode(TemplateNode node)
    {
        foreach (var child in node.Children)
        {
            LayoutNode(child);
        }

        if (node.Children.Count == 0)
        {
            node.Height = Math.Max(node.Height, node.OriginalHeight);
            return;
        }

        var backgroundChild = FindBackgroundChild(node);
        var layoutChildren = node.Children
            .Where(child => !ReferenceEquals(child, backgroundChild))
            .OrderBy(child => child.OriginalY)
            .ThenBy(child => child.OriginalX)
            .ToList();

        if (layoutChildren.Count == 0)
        {
            node.Height = Math.Max(node.Height, node.OriginalHeight);
            return;
        }

        var rows = GroupRows(layoutChildren);
        var cumulativeShift = 0.0;

        foreach (var row in rows)
        {
            var rowTop = row.Min(child => child.OriginalY);
            var rowOriginalBottom = row.Max(child => child.OriginalY + child.OriginalHeight);
            var rowOriginalHeight = rowOriginalBottom - rowTop;
            var rowNewBottom = row.Max(child => (child.OriginalY - rowTop) + child.Height);
            var rowNewHeight = Math.Max(rowOriginalHeight, rowNewBottom);
            var placedTop = rowTop + cumulativeShift;

            foreach (var child in row)
            {
                child.Y = placedTop + (child.OriginalY - rowTop);
            }

            cumulativeShift += rowNewHeight - rowOriginalHeight;
        }

        var originalContentBottom = layoutChildren.Max(child => child.OriginalY + child.OriginalHeight);
        var currentContentBottom = layoutChildren.Max(child => child.Y + child.Height);
        var bottomPadding = Math.Max(0, node.OriginalHeight - originalContentBottom);
        node.Height = Math.Max(node.Height, currentContentBottom + bottomPadding);

        if (backgroundChild is not null)
        {
            backgroundChild.Height = node.Height;
        }
    }

    private static List<List<TemplateNode>> GroupRows(List<TemplateNode> children)
    {
        var rows = new List<List<TemplateNode>>();

        foreach (var child in children)
        {
            var row = rows.FirstOrDefault(existing => Math.Abs(existing[0].OriginalY - child.OriginalY) <= LayoutRowTolerance);
            if (row is null)
            {
                row = [];
                rows.Add(row);
            }

            row.Add(child);
        }

        foreach (var row in rows)
        {
            row.Sort((left, right) =>
            {
                var xComparison = left.OriginalX.CompareTo(right.OriginalX);
                return xComparison != 0 ? xComparison : left.OriginalY.CompareTo(right.OriginalY);
            });
        }

        rows.Sort((left, right) => left[0].OriginalY.CompareTo(right[0].OriginalY));
        return rows;
    }

    private static void UpdateAbsolutePositions(TemplateNode node, double parentX, double parentY)
    {
        node.AbsoluteX = parentX + node.X;
        node.AbsoluteY = parentY + node.Y;

        foreach (var child in node.Children)
        {
            UpdateAbsolutePositions(child, node.AbsoluteX, node.AbsoluteY);
        }
    }

    private static string RenderSvg(TemplateNode root)
    {
        var svg = new StringBuilder();
        svg.Append("<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 ");
        svg.Append(FormatNumber(root.Width));
        svg.Append(' ');
        svg.Append(FormatNumber(root.Height));
        svg.Append("\" width=\"100%\" height=\"100%\" preserveAspectRatio=\"xMidYMid meet\" class=\"diagram-poster-svg\" role=\"img\">");

        foreach (var child in root.Children.OrderBy(x => x.AbsoluteY).ThenBy(x => x.AbsoluteX))
        {
            RenderNode(svg, child);
        }

        svg.Append("</svg>");
        return svg.ToString();
    }

    private static void RenderNode(StringBuilder svg, TemplateNode node)
    {
        if (node.IsGroup)
        {
            foreach (var child in node.Children.OrderBy(x => x.AbsoluteY).ThenBy(x => x.AbsoluteX))
            {
                RenderNode(svg, child);
            }

            return;
        }

        if (IsImageCell(node))
        {
            RenderImage(svg, node);
        }
        else
        {
            RenderShape(svg, node);
        }

        RenderText(svg, node);

        if (node.OverlayItems.Count > 0)
        {
            RenderOverlayItems(svg, node);
        }
    }

    private static void RenderShape(StringBuilder svg, TemplateNode node)
    {
        var fill = GetFill(node);
        var stroke = GetStroke(node.Style);
        var strokeWidth = ParseDouble(GetStyleValue(node.Style, "strokeWidth"), 1.0);

        if (HasMappedOverlay(node))
        {
            stroke = "#c92d39";
            strokeWidth = 4;
        }

        if (string.Equals(fill, "none", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(stroke, "none", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var cornerRadius = GetCornerRadius(node);

        svg.Append("<rect x=\"");
        svg.Append(FormatNumber(node.AbsoluteX));
        svg.Append("\" y=\"");
        svg.Append(FormatNumber(node.AbsoluteY));
        svg.Append("\" width=\"");
        svg.Append(FormatNumber(node.Width));
        svg.Append("\" height=\"");
        svg.Append(FormatNumber(node.Height));
        svg.Append("\" rx=\"");
        svg.Append(FormatNumber(cornerRadius));
        svg.Append("\" ry=\"");
        svg.Append(FormatNumber(cornerRadius));
        svg.Append("\" fill=\"");
        svg.Append(fill);
        svg.Append("\" stroke=\"");
        svg.Append(stroke);
        svg.Append("\" stroke-width=\"");
        svg.Append(FormatNumber(strokeWidth));
        svg.Append("\" />");
    }

    private static void RenderImage(StringBuilder svg, TemplateNode node)
    {
        var image = GetStyleValue(node.Style, "image");
        if (string.IsNullOrWhiteSpace(image))
        {
            return;
        }

        svg.Append("<image x=\"");
        svg.Append(FormatNumber(node.AbsoluteX));
        svg.Append("\" y=\"");
        svg.Append(FormatNumber(node.AbsoluteY));
        svg.Append("\" width=\"");
        svg.Append(FormatNumber(node.Width));
        svg.Append("\" height=\"");
        svg.Append(FormatNumber(node.Height));
        svg.Append("\" href=\"");
        svg.Append(WebUtility.HtmlEncode(image));
        svg.Append("\" preserveAspectRatio=\"none\" />");
    }

    private static void RenderText(StringBuilder svg, TemplateNode node)
    {
        var text = ExtractPlainText(node.Value);
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        var fontSize = ParseDouble(GetStyleValue(node.Style, "fontSize"), 16.0);
        var fontFamily = string.IsNullOrWhiteSpace(GetStyleValue(node.Style, "fontFamily"))
            ? "Open Sans, Segoe UI, Arial, sans-serif"
            : $"{GetStyleValue(node.Style, "fontFamily")}, Segoe UI, Arial, sans-serif";
        var fontWeight = GetFontWeight(node.Style);
        var fill = GetFontColor(node.Style);
        var textAlign = GetTextAnchor(node.Style, out var horizontalAlignment);
        var verticalAlignment = GetStyleValue(node.Style, "verticalAlign");
        var paddingLeft = ParseDouble(GetStyleValue(node.Style, "spacingLeft"), 5.0);
        var paddingRight = ParseDouble(GetStyleValue(node.Style, "spacingRight"), 5.0);
        var paddingTop = ParseDouble(GetStyleValue(node.Style, "spacingTop"), 4.5);
        var lineHeight = fontSize * 1.24;
        var availableWidth = Math.Max(20, node.Width - paddingLeft - paddingRight);
        var lines = WrapText(text, availableWidth, fontSize);
        var totalHeight = lines.Count * lineHeight;

        var x = horizontalAlignment switch
        {
            "center" => node.AbsoluteX + (node.Width / 2),
            "right" => node.AbsoluteX + node.Width - paddingRight,
            _ => node.AbsoluteX + paddingLeft
        };

        var startY = string.Equals(verticalAlignment, "middle", StringComparison.OrdinalIgnoreCase)
            ? node.AbsoluteY + Math.Max(fontSize, ((node.Height - totalHeight) / 2) + fontSize)
            : node.AbsoluteY + paddingTop + fontSize;

        svg.Append("<text x=\"");
        svg.Append(FormatNumber(x));
        svg.Append("\" y=\"");
        svg.Append(FormatNumber(startY));
        svg.Append("\" fill=\"");
        svg.Append(fill);
        svg.Append("\" font-family=\"");
        svg.Append(WebUtility.HtmlEncode(fontFamily));
        svg.Append("\" font-size=\"");
        svg.Append(FormatNumber(fontSize));
        svg.Append("\" font-weight=\"");
        svg.Append(fontWeight);
        svg.Append("\" text-anchor=\"");
        svg.Append(textAlign);
        svg.Append("\" dominant-baseline=\"alphabetic\">");

        for (var index = 0; index < lines.Count; index++)
        {
            svg.Append("<tspan x=\"");
            svg.Append(FormatNumber(x));
            svg.Append("\" dy=\"");
            svg.Append(index == 0 ? "0" : FormatNumber(lineHeight));
            svg.Append("\">");
            svg.Append(WebUtility.HtmlEncode(lines[index]));
            svg.Append("</tspan>");
        }

        svg.Append("</text>");
    }

    private static void RenderOverlayItems(StringBuilder svg, TemplateNode node)
    {
        var currentY = node.AbsoluteY + node.OriginalHeight + ProductTopGap;
        var itemWidth = Math.Max(80, node.Width - (ProductInsetX * 2));
        var itemX = node.AbsoluteX + ProductInsetX;

        foreach (var item in node.OverlayItems)
        {
            var openLink = !string.IsNullOrWhiteSpace(item.Href);
            if (openLink)
            {
                svg.Append("<a href=\"");
                svg.Append(WebUtility.HtmlEncode(item.Href));
                svg.Append("\">");
            }

            svg.Append("<rect x=\"");
            svg.Append(FormatNumber(itemX));
            svg.Append("\" y=\"");
            svg.Append(FormatNumber(currentY));
            svg.Append("\" width=\"");
            svg.Append(FormatNumber(itemWidth));
            svg.Append("\" height=\"");
            svg.Append(FormatNumber(item.Height));
            svg.Append("\" rx=\"");
            svg.Append(FormatNumber(ProductCornerRadius));
            svg.Append("\" ry=\"");
            svg.Append(FormatNumber(ProductCornerRadius));
            svg.Append("\" fill=\"#f6f4ef\" stroke=\"#d6d0c3\" stroke-width=\"1\" />");

            var textX = itemX + ProductPaddingX;
            var textY = currentY + ProductPaddingY + ProductFontSize;

            svg.Append("<text x=\"");
            svg.Append(FormatNumber(textX));
            svg.Append("\" y=\"");
            svg.Append(FormatNumber(textY));
            svg.Append("\" fill=\"#1f2933\" font-family=\"Open Sans, Segoe UI, Arial, sans-serif\" font-size=\"");
            svg.Append(FormatNumber(ProductFontSize));
            svg.Append("\" font-weight=\"600\" text-anchor=\"start\" dominant-baseline=\"alphabetic\">");

            for (var index = 0; index < item.Lines.Count; index++)
            {
                svg.Append("<tspan x=\"");
                svg.Append(FormatNumber(textX));
                svg.Append("\" dy=\"");
                svg.Append(index == 0 ? "0" : FormatNumber(ProductLineHeight));
                svg.Append("\">");
                svg.Append(WebUtility.HtmlEncode(item.Lines[index]));
                svg.Append("</tspan>");
            }

            svg.Append("</text>");

            if (openLink)
            {
                svg.Append("</a>");
            }

            currentY += item.Height + ProductGap;
        }
    }

    private static TemplateNode CloneSubtree(TemplateNode node)
    {
        var clone = new TemplateNode
        {
            Id = node.Id,
            ParentId = node.ParentId,
            Value = node.Value,
            Style = new Dictionary<string, string>(node.Style, StringComparer.OrdinalIgnoreCase),
            IsGroup = node.IsGroup,
            X = node.X,
            Y = node.Y,
            Width = node.Width,
            Height = node.Height,
            OriginalX = node.OriginalX,
            OriginalY = node.OriginalY,
            OriginalWidth = node.OriginalWidth,
            OriginalHeight = node.OriginalHeight
        };

        foreach (var child in node.Children)
        {
            var childClone = CloneSubtree(child);
            childClone.Parent = clone;
            clone.Children.Add(childClone);
        }

        return clone;
    }

    private static TemplateNode? CloneCanvasSubtree(TemplateNode node, TemplateNode canvasBackground)
    {
        if (!ShouldIncludeInCanvas(node, canvasBackground))
        {
            return null;
        }

        var clone = new TemplateNode
        {
            Id = node.Id,
            ParentId = node.ParentId,
            Value = node.Value,
            Style = new Dictionary<string, string>(node.Style, StringComparer.OrdinalIgnoreCase),
            IsGroup = node.IsGroup,
            X = node.X,
            Y = node.Y,
            Width = node.Width,
            Height = node.Height,
            OriginalX = node.OriginalX,
            OriginalY = node.OriginalY,
            OriginalWidth = node.OriginalWidth,
            OriginalHeight = node.OriginalHeight,
            AbsoluteX = node.AbsoluteX,
            AbsoluteY = node.AbsoluteY
        };

        foreach (var child in node.Children)
        {
            var childClone = CloneCanvasSubtree(child, canvasBackground);
            if (childClone is null)
            {
                continue;
            }

            childClone.Parent = clone;
            clone.Children.Add(childClone);
        }

        return clone;
    }

    private static Bounds? GetBounds(TemplateNode root)
    {
        Bounds? bounds = null;

        foreach (var node in EnumerateNodes(root))
        {
            if (node.IsGroup)
            {
                continue;
            }

            var right = node.AbsoluteX + node.Width;
            var bottom = node.AbsoluteY + node.Height;
            bounds = bounds is null
                ? new Bounds(node.AbsoluteX, node.AbsoluteY, right, bottom)
                : bounds.Value.Expand(node.AbsoluteX, node.AbsoluteY, right, bottom);
        }

        return bounds;
    }

    private static IEnumerable<TemplateNode> EnumerateNodes(TemplateNode node)
    {
        yield return node;

        foreach (var child in node.Children)
        {
            foreach (var descendant in EnumerateNodes(child))
            {
                yield return descendant;
            }
        }
    }

    private static TemplateNode? FindBackgroundChild(TemplateNode node) =>
        node.Children
            .Where(child => !child.IsGroup &&
                            Math.Abs(child.OriginalX) <= LayoutRowTolerance &&
                            Math.Abs(child.OriginalY) <= LayoutRowTolerance &&
                            Math.Abs(child.OriginalWidth - node.OriginalWidth) <= 2 &&
                            Math.Abs(child.OriginalHeight - node.OriginalHeight) <= 2)
            .OrderByDescending(child => child.OriginalWidth * child.OriginalHeight)
            .FirstOrDefault();

    private static bool IntersectsCanvas(TemplateNode node, TemplateNode canvasBackground)
    {
        var nodeLeft = node.AbsoluteX;
        var nodeTop = node.AbsoluteY;
        var nodeRight = nodeLeft + node.Width;
        var nodeBottom = nodeTop + node.Height;
        var canvasLeft = canvasBackground.AbsoluteX;
        var canvasTop = canvasBackground.AbsoluteY;
        var canvasRight = canvasLeft + canvasBackground.Width;
        var canvasBottom = canvasTop + canvasBackground.Height;

        return nodeRight > canvasLeft &&
               nodeBottom > canvasTop &&
               nodeLeft < canvasRight &&
               nodeTop < canvasBottom;
    }

    private static bool ShouldIncludeInCanvas(TemplateNode node, TemplateNode canvasBackground)
    {
        if (node.Children.Count > 0)
        {
            return node.Children.Any(child => ShouldIncludeInCanvas(child, canvasBackground));
        }

        if (!IntersectsCanvas(node, canvasBackground))
        {
            return false;
        }

        if (IsNodeCenterInsideCanvas(node, canvasBackground))
        {
            return true;
        }

        var nodeArea = Math.Max(1, node.Width * node.Height);
        return GetCanvasOverlapArea(node, canvasBackground) / nodeArea >= 0.6;
    }

    private static bool IsNodeCenterInsideCanvas(TemplateNode node, TemplateNode canvasBackground)
    {
        var centerX = node.AbsoluteX + (node.Width / 2);
        var centerY = node.AbsoluteY + (node.Height / 2);
        var canvasLeft = canvasBackground.AbsoluteX;
        var canvasTop = canvasBackground.AbsoluteY;
        var canvasRight = canvasLeft + canvasBackground.Width;
        var canvasBottom = canvasTop + canvasBackground.Height;

        return centerX >= canvasLeft &&
               centerX <= canvasRight &&
               centerY >= canvasTop &&
               centerY <= canvasBottom;
    }

    private static double GetCanvasOverlapArea(TemplateNode node, TemplateNode canvasBackground)
    {
        var canvasLeft = canvasBackground.AbsoluteX;
        var canvasTop = canvasBackground.AbsoluteY;
        var canvasRight = canvasLeft + canvasBackground.Width;
        var canvasBottom = canvasTop + canvasBackground.Height;
        var overlapLeft = Math.Max(node.AbsoluteX, canvasLeft);
        var overlapTop = Math.Max(node.AbsoluteY, canvasTop);
        var overlapRight = Math.Min(node.AbsoluteX + node.Width, canvasRight);
        var overlapBottom = Math.Min(node.AbsoluteY + node.Height, canvasBottom);

        return Math.Max(0, overlapRight - overlapLeft) * Math.Max(0, overlapBottom - overlapTop);
    }

    private static bool IsCanvasBackground(TemplateNode node) =>
        !node.IsGroup &&
        string.Equals(GetStyleValue(node.Style, "fillColor"), "#b2b2b2", StringComparison.OrdinalIgnoreCase) &&
        ParseDouble(GetStyleValue(node.Style, "strokeWidth"), 0) >= 2;

    private static bool IsLeafComponentCell(TemplateNode node) =>
        !node.IsGroup &&
        !IsImageCell(node) &&
        (string.Equals(NormalizeColor(GetStyleValue(node.Style, "fillColor")), "#ffffff", StringComparison.OrdinalIgnoreCase) ||
         UsesImplicitPosterCardFill(node)) &&
        !string.IsNullOrWhiteSpace(ExtractCode(node.Value));

    private static bool IsImageCell(TemplateNode node) =>
        string.Equals(GetStyleValue(node.Style, "shape"), "image", StringComparison.OrdinalIgnoreCase);

    private static string ExtractCode(string value)
    {
        var match = ComponentCodeRegex().Match(ExtractPlainText(value));
        return match.Success ? NormalizeCode(match.Groups["code"].Value) : string.Empty;
    }

    private static string NormalizeCode(string? code) =>
        string.IsNullOrWhiteSpace(code)
            ? string.Empty
            : code.Trim().ToUpperInvariant();

    private static string ExtractPlainText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var text = value;
        for (var index = 0; index < 2; index++)
        {
            text = WebUtility.HtmlDecode(text);
        }

        text = Regex.Replace(text, "<br\\s*/?>", "\n", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, "</(div|p|li|font)>", "\n", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, "<[^>]+>", string.Empty, RegexOptions.IgnoreCase);
        text = text.Replace('\u00A0', ' ');

        var lines = text
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(line => Regex.Replace(line, "\\s+", " ").Trim())
            .Where(line => line.Length > 0)
            .ToList();

        return string.Join("\n", lines);
    }

    private static List<string> WrapText(string text, double maxWidth, double fontSize)
    {
        var maxCharacters = Math.Max(8, (int)Math.Floor(maxWidth / Math.Max(6, fontSize * 0.54)));
        var lines = new List<string>();

        foreach (var paragraph in text.Split('\n', StringSplitOptions.TrimEntries))
        {
            if (string.IsNullOrWhiteSpace(paragraph))
            {
                continue;
            }

            var words = paragraph.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var currentLine = new StringBuilder();

            foreach (var word in words)
            {
                if (currentLine.Length == 0)
                {
                    currentLine.Append(word);
                    continue;
                }

                if (currentLine.Length + 1 + word.Length <= maxCharacters)
                {
                    currentLine.Append(' ');
                    currentLine.Append(word);
                    continue;
                }

                lines.Add(currentLine.ToString());
                currentLine.Clear();

                if (word.Length <= maxCharacters)
                {
                    currentLine.Append(word);
                    continue;
                }

                foreach (var chunk in SplitLongWord(word, maxCharacters))
                {
                    if (chunk.Length == maxCharacters)
                    {
                        lines.Add(chunk);
                    }
                    else
                    {
                        currentLine.Append(chunk);
                    }
                }
            }

            if (currentLine.Length > 0)
            {
                lines.Add(currentLine.ToString());
            }
        }

        return lines.Count == 0 ? [text] : lines;
    }

    private static IEnumerable<string> SplitLongWord(string word, int maxCharacters)
    {
        for (var index = 0; index < word.Length; index += maxCharacters)
        {
            var length = Math.Min(maxCharacters, word.Length - index);
            yield return word.Substring(index, length);
        }
    }

    private static Dictionary<string, string> ParseStyle(string? style)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(style))
        {
            return values;
        }

        foreach (var segment in style.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var equalsIndex = segment.IndexOf('=');
            if (equalsIndex < 0)
            {
                values[segment] = "1";
                continue;
            }

            var key = segment[..equalsIndex];
            var value = segment[(equalsIndex + 1)..];
            values[key] = value;
        }

        return values;
    }

    private static string GetStyleValue(IReadOnlyDictionary<string, string> style, string key) =>
        style.TryGetValue(key, out var value) ? value : string.Empty;

    private static string GetFill(TemplateNode node)
    {
        var fillOpacity = ParseDouble(GetStyleValue(node.Style, "fillOpacity"), 100);
        var fillColor = NormalizeColor(GetStyleValue(node.Style, "fillColor"));
        if (fillOpacity <= 0)
        {
            return "none";
        }

        if (!string.Equals(fillColor, "none", StringComparison.OrdinalIgnoreCase))
        {
            return fillColor;
        }

        return UsesImplicitPosterCardFill(node) ? "#ffffff" : "none";
    }

    private static string GetStroke(IReadOnlyDictionary<string, string> style)
    {
        var strokeOpacity = ParseDouble(GetStyleValue(style, "strokeOpacity"), 100);
        var strokeColor = NormalizeColor(GetStyleValue(style, "strokeColor"));
        return strokeOpacity <= 0 || string.IsNullOrWhiteSpace(strokeColor) ? "none" : strokeColor;
    }

    private static string NormalizeColor(string color)
    {
        if (string.IsNullOrWhiteSpace(color) || string.Equals(color, "none", StringComparison.OrdinalIgnoreCase))
        {
            return "none";
        }

        if (string.Equals(color, "default", StringComparison.OrdinalIgnoreCase))
        {
            return "#1f2933";
        }

        return color;
    }

    private static bool UsesImplicitPosterCardFill(TemplateNode node)
    {
        if (node.Children.Count != 0 ||
            IsImageCell(node) ||
            !string.Equals(GetStyleValue(node.Style, "rounded"), "1", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(ExtractPlainText(node.Value)))
        {
            return false;
        }

        for (var current = node.Parent; current is not null; current = current.Parent)
        {
            if (IsGreyPosterContainer(current))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsGreyPosterContainer(TemplateNode node) =>
        string.Equals(NormalizeColor(GetStyleValue(node.Style, "fillColor")), "#e5e5e5", StringComparison.OrdinalIgnoreCase) ||
        node.Children.Any(child =>
            !child.IsGroup &&
            string.Equals(NormalizeColor(GetStyleValue(child.Style, "fillColor")), "#e5e5e5", StringComparison.OrdinalIgnoreCase) &&
            Math.Abs(child.OriginalX) <= LayoutRowTolerance &&
            Math.Abs(child.OriginalY) <= LayoutRowTolerance &&
            Math.Abs(child.OriginalWidth - node.OriginalWidth) <= 2 &&
            Math.Abs(child.OriginalHeight - node.OriginalHeight) <= 2);

    private static bool HasMappedOverlay(TemplateNode node) =>
        node.OverlayItems.Count > 0 &&
        IsLeafComponentCell(node);

    private static string GetFontColor(IReadOnlyDictionary<string, string> style)
    {
        var color = NormalizeColor(GetStyleValue(style, "fontColor"));
        return string.Equals(color, "none", StringComparison.OrdinalIgnoreCase) ? "#1f2933" : color;
    }

    private static string GetFontWeight(IReadOnlyDictionary<string, string> style)
    {
        var fontStyle = GetStyleValue(style, "fontStyle");
        return fontStyle.Contains('1', StringComparison.OrdinalIgnoreCase) ? "700" : "400";
    }

    private static string GetTextAnchor(IReadOnlyDictionary<string, string> style, out string alignment)
    {
        alignment = GetStyleValue(style, "align").ToLowerInvariant() switch
        {
            "center" => "center",
            "right" => "right",
            _ => "left"
        };

        return alignment switch
        {
            "center" => "middle",
            "right" => "end",
            _ => "start"
        };
    }

    private static double GetCornerRadius(TemplateNode node)
    {
        if (!string.Equals(GetStyleValue(node.Style, "rounded"), "1", StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        return ParseDouble(GetStyleValue(node.Style, "arcSize"), 12);
    }

    private static double ParseDouble(string? value, double fallback = 0) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : fallback;

    private static string FormatNumber(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);

    [GeneratedRegex(@"\((?<code>[A-Za-z]{1,8}\d{1,4})\)", RegexOptions.Compiled)]
    private static partial Regex ComponentCodeRegex();

    private sealed class TemplateNode
    {
        public required string Id { get; init; }
        public string? ParentId { get; init; }
        public TemplateNode? Parent { get; set; }
        public string Value { get; init; } = string.Empty;
        public Dictionary<string, string> Style { get; init; } = new(StringComparer.OrdinalIgnoreCase);
        public bool IsGroup { get; init; }
        public double X { get; set; }
        public double Y { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
        public double OriginalX { get; init; }
        public double OriginalY { get; init; }
        public double OriginalWidth { get; init; }
        public double OriginalHeight { get; init; }
        public double AbsoluteX { get; set; }
        public double AbsoluteY { get; set; }
        public List<TemplateNode> Children { get; } = [];
        public List<OverlayItem> OverlayItems { get; set; } = [];
    }

    private sealed record MappedPosterItem(string Label, string? Href);

    private sealed record OverlayItem(string Label, string? Href, IReadOnlyList<string> Lines, double Height);

    private readonly record struct Bounds(double Left, double Top, double Right, double Bottom)
    {
        public Bounds Expand(double left, double top, double right, double bottom) =>
            new(
                Math.Min(Left, left),
                Math.Min(Top, top),
                Math.Max(Right, right),
                Math.Max(Bottom, bottom));
    }
}
