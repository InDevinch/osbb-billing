using System.ComponentModel.DataAnnotations;

namespace OsbbBilling.Web.Models;

public class LoginViewModel
{
    [Required(ErrorMessage = "Введіть логін.")]
    [StringLength(100, ErrorMessage = "Логін має містити до 100 символів.")]
    [Display(Name = "Логін")]
    public string Login { get; set; } = "";

    [Required(ErrorMessage = "Введіть пароль.")]
    [StringLength(256, ErrorMessage = "Пароль має містити до 256 символів.")]
    [DataType(DataType.Password)]
    [Display(Name = "Пароль")]
    public string Password { get; set; } = "";

    public string? ReturnUrl { get; set; }
}
