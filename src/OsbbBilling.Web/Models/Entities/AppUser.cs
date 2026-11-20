namespace OsbbBilling.Web.Models.Entities;

public class AppUser
{
    public int UserId { get; set; }
    public string Login { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public string RoleCode { get; set; } = "";
    public int? OwnerId { get; set; }
    public bool IsActive { get; set; } = true;

    public Owner? Owner { get; set; }
    public ICollection<Charge> CreatedCharges { get; set; } = new List<Charge>();
    public ICollection<Payment> CreatedPayments { get; set; } = new List<Payment>();
}
