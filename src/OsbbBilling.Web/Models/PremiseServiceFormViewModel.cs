using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace OsbbBilling.Web.Models;

public class PremiseServiceFormViewModel : IValidatableObject
{
    [Range(1, int.MaxValue, ErrorMessage = "Оберіть приміщення.")]
    [Display(Name = "Приміщення")]
    public int PremiseId { get; set; }
    [Range(1, int.MaxValue, ErrorMessage = "Оберіть послугу.")]
    [Display(Name = "Послуга")]
    public int ServiceId { get; set; }
    [Required(ErrorMessage = "Оберіть місяць початку дії."), FirstOfMonth, DataType(DataType.Date)]
    [Display(Name = "Діє з")]
    public DateOnly? ValidFrom { get; set; }
    [FirstOfMonth, DataType(DataType.Date)]
    [Display(Name = "Діє до, не включно")]
    public DateOnly? ValidTo { get; set; }
    [BindNever, ValidateNever]
    public IReadOnlyList<SelectListItem> Premises { get; set; } = [];
    [BindNever, ValidateNever]
    public IReadOnlyList<SelectListItem> Services { get; set; } = [];

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (ValidFrom.HasValue && ValidTo.HasValue && ValidTo <= ValidFrom)
            yield return new("Завершення має бути пізніше початку.", [nameof(ValidTo)]);
    }
}
