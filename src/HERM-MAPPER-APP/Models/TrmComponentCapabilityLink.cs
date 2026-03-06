namespace HERM_MAPPER_APP.Models;

public sealed class TrmComponentCapabilityLink
{
    public int Id { get; set; }

    public int TrmComponentId { get; set; }
    public TrmComponent? TrmComponent { get; set; }

    public int TrmCapabilityId { get; set; }
    public TrmCapability? TrmCapability { get; set; }

    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
}
