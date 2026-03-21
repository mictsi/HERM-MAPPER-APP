namespace HERMMapperApp.Models;

public sealed class ArmComponentCapabilityLink
{
    public int Id { get; set; }

    public int ArmComponentId { get; set; }
    public ArmComponent? ArmComponent { get; set; }

    public int ArmCapabilityId { get; set; }
    public ArmCapability? ArmCapability { get; set; }

    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
}
