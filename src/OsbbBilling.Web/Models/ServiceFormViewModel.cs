using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace OsbbBilling.Web.Models;

public class ServiceFormViewModel
{
    [Required(ErrorMessage = "Введіть назву послуги.")]
    [StringLength(100, ErrorMessage = "Назва має містити до 100 символів.")]
    [Display(Name = "Назва")]
    public string Name { get; set; } = "";

    [Required(ErrorMessage = "Введіть одиницю виміру.")]
    [StringLength(20, ErrorMessage = "Одиниця виміру має містити до 20 символів.")]
    [Display(Name = "Одиниця виміру")]
    public string Unit { get; set; } = "";

    [Required(ErrorMessage = "Оберіть спосіб розрахунку.")]
    [RegularExpression("^(Area|Residents|Meter|Fixed)$", ErrorMessage = "Оберіть коректний спосіб розрахунку.")]
    [Display(Name = "Спосіб розрахунку")]
    public string CalculationMethod { get; set; } = "Area";

    [BindNever]
    public bool HasAssignments { get; set; }
}
