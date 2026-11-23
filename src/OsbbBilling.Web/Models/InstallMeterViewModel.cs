using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace OsbbBilling.Web.Models;

public class InstallMeterViewModel : IValidatableObject
{
    [Range(1, int.MaxValue, ErrorMessage = "Оберіть підключення послуги.")]
    [Display(Name = "Послуга приміщення")]
    public int PremiseServiceId { get; set; }
    [Required(ErrorMessage = "Введіть серійний номер.")]
    [StringLength(50, ErrorMessage = "Серійний номер має містити до 50 символів.")]
    [Display(Name = "Серійний номер")]
    public string SerialNumber { get; set; } = "";
    [Required(ErrorMessage = "Оберіть місяць встановлення."), FirstOfMonth, DataType(DataType.Date)]
    [Display(Name = "Працює з")]
    public DateOnly? InstalledOn { get; set; }
    [FirstOfMonth, DataType(DataType.Date)]
    [Display(Name = "Працює до, не включно")]
    public DateOnly? RemovedOn { get; set; }
    [Range(typeof(decimal), "0", "999999999999999.999", ParseLimitsInInvariantCulture = true,
        ErrorMessage = "Початкове показання має бути від 0 до 999 999 999 999 999,999.")]
    [Display(Name = "Початкове показання")]
    public decimal InitialReading { get; set; }
    [BindNever, ValidateNever]
    public IReadOnlyList<SelectListItem> Assignments { get; set; } = [];

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (RemovedOn.HasValue && InstalledOn.HasValue && RemovedOn <= InstalledOn)
            yield return new("Завершення має бути пізніше встановлення.", [nameof(RemovedOn)]);
        if (InitialReading != decimal.Round(InitialReading, 3))
            yield return new("Можна вказати до трьох знаків після коми.", [nameof(InitialReading)]);
    }
}
