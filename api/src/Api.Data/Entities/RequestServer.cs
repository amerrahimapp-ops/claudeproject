namespace Api.Data.Entities;

public class RequestServer
{
    public int Id { get; set; }
    public int RequestId { get; set; }
    public Request Request { get; set; } = null!;

    public string Hostname { get; set; } = null!;
    public string IpAddress { get; set; } = null!;

    /// <summary>Free-text OS (e.g. "RHEL 8.6"), distinct from the coarse <see cref="Platform"/> enum below.</summary>
    public string? Os { get; set; }

    public bool IsPhysical { get; set; }

    public ResourceType ResourceType { get; set; }
    public decimal CurrentValue { get; set; }
    public decimal RequestedValue { get; set; }
    public string? MountPoint { get; set; }
    public Platform Platform { get; set; }
    public bool DrApplicable { get; set; }
    public string? AppTier { get; set; }
}
