using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace OsbbBilling.Web.Models;

public class OpenAccountViewModel : IValidatableObject
{
    [Required(ErrorMessage = "Введіть номер рахунку.")]
    [StringLength(30, ErrorMessage = "Номер має містити до 30 символів.")]
    [RegularExpression("^[A-Za-z0-9_-]+$", ErrorMessage = "Використовуйте латинські літери, цифри, дефіс або підкреслення.")]
    [Display(Name = "Номер особового рахунку")]
    public string AccountNumber { get; set; } = "";

    [Range(1, int.MaxValue, ErrorMessage = "Оберіть платника.")]
    [Display(Name = "Відповідальний платник")]
    public int OwnerId { get; set; }

    [Range(1, int.MaxValue, ErrorMessage = "Оберіть приміщення.")]
    [Display(Name = "Приміщення")]
    public int PremiseId { get; set; }

    [Required(ErrorMessage = "Оберіть місяць початку дії.")]
    [DataType(DataType.Date)]
    [Display(Name = "Діє з")]
    public DateOnly? ValidFrom { get; set; }

    [DataType(DataType.Date)]
    [Display(Name = "Діє до")]
    public DateOnly? ValidTo { get; set; }

    [BindNever, ValidateNever]
    public IReadOnlyList<SelectListItem> Owners { get; set; } = [];
    [BindNever, ValidateNever]
    public IReadOnlyList<SelectListItem> Premises { get; set; } = [];

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (ValidFrom.HasValue && ValidFrom.Value.Day != 1)
            yield return new("Оберіть перше число місяця.", [nameof(ValidFrom)]);
        if (ValidTo.HasValue && ValidTo.Value.Day != 1)
            yield return new("Оберіть перше число місяця.", [nameof(ValidTo)]);
        if (ValidFrom.HasValue && ValidTo.HasValue && ValidTo <= ValidFrom)
            yield return new("Завершення має бути пізніше початку дії.", [nameof(ValidTo)]);
    }
}
