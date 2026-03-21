namespace HERMMapperApp.Models;

public enum ReferenceModelKind
{
    Trm = 0,
    Arm = 1,
    Brm = 2
}

public static class ReferenceModelCatalog
{
    public static IReadOnlyList<ReferenceModelKind> All { get; } =
    [
        ReferenceModelKind.Trm,
        ReferenceModelKind.Arm,
        ReferenceModelKind.Brm
    ];

    public static string GetDisplayName(ReferenceModelKind modelKind) =>
        modelKind switch
        {
            ReferenceModelKind.Trm => "Technology Reference Model",
            ReferenceModelKind.Arm => "Application Reference Model",
            ReferenceModelKind.Brm => "Business Reference Model",
            _ => "Reference Model"
        };

    public static string GetShortName(ReferenceModelKind modelKind) =>
        modelKind switch
        {
            ReferenceModelKind.Trm => "TRM",
            ReferenceModelKind.Arm => "ARM",
            ReferenceModelKind.Brm => "BRM",
            _ => "Model"
        };

    public static string GetWorkbookLabel(ReferenceModelKind modelKind) => $"{GetShortName(modelKind)} catalogue";

    public static string GetDomainLabel(ReferenceModelKind modelKind) =>
        modelKind switch
        {
            ReferenceModelKind.Brm => "Groups",
            _ => "Domains"
        };

    public static string GetCapabilityLabel(ReferenceModelKind modelKind) =>
        modelKind switch
        {
            ReferenceModelKind.Brm => "Level 1 capabilities",
            _ => "Capabilities"
        };

    public static string GetComponentLabel(ReferenceModelKind modelKind) =>
        modelKind switch
        {
            ReferenceModelKind.Brm => "Level 2 capabilities",
            _ => "Components"
        };

    public static string GetDomainPrefix(ReferenceModelKind modelKind) =>
        modelKind switch
        {
            ReferenceModelKind.Trm => "TD",
            ReferenceModelKind.Arm => "AD",
            ReferenceModelKind.Brm => "BD",
            _ => string.Empty
        };

    public static string GetCapabilityPrefix(ReferenceModelKind modelKind) =>
        modelKind switch
        {
            ReferenceModelKind.Trm => "TP",
            ReferenceModelKind.Arm => "AP",
            ReferenceModelKind.Brm => "BC",
            _ => string.Empty
        };

    public static string GetComponentPrefix(ReferenceModelKind modelKind) =>
        modelKind switch
        {
            ReferenceModelKind.Trm => "TC",
            ReferenceModelKind.Arm => "AC",
            ReferenceModelKind.Brm => "BC",
            _ => string.Empty
        };
}
