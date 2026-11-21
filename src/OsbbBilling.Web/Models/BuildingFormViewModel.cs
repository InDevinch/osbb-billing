using System.ComponentModel.DataAnnotations;

namespace OsbbBilling.Web.Models;

public class BuildingFormViewModel
{
    [Required(ErrorMessage = "Введіть адресу будинку.")]
    [StringLength(250, ErrorMessage = "Адреса має містити до 250 символів.")]
    [Display(Name = "Адреса")]
    public string Address { get; set; } = "";
}
