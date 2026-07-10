namespace Api.Data.Entities;

public class User
{
    public int Id { get; set; }
    public string AdUsername { get; set; } = null!;
    public string? PfNumber { get; set; }
    public string DisplayName { get; set; } = null!;
    public UserRole Role { get; set; }
    public string Email { get; set; } = null!;
    public DateTime CreatedAt { get; set; }

    public ICollection<Request> Requests { get; set; } = new List<Request>();
    public UserPreference? UserPreference { get; set; }
}
