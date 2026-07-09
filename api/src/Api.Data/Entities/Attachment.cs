namespace Api.Data.Entities;

public class Attachment
{
    public int Id { get; set; }
    public int RequestId { get; set; }
    public Request Request { get; set; } = null!;

    public string FileName { get; set; } = null!;
    public string StoragePath { get; set; } = null!;
    public string ContentType { get; set; } = null!;

    public int UploadedByUserId { get; set; }
    public User UploadedByUser { get; set; } = null!;
    public DateTime UploadedAt { get; set; }
}
