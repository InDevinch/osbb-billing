using System.ComponentModel.DataAnnotations;

namespace OsbbBilling.Web.Models;

public class OwnerFormViewModel
{
    [Required(ErrorMessage = "Введіть ПІБ власника.")]
    [StringLength(150, ErrorMessage = "ПІБ має містити до 150 символів.")]
    [Display(Name = "ПІБ")]
    public string FullName { get; set; } = "";

    [StringLength(30, ErrorMessage = "Номер телефону має містити до 30 символів.")]
    [Phone(ErrorMessage = "Введіть коректний номер телефону.")]
    [Display(Name = "Телефон")]
    public string? Phone { get; set; }

    [StringLength(254, ErrorMessage = "Електронна адреса має містити до 254 символів.")]
    [EmailAddress(ErrorMessage = "Введіть коректну електронну адресу.")]
    [Display(Name = "Електронна пошта")]
    public string? Email { get; set; }
}
