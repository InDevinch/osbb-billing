namespace OsbbBilling.Web.Models.Entities;

public class Owner
{
    public int OwnerId { get; set; }
    public string FullName { get; set; } = "";
    public string? Phone { get; set; }
    public string? Email { get; set; }

    public AppUser? User { get; set; }
    public ICollection<Account> Accounts { get; set; } = new List<Account>();
}
