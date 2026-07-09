namespace Api.Data.Entities;

public class RequestServer
{
    public int Id { get; set; }
    public int RequestId { get; set; }
    public Request Request { get; set; } = null!;

    public string Hostname { get; set; } = null!;
    public ResourceType ResourceType { get; set; }
    public decimal CurrentValue { get; set; }
    public decimal RequestedValue { get; set; }
    public string? MountPoint { get; set; }
    public Platform Platform { get; set; }
    public bool DrApplicable { get; set; }
    public string? AppTier { get; set; }
}
