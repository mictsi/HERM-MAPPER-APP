using System.Globalization;
using System.Net;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using HERMMapperApp.ViewModels;

namespace HERMMapperApp.Services;

public static class ModelDiagramExportService
{
    private const double DomainWidth = 360;
    private const double DomainGap = 32;
    private const double RowGap = 18;
    private const double CapabilityPadding = 14;
    private const double ComponentPadding = 10;
    private const double HeaderHeight = 44;
    private const double ComponentHeight = 58;
    private const double ProductHeight = 34;
    private const double ProductGap = 6;
    private const int Columns = 3;

    public static byte[] BuildDrawIo(ModelDiagramReportViewModel model)
    {
        var layout = BuildLayout(model);
        var root = new XElement("root",
            new XElement("mxCell", new XAttribute("id", "0")),
            new XElement("mxCell", new XAttribute("id", "1"), new XAttribute("parent", "0")));

        foreach (var domain in layout.Domains)
        {
            AddVertex(root, $"domain-{domain.Domain.DomainId}", "1", domain.X, domain.Y, domain.Width, domain.Height, "rounded=1;whiteSpace=wrap;html=1;fillColor=#f4efe3;strokeColor=#d5cdbd;fontStyle=1;fontSize=18;", WebUtility.HtmlEncode(domain.Domain.DisplayLabel));

            foreach (var capability in domain.Capabilities)
            {
                AddVertex(root, $"capability-{capability.Capability.CapabilityId}", "1", capability.X, capability.Y, capability.Width, capability.Height, "rounded=1;whiteSpace=wrap;html=1;fillColor=#ffffff;strokeColor=#c7d2de;fontStyle=1;fontSize=14;", WebUtility.HtmlEncode(capability.Capability.DisplayLabel));

                foreach (var component in capability.Components)
                {
                    var hasProducts = component.Component.Products.Count > 0;
                    AddVertex(root, $"component-{component.Component.ComponentId}", "1", component.X, component.Y, component.Width, component.Height, hasProducts ? "rounded=1;whiteSpace=wrap;html=1;fillColor=#fff7f7;strokeColor=#c92d39;strokeWidth=2;fontSize=12;" : "rounded=1;whiteSpace=wrap;html=1;fillColor=#f8fbff;strokeColor=#dce6f1;fontSize=12;", WebUtility.HtmlEncode(component.Component.DisplayLabel));

                    foreach (var product in component.Products)
                    {
                        AddVertex(root, $"item-{product.Product.ProductId}-{component.Component.ComponentId}", "1", product.X, product.Y, product.Width, product.Height, "rounded=1;whiteSpace=wrap;html=1;fillColor=#fef2f2;strokeColor=#c92d39;fontSize=11;", WebUtility.HtmlEncode(product.Product.Name));
                    }
                }
            }
        }

        var mxFile = new XElement("mxfile",
            new XAttribute("host", "app.diagrams.net"),
            new XAttribute("agent", "HERM Mapper"),
            new XAttribute("version", "29.6.3"),
            new XElement("diagram",
                new XAttribute("name", model.PosterTitle),
                new XAttribute("id", BuildIdentifier("diagram", model.ScopeKey)),
                new XElement("mxGraphModel",
                    new XAttribute("dx", FormatNumber(layout.CanvasWidth + 700)),
                    new XAttribute("dy", FormatNumber(layout.CanvasHeight + 700)),
                    new XAttribute("grid", "1"),
                    new XAttribute("gridSize", "10"),
                    new XAttribute("guides", "1"),
                    new XAttribute("tooltips", "1"),
                    new XAttribute("connect", "1"),
                    new XAttribute("arrows", "1"),
                    new XAttribute("fold", "1"),
                    new XAttribute("page", "1"),
                    new XAttribute("pageScale", "1"),
                    new XAttribute("pageWidth", FormatNumber(layout.CanvasWidth + 80)),
                    new XAttribute("pageHeight", FormatNumber(layout.CanvasHeight + 80)),
                    new XAttribute("math", "0"),
                    new XAttribute("shadow", "0"),
                    root)));

        return SerializeXml(new XDocument(mxFile), includeDeclaration: false);
    }

    public static byte[] BuildArchiXml(ModelDiagramReportViewModel model)
    {
        XNamespace archimate = "http://www.opengroup.org/xsd/archimate/3.0/";
        XNamespace xsi = "http://www.w3.org/2001/XMLSchema-instance";
        XNamespace xml = XNamespace.Xml;

        var archiModel = new XElement(archimate + "model",
            new XAttribute(XNamespace.Xmlns + "xsi", xsi),
            new XAttribute("identifier", BuildIdentifier("model", model.ScopeKey)),
            new XElement(archimate + "name", new XAttribute(xml + "lang", "en"), model.PosterTitle),
            new XElement(archimate + "documentation", new XAttribute(xml + "lang", "en"), model.PosterDescription));

        var elements = new XElement(archimate + "elements");
        var relationships = new XElement(archimate + "relationships");
        var organizations = new XElement(archimate + "organizations");
        var productElementIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var domain in model.Domains)
        {
            var domainId = BuildIdentifier("domain", domain.DomainId, domain.Code);
            elements.Add(BuildArchiElement(archimate, xsi, xml, domainId, "Grouping", domain.DisplayLabel, $"Domain code: {domain.Code}"));

            foreach (var capability in domain.Capabilities)
            {
                var capabilityId = BuildIdentifier("capability", capability.CapabilityId, capability.Code);
                elements.Add(BuildArchiElement(archimate, xsi, xml, capabilityId, "Capability", capability.DisplayLabel, $"Capability code: {capability.Code}"));
                relationships.Add(BuildArchiRelationship(archimate, xsi, xml, BuildIdentifier("relationship", domainId, capabilityId), "Composition", domainId, capabilityId, "Contains"));

                foreach (var component in capability.Components)
                {
                    var componentId = BuildIdentifier("component", component.ComponentId, component.Code);
                    elements.Add(BuildArchiElement(archimate, xsi, xml, componentId, GetComponentArchiType(model.ScopeKey), component.DisplayLabel, $"Component code: {component.Code}"));
                    relationships.Add(BuildArchiRelationship(archimate, xsi, xml, BuildIdentifier("relationship", capabilityId, componentId), "Composition", capabilityId, componentId, "Includes"));

                    foreach (var product in component.Products)
                    {
                        var productId = BuildIdentifier("item", product.ProductId, product.Name);
                        if (productElementIds.Add(productId))
                        {
                            elements.Add(BuildArchiElement(archimate, xsi, xml, productId, "ApplicationComponent", product.Name, BuildProductDocumentation(product)));
                        }

                        relationships.Add(BuildArchiRelationship(archimate, xsi, xml, BuildIdentifier("relationship", componentId, productId), "Association", componentId, productId, model.MappedItemLabel));
                    }
                }
            }
        }

        organizations.Add(BuildOrganizationFolder(
            archimate,
            xml,
            "Model",
            model.Domains.Select(domain =>
                BuildOrganizationReference(
                    archimate,
                    xml,
                    BuildIdentifier("domain", domain.DomainId, domain.Code),
                    domain.DisplayLabel,
                    domain.Capabilities.Select(capability =>
                        BuildOrganizationReference(
                            archimate,
                            xml,
                            BuildIdentifier("capability", capability.CapabilityId, capability.Code),
                            capability.DisplayLabel,
                            capability.Components.Select(component =>
                                BuildOrganizationReference(
                                    archimate,
                                    xml,
                                    BuildIdentifier("component", component.ComponentId, component.Code),
                                    component.DisplayLabel))))))));

        archiModel.Add(elements);
        archiModel.Add(relationships);
        archiModel.Add(organizations);

        return SerializeXml(new XDocument(new XDeclaration("1.0", "utf-8", null), archiModel), includeDeclaration: true);
    }

    public static string BuildDrawIoFileName(ModelDiagramReportViewModel model) => $"{BuildFileStem(model)}.drawio";

    public static string BuildArchiXmlFileName(ModelDiagramReportViewModel model) => $"{BuildFileStem(model)}.archimate.xml";

    private static DiagramLayout BuildLayout(ModelDiagramReportViewModel model)
    {
        var visibleDomains = model.Domains
            .Where(domain => !model.OnlyShowMappedNodes || domain.ProductCount > 0)
            .ToList();
        var domains = new List<DiagramDomainPlacement>();
        var rowHeights = new Dictionary<int, double>();

        for (var index = 0; index < visibleDomains.Count; index++)
        {
            var column = index % Columns;
            var row = index / Columns;
            var x = 40 + (column * (DomainWidth + DomainGap));
            var y = 40 + Enumerable.Range(0, row).Sum(rowIndex => rowHeights.GetValueOrDefault(rowIndex) + DomainGap);
            var placement = BuildDomainPlacement(visibleDomains[index], x, y, DomainWidth);
            domains.Add(placement);
            rowHeights[row] = Math.Max(rowHeights.GetValueOrDefault(row), placement.Height);
        }

        var canvasWidth = Math.Max(900, 80 + (Math.Min(Columns, Math.Max(1, visibleDomains.Count)) * DomainWidth) + ((Math.Min(Columns, Math.Max(1, visibleDomains.Count)) - 1) * DomainGap));
        var canvasHeight = domains.Count == 0 ? 500 : domains.Max(x => x.Y + x.Height) + 40;

        return new DiagramLayout(domains, canvasWidth, canvasHeight);
    }

    private static DiagramDomainPlacement BuildDomainPlacement(ModelDiagramDomainViewModel domain, double x, double y, double width)
    {
        var cursorY = y + HeaderHeight + RowGap;
        var capabilities = new List<DiagramCapabilityPlacement>();
        var innerWidth = width - (CapabilityPadding * 2);

        foreach (var capability in domain.Capabilities)
        {
            var capabilityPlacement = BuildCapabilityPlacement(capability, x + CapabilityPadding, cursorY, innerWidth);
            capabilities.Add(capabilityPlacement);
            cursorY += capabilityPlacement.Height + RowGap;
        }

        var height = Math.Max(HeaderHeight + (CapabilityPadding * 2), cursorY - y + CapabilityPadding - RowGap);
        return new DiagramDomainPlacement(domain, x, y, width, height, capabilities);
    }

    private static DiagramCapabilityPlacement BuildCapabilityPlacement(ModelDiagramCapabilityViewModel capability, double x, double y, double width)
    {
        var cursorY = y + HeaderHeight + ComponentPadding;
        var components = new List<DiagramComponentPlacement>();
        var innerWidth = width - (ComponentPadding * 2);

        foreach (var component in capability.Components)
        {
            var productCount = Math.Max(0, component.Products.Count);
            var height = ComponentHeight + (productCount == 0 ? 0 : (productCount * (ProductHeight + ProductGap)) + ComponentPadding);
            var productY = cursorY + ComponentHeight;
            var products = component.Products
                .Select(product =>
                {
                    var placement = new DiagramProductPlacement(product, x + (ComponentPadding * 2), productY, innerWidth - (ComponentPadding * 2), ProductHeight);
                    productY += ProductHeight + ProductGap;
                    return placement;
                })
                .ToList();

            components.Add(new DiagramComponentPlacement(component, x + ComponentPadding, cursorY, innerWidth, height, products));
            cursorY += height + ComponentPadding;
        }

        var capabilityHeight = Math.Max(HeaderHeight + (ComponentPadding * 2), cursorY - y);
        return new DiagramCapabilityPlacement(capability, x, y, width, capabilityHeight, components);
    }

    private static void AddVertex(XElement root, string id, string parent, double x, double y, double width, double height, string style, string value)
    {
        root.Add(new XElement("mxCell",
            new XAttribute("id", id),
            new XAttribute("parent", parent),
            new XAttribute("style", style),
            new XAttribute("value", value),
            new XAttribute("vertex", "1"),
            new XElement("mxGeometry",
                new XAttribute("x", FormatNumber(x)),
                new XAttribute("y", FormatNumber(y)),
                new XAttribute("width", FormatNumber(width)),
                new XAttribute("height", FormatNumber(height)),
                new XAttribute("as", "geometry"))));
    }

    private static XElement BuildArchiElement(XNamespace archimate, XNamespace xsi, XNamespace xml, string id, string type, string name, string? documentation = null)
    {
        var element = new XElement(archimate + "element",
            new XAttribute("identifier", id),
            new XAttribute(xsi + "type", type),
            new XElement(archimate + "name", new XAttribute(xml + "lang", "en"), name));

        if (!string.IsNullOrWhiteSpace(documentation))
        {
            element.Add(new XElement(archimate + "documentation", new XAttribute(xml + "lang", "en"), documentation));
        }

        return element;
    }

    private static XElement BuildArchiRelationship(XNamespace archimate, XNamespace xsi, XNamespace xml, string id, string type, string sourceId, string targetId, string name) =>
        new(archimate + "relationship",
            new XAttribute("identifier", id),
            new XAttribute(xsi + "type", type),
            new XAttribute("source", sourceId),
            new XAttribute("target", targetId),
            new XElement(archimate + "name", new XAttribute(xml + "lang", "en"), name));

    private static XElement BuildOrganizationFolder(XNamespace archimate, XNamespace xml, string label, IEnumerable<XElement> children) =>
        new(archimate + "item",
            new XElement(archimate + "label", new XAttribute(xml + "lang", "en"), label),
            children);

    private static XElement BuildOrganizationReference(XNamespace archimate, XNamespace xml, string identifierRef, string label, IEnumerable<XElement>? children = null)
    {
        var item = new XElement(archimate + "item",
            new XAttribute("identifierRef", identifierRef),
            new XElement(archimate + "label", new XAttribute(xml + "lang", "en"), label));

        if (children is not null)
        {
            item.Add(children);
        }

        return item;
    }

    private static string BuildProductDocumentation(ModelDiagramProductViewModel product)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(product.StatusLabel))
        {
            parts.Add($"Status: {product.StatusLabel}");
        }

        if (!string.IsNullOrWhiteSpace(product.VersionLabel))
        {
            parts.Add($"Version: {product.VersionLabel}");
        }

        if (!string.IsNullOrWhiteSpace(product.OwnersLabel))
        {
            parts.Add($"Owners: {product.OwnersLabel}");
        }

        return string.Join(Environment.NewLine, parts);
    }

    private static string GetComponentArchiType(string scopeKey) =>
        scopeKey.Trim().ToLowerInvariant() switch
        {
            "arm" => "ApplicationComponent",
            "drm" => "BusinessObject",
            "brm" => "Capability",
            _ => "TechnologyService"
        };

    private static string BuildFileStem(ModelDiagramReportViewModel model) =>
        $"herm-{SanitizeFileToken(model.ScopeKey)}-{SanitizeFileToken(model.PosterTitle)}";

    private static string BuildIdentifier(string prefix, params object?[] parts)
    {
        var token = string.Join("-", parts.Select(part => SanitizeFileToken(Convert.ToString(part, CultureInfo.InvariantCulture) ?? string.Empty)));
        return string.IsNullOrWhiteSpace(token) ? prefix : $"{prefix}-{token}";
    }

    private static string SanitizeFileToken(string value)
    {
        var builder = new StringBuilder();
        foreach (var character in value.Trim().ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(character))
            {
                builder.Append(character);
            }
            else if (builder.Length > 0 && builder[^1] != '-')
            {
                builder.Append('-');
            }
        }

        return builder.ToString().Trim('-');
    }

    private static byte[] SerializeXml(XDocument document, bool includeDeclaration)
    {
        using var stream = new MemoryStream();
        using (var writer = XmlWriter.Create(stream, new XmlWriterSettings
        {
            Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            Indent = false,
            OmitXmlDeclaration = !includeDeclaration
        }))
        {
            document.Save(writer);
        }

        return stream.ToArray();
    }

    private static string FormatNumber(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);

    private sealed record DiagramLayout(IReadOnlyList<DiagramDomainPlacement> Domains, double CanvasWidth, double CanvasHeight);
    private sealed record DiagramDomainPlacement(ModelDiagramDomainViewModel Domain, double X, double Y, double Width, double Height, IReadOnlyList<DiagramCapabilityPlacement> Capabilities);
    private sealed record DiagramCapabilityPlacement(ModelDiagramCapabilityViewModel Capability, double X, double Y, double Width, double Height, IReadOnlyList<DiagramComponentPlacement> Components);
    private sealed record DiagramComponentPlacement(ModelDiagramComponentViewModel Component, double X, double Y, double Width, double Height, IReadOnlyList<DiagramProductPlacement> Products);
    private sealed record DiagramProductPlacement(ModelDiagramProductViewModel Product, double X, double Y, double Width, double Height);
}
