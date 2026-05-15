using HERMMapperApp.Models;

namespace HERMMapperApp.ViewModels;

public sealed class HomeDashboardViewModel
{
    public int ProductCount { get; init; }
    public int CompletedMappings { get; init; }
    public int TrmDomainCount { get; init; }
    public int TrmCapabilityCount { get; init; }
    public int TrmComponentCount { get; init; }
    public int ArmDomainCount { get; init; }
    public int ArmCapabilityCount { get; init; }
    public int ArmComponentCount { get; init; }
    public int BrmDomainCount { get; init; }
    public int BrmCapabilityCount { get; init; }
    public int BrmComponentCount { get; init; }
    public int DrmTopicTypeCount { get; init; }
    public int DrmTopicCount { get; init; }
    public int DrmDataEntityCount { get; init; }
    public IReadOnlyList<ProductCatalogItem> RecentProducts { get; init; } = [];

    public bool HasTrmModel => TrmDomainCount != 0 || TrmCapabilityCount != 0 || TrmComponentCount != 0;
    public bool HasArmModel => ArmDomainCount != 0 || ArmCapabilityCount != 0 || ArmComponentCount != 0;
    public bool HasBrmModel => BrmDomainCount != 0 || BrmCapabilityCount != 0 || BrmComponentCount != 0;
    public bool HasDrmModel => DrmTopicTypeCount != 0 || DrmTopicCount != 0 || DrmDataEntityCount != 0;

    public string ReferenceModelStatus
    {
        get
        {
            if (HasTrmModel && HasArmModel && HasBrmModel && HasDrmModel)
            {
                return "TRM, ARM, BRM, and DRM are imported and ready.";
            }

            if (HasTrmModel || HasArmModel || HasBrmModel || HasDrmModel)
            {
                return "Reference models are partially imported. Open Configuration to load the missing models.";
            }

            return "No reference models imported yet.";
        }
    }
}
