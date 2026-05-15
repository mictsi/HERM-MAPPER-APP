namespace HERMMapperApp.Models;

public enum ReferenceModelKind
{
    Trm = 0,
    Arm = 1,
    Brm = 2,
    Drm = 3
}

public static class ReferenceModelCatalog
{
    public static IReadOnlyList<ReferenceModelKind> All { get; } =
    [
        ReferenceModelKind.Trm,
        ReferenceModelKind.Arm,
        ReferenceModelKind.Brm,
        ReferenceModelKind.Drm
    ];

    public static string GetDisplayName(ReferenceModelKind modelKind) =>
        modelKind switch
        {
            ReferenceModelKind.Trm => "Technology Reference Model",
            ReferenceModelKind.Arm => "Application Reference Model",
            ReferenceModelKind.Brm => "Business Reference Model",
            ReferenceModelKind.Drm => "Data Reference Model",
            _ => "Reference Model"
        };

    public static string GetShortName(ReferenceModelKind modelKind) =>
        modelKind switch
        {
            ReferenceModelKind.Trm => "TRM",
            ReferenceModelKind.Arm => "ARM",
            ReferenceModelKind.Brm => "BRM",
            ReferenceModelKind.Drm => "DRM",
            _ => "Model"
        };

    public static string GetWorkbookLabel(ReferenceModelKind modelKind) => $"{GetShortName(modelKind)} catalogue";

    public static string GetDomainLabel(ReferenceModelKind modelKind) =>
        modelKind switch
        {
            ReferenceModelKind.Brm => "Groups",
            ReferenceModelKind.Drm => "Topic types",
            _ => "Domains"
        };

    public static string GetCapabilityLabel(ReferenceModelKind modelKind) =>
        modelKind switch
        {
            ReferenceModelKind.Brm => "Level 1 capabilities",
            ReferenceModelKind.Drm => "Topics",
            _ => "Capabilities"
        };

    public static string GetComponentLabel(ReferenceModelKind modelKind) =>
        modelKind switch
        {
            ReferenceModelKind.Brm => "Level 2 capabilities",
            ReferenceModelKind.Drm => "Data entities",
            _ => "Components"
        };

    public static string GetDomainPrefix(ReferenceModelKind modelKind) =>
        modelKind switch
        {
            ReferenceModelKind.Trm => "TD",
            ReferenceModelKind.Arm => "AD",
            ReferenceModelKind.Brm => "BD",
            ReferenceModelKind.Drm => "DY",
            _ => string.Empty
        };

    public static string GetCapabilityPrefix(ReferenceModelKind modelKind) =>
        modelKind switch
        {
            ReferenceModelKind.Trm => "TP",
            ReferenceModelKind.Arm => "AP",
            ReferenceModelKind.Brm => "BC",
            ReferenceModelKind.Drm => "DT",
            _ => string.Empty
        };

    public static string GetComponentPrefix(ReferenceModelKind modelKind) =>
        modelKind switch
        {
            ReferenceModelKind.Trm => "TC",
            ReferenceModelKind.Arm => "AC",
            ReferenceModelKind.Brm => "BC",
            ReferenceModelKind.Drm => "DE",
            _ => string.Empty
        };
}
