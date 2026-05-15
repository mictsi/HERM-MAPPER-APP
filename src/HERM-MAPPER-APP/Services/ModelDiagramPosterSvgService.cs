using System.Globalization;
using System.Net;
using System.Text;
using HERMMapperApp.ViewModels;

namespace HERMMapperApp.Services;

public static class ModelDiagramPosterSvgService
{
    private const double FallbackCanvasWidth = 1760.0;
    private const double FallbackOuterPadding = 40.0;
    private const double FallbackSectionGap = 24.0;
    private const double FallbackCapabilityCardWidth = 288.0;
    private const double FallbackCardRadius = 28.0;
    private const double FallbackInnerRadius = 20.0;
    private const double FallbackHeaderPadding = 28.0;
    private const double FallbackDomainPadding = 24.0;
    private const double FallbackCapabilityPadding = 16.0;
    private const double FallbackComponentPadding = 14.0;
    private const double FallbackTitleFontSize = 32.0;
    private const double FallbackDomainTitleFontSize = 26.0;
    private const double FallbackCapabilityFontSize = 18.0;
    private const double FallbackComponentFontSize = 16.0;
    private const double FallbackBodyFontSize = 15.0;
    private const double FallbackMetaFontSize = 13.0;
    private const double FallbackCodeFontSize = 12.0;
    private const double FallbackLineHeightFactor = 1.28;
    private const double FallbackComponentGap = 12.0;
    private const double FallbackCapabilityGap = 14.0;
    private const double FallbackProductGap = 8.0;
    private const double FallbackProductPaddingX = 10.0;
    private const double FallbackProductPaddingY = 6.0;
    private const double FallbackUnmappedCardMinWidth = 220.0;
    private static readonly List<string> FallbackUnmappedLabelLines = ["UNMAPPED"];

    public static string BuildSvg(ModelDiagramReportViewModel model)
    {
        return RenderFallbackSvg(model);
    }

    public static string BuildDownloadFileName(string scopeKey) => $"herm-{scopeKey.Trim().ToLowerInvariant()}-poster.svg";

    private static string RenderFallbackSvg(ModelDiagramReportViewModel model)
    {
        var visibleDomains = GetVisibleDomains(model);
        var contentWidth = FallbackCanvasWidth - (FallbackOuterPadding * 2);
        var headerHeight = EstimateFallbackHeaderHeight(model, contentWidth);
        var contentTop = FallbackOuterPadding + headerHeight + FallbackSectionGap;
        var placements = new List<FallbackDomainPlacement>();
        var currentTop = contentTop;

        foreach (var domain in visibleDomains)
        {
            var domainHeight = EstimateFallbackDomainHeight(domain, model, contentWidth);
            placements.Add(new FallbackDomainPlacement(domain, FallbackOuterPadding, currentTop, contentWidth, domainHeight));
            currentTop += domainHeight + FallbackSectionGap;
        }

        var contentBottom = placements.Count == 0 ? contentTop : currentTop - FallbackSectionGap;
        var emptyStateHeight = 0.0;
        var emptyStateY = contentTop;

        if (placements.Count == 0 && model.UnmappedProducts.Count == 0)
        {
            emptyStateHeight = EstimateFallbackEmptyStateHeight(contentWidth);
            contentBottom = emptyStateY + emptyStateHeight;
        }

        var unmappedHeight = 0.0;
        var unmappedY = contentBottom + FallbackSectionGap;
        if (model.ShowUnmappedItems && model.UnmappedProducts.Count > 0)
        {
            unmappedHeight = EstimateFallbackUnmappedSectionHeight(model, contentWidth);
            contentBottom = unmappedY + unmappedHeight;
        }

        var canvasHeight = Math.Max(contentBottom + FallbackOuterPadding, FallbackOuterPadding + headerHeight + 240);
        var svg = new StringBuilder();

        svg.Append("<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 ");
        svg.Append(FormatNumber(FallbackCanvasWidth));
        svg.Append(' ');
        svg.Append(FormatNumber(canvasHeight));
        svg.Append("\" width=\"");
        svg.Append(FormatNumber(FallbackCanvasWidth));
        svg.Append("\" height=\"");
        svg.Append(FormatNumber(canvasHeight));
        svg.Append("\" preserveAspectRatio=\"xMidYMin meet\" class=\"diagram-poster-svg\" role=\"img\">");
        svg.Append("<title>");
        svg.Append(WebUtility.HtmlEncode(model.PosterTitle));
        svg.Append("</title><desc>");
        svg.Append(WebUtility.HtmlEncode(BuildPosterSummary(model)));
        svg.Append("</desc>");
        svg.Append("<rect x=\"0\" y=\"0\" width=\"");
        svg.Append(FormatNumber(FallbackCanvasWidth));
        svg.Append("\" height=\"");
        svg.Append(FormatNumber(canvasHeight));
        svg.Append("\" fill=\"#f3efe6\" />");

        RenderFallbackHeader(svg, model, FallbackOuterPadding, FallbackOuterPadding, contentWidth, headerHeight);

        foreach (var placement in placements)
        {
            RenderFallbackDomain(svg, placement.Domain, model, placement.X, placement.Y, placement.Width, placement.Height);
        }

        if (emptyStateHeight > 0)
        {
            RenderFallbackEmptyState(svg, model, FallbackOuterPadding, emptyStateY, contentWidth, emptyStateHeight);
        }

        if (unmappedHeight > 0)
        {
            RenderFallbackUnmappedSection(svg, model, FallbackOuterPadding, unmappedY, contentWidth, unmappedHeight);
        }

        svg.Append("</svg>");
        return svg.ToString();
    }

    private static void RenderFallbackHeader(StringBuilder svg, ModelDiagramReportViewModel model, double x, double y, double width, double height)
    {
        var innerWidth = width - (FallbackHeaderPadding * 2);
        var titleLines = WrapText(model.PosterTitle, innerWidth, FallbackTitleFontSize);
        var descriptionText = string.IsNullOrWhiteSpace(model.PosterDescription)
            ? model.DiagramDescription
            : model.PosterDescription;
        var descriptionLines = string.IsNullOrWhiteSpace(descriptionText)
            ? []
            : WrapText(descriptionText, innerWidth, FallbackBodyFontSize);
        var summaryLines = WrapText(BuildPosterSummary(model), innerWidth, FallbackBodyFontSize);

        svg.Append("<rect x=\"");
        svg.Append(FormatNumber(x));
        svg.Append("\" y=\"");
        svg.Append(FormatNumber(y));
        svg.Append("\" width=\"");
        svg.Append(FormatNumber(width));
        svg.Append("\" height=\"");
        svg.Append(FormatNumber(height));
        svg.Append("\" rx=\"");
        svg.Append(FormatNumber(FallbackCardRadius));
        svg.Append("\" ry=\"");
        svg.Append(FormatNumber(FallbackCardRadius));
        svg.Append("\" fill=\"#fffdf8\" stroke=\"#d8d1c2\" stroke-width=\"1.5\" />");

        var cursorTop = y + FallbackHeaderPadding;
        cursorTop += RenderFallbackTextBlockAtTop(svg, titleLines, x + FallbackHeaderPadding, cursorTop, FallbackTitleFontSize, "#2d3a52", "700");

        if (descriptionLines.Count > 0)
        {
            cursorTop += 10;
            cursorTop += RenderFallbackTextBlockAtTop(svg, descriptionLines, x + FallbackHeaderPadding, cursorTop, FallbackBodyFontSize, "#52606d");
        }

        if (summaryLines.Count > 0)
        {
            cursorTop += 10;
            RenderFallbackTextBlockAtTop(svg, summaryLines, x + FallbackHeaderPadding, cursorTop, FallbackBodyFontSize, "#6b7280", "600");
        }
    }

    private static void RenderFallbackDomain(StringBuilder svg, ModelDiagramDomainViewModel domain, ModelDiagramReportViewModel model, double x, double y, double width, double height)
    {
        var innerWidth = width - (FallbackDomainPadding * 2);
        var visibleCapabilities = GetVisibleCapabilities(model, domain);
        var domainTitle = string.IsNullOrWhiteSpace(domain.Name) ? domain.DisplayLabel : domain.Name;
        var titleLines = WrapText(domainTitle, innerWidth, FallbackDomainTitleFontSize);
        var summaryLines = WrapText(BuildDomainSummary(domain, model), innerWidth, FallbackMetaFontSize);

        svg.Append("<rect x=\"");
        svg.Append(FormatNumber(x));
        svg.Append("\" y=\"");
        svg.Append(FormatNumber(y));
        svg.Append("\" width=\"");
        svg.Append(FormatNumber(width));
        svg.Append("\" height=\"");
        svg.Append(FormatNumber(height));
        svg.Append("\" rx=\"");
        svg.Append(FormatNumber(FallbackCardRadius));
        svg.Append("\" ry=\"");
        svg.Append(FormatNumber(FallbackCardRadius));
        svg.Append("\" fill=\"#b9b9b9\" stroke=\"#9ca3af\" stroke-width=\"1.5\" />");

        var cursorTop = y + FallbackDomainPadding;
        if (!string.IsNullOrWhiteSpace(domain.Code))
        {
            var codeLines = WrapText(domain.Code, innerWidth, FallbackCodeFontSize);
            cursorTop += RenderFallbackTextBlockAtTop(svg, codeLines, x + FallbackDomainPadding, cursorTop, FallbackCodeFontSize, "#7b6f5a", "700");
            cursorTop += 8;
        }

        cursorTop += RenderFallbackTextBlockAtTop(svg, titleLines, x + FallbackDomainPadding, cursorTop, FallbackDomainTitleFontSize, "#2d3a52", "700");

        if (summaryLines.Count > 0)
        {
            cursorTop += 10;
            cursorTop += RenderFallbackTextBlockAtTop(svg, summaryLines, x + FallbackDomainPadding, cursorTop, FallbackMetaFontSize, "#6b7280", "600");
        }

        cursorTop += 16;

        if (visibleCapabilities.Count == 0)
        {
            if (model.ShowBranchEmptyStates)
            {
                var emptyHeight = EstimateFallbackMessageCardHeight(innerWidth, "No capabilities are available in this domain yet.");
                RenderFallbackMessageCard(svg, x + FallbackDomainPadding, cursorTop, innerWidth, emptyHeight, "No capabilities are available in this domain yet.");
            }

            return;
        }

        var capabilityPlacements = BuildFallbackCapabilityPlacements(
            visibleCapabilities,
            model,
            x + FallbackDomainPadding,
            cursorTop,
            innerWidth);

        foreach (var capability in capabilityPlacements)
        {
            RenderFallbackCapability(svg, capability.Capability, model, capability.X, capability.Y, capability.Width, capability.Height);
        }
    }

    private static void RenderFallbackCapability(StringBuilder svg, ModelDiagramCapabilityViewModel capability, ModelDiagramReportViewModel model, double x, double y, double width, double height)
    {
        var innerWidth = width - (FallbackCapabilityPadding * 2);
        var visibleComponents = GetVisibleComponents(model, capability);
        var titleLines = WrapText(capability.DisplayLabel, innerWidth, FallbackCapabilityFontSize);
        var summaryLines = WrapText(BuildCapabilitySummary(capability, model), innerWidth, FallbackMetaFontSize);

        svg.Append("<rect x=\"");
        svg.Append(FormatNumber(x));
        svg.Append("\" y=\"");
        svg.Append(FormatNumber(y));
        svg.Append("\" width=\"");
        svg.Append(FormatNumber(width));
        svg.Append("\" height=\"");
        svg.Append(FormatNumber(height));
        svg.Append("\" rx=\"");
        svg.Append(FormatNumber(FallbackInnerRadius));
        svg.Append("\" ry=\"");
        svg.Append(FormatNumber(FallbackInnerRadius));
        svg.Append("\" fill=\"#e5e5e5\" stroke=\"#cfd4da\" stroke-width=\"1.2\" />");

        var cursorTop = y + FallbackCapabilityPadding;
        cursorTop += RenderFallbackTextBlockAtTop(svg, titleLines, x + FallbackCapabilityPadding, cursorTop, FallbackCapabilityFontSize, "#243b53", "700");

        if (summaryLines.Count > 0)
        {
            cursorTop += 8;
            cursorTop += RenderFallbackTextBlockAtTop(svg, summaryLines, x + FallbackCapabilityPadding, cursorTop, FallbackMetaFontSize, "#6b7280", "600");
        }

        cursorTop += 14;

        if (visibleComponents.Count == 0)
        {
            if (model.ShowBranchEmptyStates)
            {
                var emptyHeight = EstimateFallbackMessageCardHeight(innerWidth, "No components are linked to this capability yet.");
                RenderFallbackMessageCard(svg, x + FallbackCapabilityPadding, cursorTop, innerWidth, emptyHeight, "No components are linked to this capability yet.");
            }

            return;
        }

        foreach (var component in visibleComponents)
        {
            var componentHeight = EstimateFallbackComponentHeight(component, model, innerWidth);
            RenderFallbackComponent(svg, component, model, x + FallbackCapabilityPadding, cursorTop, innerWidth, componentHeight);
            cursorTop += componentHeight + FallbackComponentGap;
        }
    }

    private static void RenderFallbackComponent(StringBuilder svg, ModelDiagramComponentViewModel component, ModelDiagramReportViewModel model, double x, double y, double width, double height)
    {
        var innerWidth = width - (FallbackComponentPadding * 2);
        var labelLines = WrapText(component.DisplayLabel, innerWidth, FallbackComponentFontSize);
        var summaryLines = model.ShowComponentMappedSummary && component.ProductCount > 0
            ? WrapText(model.FormatMappedSummary(component.ProductCount), innerWidth, FallbackMetaFontSize)
            : [];
        var hasMappedProducts = component.ProductCount > 0;

        svg.Append("<rect x=\"");
        svg.Append(FormatNumber(x));
        svg.Append("\" y=\"");
        svg.Append(FormatNumber(y));
        svg.Append("\" width=\"");
        svg.Append(FormatNumber(width));
        svg.Append("\" height=\"");
        svg.Append(FormatNumber(height));
        svg.Append("\" rx=\"18\" ry=\"18\" fill=\"");
        svg.Append(hasMappedProducts ? "#fff7f8" : "#ffffff");
        svg.Append("\" stroke=\"");
        svg.Append(hasMappedProducts ? "#c92d39" : "#d8d1c2");
        svg.Append("\" stroke-width=\"");
        svg.Append(hasMappedProducts ? "3" : "1.2");
        svg.Append("\" />");

        var cursorTop = y + FallbackComponentPadding;
        cursorTop += RenderFallbackTextBlockAtTop(svg, labelLines, x + FallbackComponentPadding, cursorTop, FallbackComponentFontSize, "#102a43", "700");

        if (summaryLines.Count > 0)
        {
            cursorTop += 6;
            cursorTop += RenderFallbackTextBlockAtTop(svg, summaryLines, x + FallbackComponentPadding, cursorTop, FallbackMetaFontSize, "#7c2d12", "600");
        }

        if (component.Products.Count == 0)
        {
            return;
        }

        cursorTop += 10;
        foreach (var product in component.Products)
        {
            var productHeight = EstimateFallbackProductCardHeight(product.Name, innerWidth);
            var href = BuildProductHref(product);
            AppendOpenLink(svg, href);

            svg.Append("<rect x=\"");
            svg.Append(FormatNumber(x + FallbackComponentPadding));
            svg.Append("\" y=\"");
            svg.Append(FormatNumber(cursorTop));
            svg.Append("\" width=\"");
            svg.Append(FormatNumber(innerWidth));
            svg.Append("\" height=\"");
            svg.Append(FormatNumber(productHeight));
            svg.Append("\" rx=\"14\" ry=\"14\" fill=\"#f3f7fa\" stroke=\"#d6dce3\" stroke-width=\"1\" />");

            var productLines = WrapText(product.Name, innerWidth - (FallbackProductPaddingX * 2), FallbackBodyFontSize);
            RenderFallbackTextBlockAtTop(svg, productLines, x + FallbackComponentPadding + FallbackProductPaddingX, cursorTop + FallbackProductPaddingY, FallbackBodyFontSize, "#243b53", "600");

            AppendCloseLink(svg, href);
            cursorTop += productHeight + FallbackProductGap;
        }
    }

    private static void RenderFallbackUnmappedSection(StringBuilder svg, ModelDiagramReportViewModel model, double x, double y, double width, double height)
    {
        var innerWidth = width - (FallbackDomainPadding * 2);
        var titleLines = WrapText(model.UnmappedSectionTitle, innerWidth, FallbackDomainTitleFontSize);
        var summaryLines = WrapText($"{model.UnmappedProducts.Count} {model.UnmappedSummaryLabel}", innerWidth, FallbackMetaFontSize);
        var columns = GetFallbackUnmappedColumnCount(innerWidth);
        var cardWidth = (innerWidth - (FallbackProductGap * (columns - 1))) / columns;

        svg.Append("<rect x=\"");
        svg.Append(FormatNumber(x));
        svg.Append("\" y=\"");
        svg.Append(FormatNumber(y));
        svg.Append("\" width=\"");
        svg.Append(FormatNumber(width));
        svg.Append("\" height=\"");
        svg.Append(FormatNumber(height));
        svg.Append("\" rx=\"");
        svg.Append(FormatNumber(FallbackCardRadius));
        svg.Append("\" ry=\"");
        svg.Append(FormatNumber(FallbackCardRadius));
        svg.Append("\" fill=\"#fffdf8\" stroke=\"#d8d1c2\" stroke-width=\"1.5\" />");

        var cursorTop = y + FallbackDomainPadding;
        RenderFallbackTextBlockAtTop(svg, FallbackUnmappedLabelLines, x + FallbackDomainPadding, cursorTop, FallbackCodeFontSize, "#7b6f5a", "700");
        cursorTop += GetTextBlockHeight(FallbackUnmappedLabelLines, FallbackCodeFontSize) + 8;
        cursorTop += RenderFallbackTextBlockAtTop(svg, titleLines, x + FallbackDomainPadding, cursorTop, FallbackDomainTitleFontSize, "#2d3a52", "700");
        cursorTop += 10;
        cursorTop += RenderFallbackTextBlockAtTop(svg, summaryLines, x + FallbackDomainPadding, cursorTop, FallbackMetaFontSize, "#6b7280", "600");
        cursorTop += 16;

        var rowHeights = new List<double>();
        var currentRowHeight = 0.0;
        for (var index = 0; index < model.UnmappedProducts.Count; index++)
        {
            var cardHeight = EstimateFallbackProductCardHeight(model.UnmappedProducts[index].Name, cardWidth - (FallbackProductPaddingX * 2));
            currentRowHeight = Math.Max(currentRowHeight, cardHeight);
            if ((index + 1) % columns == 0)
            {
                rowHeights.Add(currentRowHeight);
                currentRowHeight = 0.0;
            }
        }

        if (currentRowHeight > 0)
        {
            rowHeights.Add(currentRowHeight);
        }

        var currentTop = cursorTop;
        var itemIndex = 0;
        foreach (var rowHeight in rowHeights)
        {
            for (var column = 0; column < columns && itemIndex < model.UnmappedProducts.Count; column++, itemIndex++)
            {
                var product = model.UnmappedProducts[itemIndex];
                var cardX = x + FallbackDomainPadding + (column * (cardWidth + FallbackProductGap));
                var href = BuildProductHref(product);
                AppendOpenLink(svg, href);

                svg.Append("<rect x=\"");
                svg.Append(FormatNumber(cardX));
                svg.Append("\" y=\"");
                svg.Append(FormatNumber(currentTop));
                svg.Append("\" width=\"");
                svg.Append(FormatNumber(cardWidth));
                svg.Append("\" height=\"");
                svg.Append(FormatNumber(rowHeight));
                svg.Append("\" rx=\"14\" ry=\"14\" fill=\"#f5f1e6\" stroke=\"#d6d0c3\" stroke-width=\"1\" />");

                var productLines = WrapText(product.Name, cardWidth - (FallbackProductPaddingX * 2), FallbackBodyFontSize);
                RenderFallbackTextBlockAtTop(svg, productLines, cardX + FallbackProductPaddingX, currentTop + FallbackProductPaddingY, FallbackBodyFontSize, "#243b53", "600");

                AppendCloseLink(svg, href);
            }

            currentTop += rowHeight + FallbackProductGap;
        }
    }

    private static void RenderFallbackEmptyState(StringBuilder svg, ModelDiagramReportViewModel model, double x, double y, double width, double height)
    {
        var cardWidth = Math.Min(780, width);
        var cardX = x + ((width - cardWidth) / 2);
        var titleLines = WrapText(model.EmptyStateTitle, cardWidth - (FallbackHeaderPadding * 2), FallbackDomainTitleFontSize);
        var bodyLines = WrapText(model.EmptyStateBody, cardWidth - (FallbackHeaderPadding * 2), FallbackBodyFontSize);

        svg.Append("<rect x=\"");
        svg.Append(FormatNumber(cardX));
        svg.Append("\" y=\"");
        svg.Append(FormatNumber(y));
        svg.Append("\" width=\"");
        svg.Append(FormatNumber(cardWidth));
        svg.Append("\" height=\"");
        svg.Append(FormatNumber(height));
        svg.Append("\" rx=\"");
        svg.Append(FormatNumber(FallbackCardRadius));
        svg.Append("\" ry=\"");
        svg.Append(FormatNumber(FallbackCardRadius));
        svg.Append("\" fill=\"#fffdf8\" stroke=\"#d8d1c2\" stroke-width=\"1.5\" />");

        var cursorTop = y + FallbackHeaderPadding;
        cursorTop += RenderFallbackTextBlockAtTop(svg, titleLines, cardX + FallbackHeaderPadding, cursorTop, FallbackDomainTitleFontSize, "#2d3a52", "700");
        cursorTop += 10;
        RenderFallbackTextBlockAtTop(svg, bodyLines, cardX + FallbackHeaderPadding, cursorTop, FallbackBodyFontSize, "#52606d");
    }

    private static void RenderFallbackMessageCard(StringBuilder svg, double x, double y, double width, double height, string message)
    {
        var lines = WrapText(message, width - (FallbackCapabilityPadding * 2), FallbackMetaFontSize);

        svg.Append("<rect x=\"");
        svg.Append(FormatNumber(x));
        svg.Append("\" y=\"");
        svg.Append(FormatNumber(y));
        svg.Append("\" width=\"");
        svg.Append(FormatNumber(width));
        svg.Append("\" height=\"");
        svg.Append(FormatNumber(height));
        svg.Append("\" rx=\"16\" ry=\"16\" fill=\"#ffffff\" stroke=\"#ddd6c5\" stroke-width=\"1\" />");

        RenderFallbackTextBlockAtTop(svg, lines, x + FallbackCapabilityPadding, y + FallbackCapabilityPadding, FallbackMetaFontSize, "#6b7280");
    }

    private static double EstimateFallbackHeaderHeight(ModelDiagramReportViewModel model, double width)
    {
        var innerWidth = width - (FallbackHeaderPadding * 2);
        var titleHeight = GetTextBlockHeight(WrapText(model.PosterTitle, innerWidth, FallbackTitleFontSize), FallbackTitleFontSize);
        var descriptionText = string.IsNullOrWhiteSpace(model.PosterDescription)
            ? model.DiagramDescription
            : model.PosterDescription;
        var descriptionHeight = string.IsNullOrWhiteSpace(descriptionText)
            ? 0
            : GetTextBlockHeight(WrapText(descriptionText, innerWidth, FallbackBodyFontSize), FallbackBodyFontSize);
        var summaryHeight = GetTextBlockHeight(WrapText(BuildPosterSummary(model), innerWidth, FallbackBodyFontSize), FallbackBodyFontSize);
        var height = FallbackHeaderPadding + titleHeight + FallbackHeaderPadding;

        if (descriptionHeight > 0)
        {
            height += 10 + descriptionHeight;
        }

        if (summaryHeight > 0)
        {
            height += 10 + summaryHeight;
        }

        return height;
    }

    private static double EstimateFallbackDomainHeight(ModelDiagramDomainViewModel domain, ModelDiagramReportViewModel model, double width)
    {
        var innerWidth = width - (FallbackDomainPadding * 2);
        var visibleCapabilities = GetVisibleCapabilities(model, domain);
        var height = FallbackDomainPadding;

        if (!string.IsNullOrWhiteSpace(domain.Code))
        {
            height += GetTextBlockHeight(WrapText(domain.Code, innerWidth, FallbackCodeFontSize), FallbackCodeFontSize) + 8;
        }

        var domainTitle = string.IsNullOrWhiteSpace(domain.Name) ? domain.DisplayLabel : domain.Name;
        height += GetTextBlockHeight(WrapText(domainTitle, innerWidth, FallbackDomainTitleFontSize), FallbackDomainTitleFontSize);
        height += 10 + GetTextBlockHeight(WrapText(BuildDomainSummary(domain, model), innerWidth, FallbackMetaFontSize), FallbackMetaFontSize);
        height += 16;

        if (visibleCapabilities.Count == 0)
        {
            if (model.ShowBranchEmptyStates)
            {
                height += EstimateFallbackMessageCardHeight(innerWidth, "No capabilities are available in this domain yet.");
            }

            return height + FallbackDomainPadding;
        }

        var capabilityPlacements = BuildFallbackCapabilityPlacements(visibleCapabilities, model, 0, height, innerWidth);
        var capabilityBottom = capabilityPlacements.Count == 0
            ? height
            : capabilityPlacements.Max(capability => capability.Y + capability.Height);

        return capabilityBottom + FallbackDomainPadding;
    }

    private static List<FallbackCapabilityPlacement> BuildFallbackCapabilityPlacements(
        IReadOnlyList<ModelDiagramCapabilityViewModel> capabilities,
        ModelDiagramReportViewModel model,
        double x,
        double y,
        double width)
    {
        var columns = GetFallbackCapabilityColumnCount(width);
        var cardWidth = (width - (FallbackCapabilityGap * (columns - 1))) / columns;
        var placements = new List<FallbackCapabilityPlacement>(capabilities.Count);
        var currentY = y;

        for (var index = 0; index < capabilities.Count; index += columns)
        {
            var rowCapabilities = capabilities.Skip(index).Take(columns).ToList();
            var rowHeights = rowCapabilities
                .Select(capability => EstimateFallbackCapabilityHeight(capability, model, cardWidth))
                .ToList();
            var rowHeight = rowHeights.Count == 0 ? 0 : rowHeights.Max();

            for (var column = 0; column < rowCapabilities.Count; column++)
            {
                placements.Add(new FallbackCapabilityPlacement(
                    rowCapabilities[column],
                    x + (column * (cardWidth + FallbackCapabilityGap)),
                    currentY,
                    cardWidth,
                    rowHeights[column]));
            }

            currentY += rowHeight + FallbackCapabilityGap;
        }

        return placements;
    }

    private static double EstimateFallbackCapabilityHeight(ModelDiagramCapabilityViewModel capability, ModelDiagramReportViewModel model, double width)
    {
        var innerWidth = width - (FallbackCapabilityPadding * 2);
        var visibleComponents = GetVisibleComponents(model, capability);
        var height = FallbackCapabilityPadding;
        height += GetTextBlockHeight(WrapText(capability.DisplayLabel, innerWidth, FallbackCapabilityFontSize), FallbackCapabilityFontSize);
        height += 8 + GetTextBlockHeight(WrapText(BuildCapabilitySummary(capability, model), innerWidth, FallbackMetaFontSize), FallbackMetaFontSize);
        height += 14;

        if (visibleComponents.Count == 0)
        {
            if (model.ShowBranchEmptyStates)
            {
                height += EstimateFallbackMessageCardHeight(innerWidth, "No components are linked to this capability yet.");
            }

            return height + FallbackCapabilityPadding;
        }

        foreach (var component in visibleComponents)
        {
            height += EstimateFallbackComponentHeight(component, model, innerWidth) + FallbackComponentGap;
        }

        return height - FallbackComponentGap + FallbackCapabilityPadding;
    }

    private static double EstimateFallbackComponentHeight(ModelDiagramComponentViewModel component, ModelDiagramReportViewModel model, double width)
    {
        var innerWidth = width - (FallbackComponentPadding * 2);
        var height = FallbackComponentPadding;
        height += GetTextBlockHeight(WrapText(component.DisplayLabel, innerWidth, FallbackComponentFontSize), FallbackComponentFontSize);

        if (model.ShowComponentMappedSummary && component.ProductCount > 0)
        {
            height += 6 + GetTextBlockHeight(WrapText(model.FormatMappedSummary(component.ProductCount), innerWidth, FallbackMetaFontSize), FallbackMetaFontSize);
        }

        if (component.Products.Count > 0)
        {
            height += 10;
            foreach (var product in component.Products)
            {
                height += EstimateFallbackProductCardHeight(product.Name, innerWidth) + FallbackProductGap;
            }

            height -= FallbackProductGap;
        }

        return height + FallbackComponentPadding;
    }

    private static double EstimateFallbackUnmappedSectionHeight(ModelDiagramReportViewModel model, double width)
    {
        var innerWidth = width - (FallbackDomainPadding * 2);
        var columns = GetFallbackUnmappedColumnCount(innerWidth);
        var cardWidth = (innerWidth - (FallbackProductGap * (columns - 1))) / columns;
        var height = FallbackDomainPadding;
        height += GetTextBlockHeight(FallbackUnmappedLabelLines, FallbackCodeFontSize) + 8;
        height += GetTextBlockHeight(WrapText(model.UnmappedSectionTitle, innerWidth, FallbackDomainTitleFontSize), FallbackDomainTitleFontSize);
        height += 10 + GetTextBlockHeight(WrapText($"{model.UnmappedProducts.Count} {model.UnmappedSummaryLabel}", innerWidth, FallbackMetaFontSize), FallbackMetaFontSize);
        height += 16;

        var rowHeight = 0.0;
        for (var index = 0; index < model.UnmappedProducts.Count; index++)
        {
            rowHeight = Math.Max(rowHeight, EstimateFallbackProductCardHeight(model.UnmappedProducts[index].Name, cardWidth - (FallbackProductPaddingX * 2)));
            if ((index + 1) % columns == 0)
            {
                height += rowHeight + FallbackProductGap;
                rowHeight = 0.0;
            }
        }

        if (rowHeight > 0)
        {
            height += rowHeight + FallbackProductGap;
        }

        return height - FallbackProductGap + FallbackDomainPadding;
    }

    private static double EstimateFallbackEmptyStateHeight(double width)
    {
        var cardWidth = Math.Min(780, width);
        var innerWidth = cardWidth - (FallbackHeaderPadding * 2);
        var titleHeight = GetTextBlockHeight(WrapText("No model content available", innerWidth, FallbackDomainTitleFontSize), FallbackDomainTitleFontSize);
        var bodyHeight = GetTextBlockHeight(WrapText("Import the HERM reference model and product mappings to populate this report.", innerWidth, FallbackBodyFontSize), FallbackBodyFontSize);
        return (FallbackHeaderPadding * 2) + titleHeight + 10 + bodyHeight;
    }

    private static double EstimateFallbackMessageCardHeight(double width, string message)
    {
        var innerWidth = width - (FallbackCapabilityPadding * 2);
        return (FallbackCapabilityPadding * 2) + GetTextBlockHeight(WrapText(message, innerWidth, FallbackMetaFontSize), FallbackMetaFontSize);
    }

    private static double EstimateFallbackProductCardHeight(string label, double width)
    {
        var productLines = WrapText(label, width - (FallbackProductPaddingX * 2), FallbackBodyFontSize);
        return (FallbackProductPaddingY * 2) + GetTextBlockHeight(productLines, FallbackBodyFontSize);
    }

    private static double RenderFallbackTextBlockAtTop(StringBuilder svg, List<string> lines, double x, double top, double fontSize, string fill, string fontWeight = "400", string anchor = "start")
    {
        if (lines.Count == 0)
        {
            return 0;
        }

        var baseline = top + fontSize;
        var lineHeight = fontSize * FallbackLineHeightFactor;

        svg.Append("<text x=\"");
        svg.Append(FormatNumber(x));
        svg.Append("\" y=\"");
        svg.Append(FormatNumber(baseline));
        svg.Append("\" fill=\"");
        svg.Append(fill);
        svg.Append("\" font-family=\"Open Sans, Segoe UI, Arial, sans-serif\" font-size=\"");
        svg.Append(FormatNumber(fontSize));
        svg.Append("\" font-weight=\"");
        svg.Append(fontWeight);
        svg.Append("\" text-anchor=\"");
        svg.Append(anchor);
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
        return GetTextBlockHeight(lines, fontSize);
    }

    private static double GetTextBlockHeight(List<string> lines, double fontSize) =>
        lines.Count == 0 ? 0 : fontSize + ((lines.Count - 1) * (fontSize * FallbackLineHeightFactor));

    private static IReadOnlyList<ModelDiagramDomainViewModel> GetVisibleDomains(ModelDiagramReportViewModel model) =>
        model.OnlyShowMappedNodes
            ? model.Domains.Where(domain => domain.ProductCount > 0).ToList()
            : model.Domains;

    private static IReadOnlyList<ModelDiagramCapabilityViewModel> GetVisibleCapabilities(ModelDiagramReportViewModel model, ModelDiagramDomainViewModel domain) =>
        model.OnlyShowMappedNodes
            ? domain.Capabilities.Where(capability => capability.ProductCount > 0).ToList()
            : domain.Capabilities;

    private static IReadOnlyList<ModelDiagramComponentViewModel> GetVisibleComponents(ModelDiagramReportViewModel model, ModelDiagramCapabilityViewModel capability) =>
        model.OnlyShowMappedNodes
            ? capability.Components.Where(component => component.ProductCount > 0).ToList()
            : capability.Components;

    private static int GetFallbackCapabilityColumnCount(double innerWidth)
    {
        var columns = (int)Math.Floor((innerWidth + FallbackCapabilityGap) / (FallbackCapabilityCardWidth + FallbackCapabilityGap));
        return Math.Max(1, columns);
    }

    private static int GetFallbackUnmappedColumnCount(double innerWidth)
    {
        var columns = (int)Math.Floor((innerWidth + FallbackProductGap) / (FallbackUnmappedCardMinWidth + FallbackProductGap));
        return Math.Max(1, Math.Min(4, columns));
    }

    private static string BuildPosterSummary(ModelDiagramReportViewModel model)
    {
        var summaryParts = new List<string>
        {
            $"{model.DomainCount} domain(s)",
            $"{model.CapabilityCount} capability(s)",
            $"{model.ComponentCount} component(s)"
        };

        if (model.MappedItemCount > 0)
        {
            summaryParts.Add(model.FormatMappedSummary(model.MappedItemCount));
        }

        if (model.ShowUnmappedItems && model.UnmappedProducts.Count > 0)
        {
            summaryParts.Add($"{model.UnmappedProducts.Count} unmapped");
        }

        return string.Join(" • ", summaryParts);
    }

    private static string BuildDomainSummary(ModelDiagramDomainViewModel domain, ModelDiagramReportViewModel model)
    {
        var summaryParts = new List<string>
        {
            $"{domain.CapabilityCount} capability(s)",
            $"{domain.ComponentCount} component(s)"
        };

        if (domain.ProductCount > 0)
        {
            summaryParts.Add(model.FormatMappedSummary(domain.ProductCount));
        }

        return string.Join(", ", summaryParts);
    }

    private static string BuildCapabilitySummary(ModelDiagramCapabilityViewModel capability, ModelDiagramReportViewModel model)
    {
        var summaryParts = new List<string>
        {
            $"{capability.ComponentCount} component(s)"
        };

        if (capability.ProductCount > 0)
        {
            summaryParts.Add(model.FormatMappedSummary(capability.ProductCount));
        }

        return string.Join(", ", summaryParts);
    }

    private static string? BuildProductHref(ModelDiagramProductViewModel product) =>
        product.HasLink && product.LinkId.HasValue
            ? $"/{product.LinkController}/{product.LinkAction}/{product.LinkId.Value}"
            : null;

    private static void AppendOpenLink(StringBuilder svg, string? href)
    {
        if (string.IsNullOrWhiteSpace(href))
        {
            return;
        }

        svg.Append("<a href=\"");
        svg.Append(WebUtility.HtmlEncode(href));
        svg.Append("\">");
    }

    private static void AppendCloseLink(StringBuilder svg, string? href)
    {
        if (!string.IsNullOrWhiteSpace(href))
        {
            svg.Append("</a>");
        }
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

    private static string FormatNumber(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);

    private sealed record FallbackDomainPlacement(ModelDiagramDomainViewModel Domain, double X, double Y, double Width, double Height);

    private sealed record FallbackCapabilityPlacement(ModelDiagramCapabilityViewModel Capability, double X, double Y, double Width, double Height);
}
