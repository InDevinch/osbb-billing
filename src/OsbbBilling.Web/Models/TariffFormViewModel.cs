using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;
using Microsoft.AspNetCore.Mvc.Rendering;
using OsbbBilling.Web.Models.Entities;

namespace OsbbBilling.Web.Models;

public class TariffFormViewModel : IValidatableObject
{
    [Range(1, int.MaxValue, ErrorMessage = "Оберіть будинок.")]
    [Display(Name = "Будинок")]
    public int BuildingId { get; set; }
    [Range(1, int.MaxValue, ErrorMessage = "Оберіть послугу.")]
    [Display(Name = "Послуга")]
    public int ServiceId { get; set; }
    [Range(typeof(decimal), "0", "99999999999999.9999", ParseLimitsInInvariantCulture = true,
        ErrorMessage = "Ціна має бути від 0 до 99 999 999 999 999,9999.")]
    [Display(Name = "Ціна за одиницю, грн")]
    public decimal UnitPrice { get; set; }
    [Required(ErrorMessage = "Оберіть місяць початку дії."), FirstOfMonth, DataType(DataType.Date)]
    [Display(Name = "Діє з")]
    public DateOnly? ValidFrom { get; set; }
    [FirstOfMonth, DataType(DataType.Date)]
    [Display(Name = "Діє до, не включно")]
    public DateOnly? ValidTo { get; set; }
    [BindNever, ValidateNever]
    public IReadOnlyList<SelectListItem> Buildings { get; set; } = [];
    [BindNever, ValidateNever]
    public IReadOnlyList<SelectListItem> Services { get; set; } = [];
    [BindNever, ValidateNever]
    public Tariff? CurrentTariff { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (UnitPrice != decimal.Round(UnitPrice, 4))
            yield return new("Ціна може містити до чотирьох знаків після коми.", [nameof(UnitPrice)]);
        if (ValidFrom.HasValue && ValidTo.HasValue && ValidTo <= ValidFrom)
            yield return new("Завершення має бути пізніше початку.", [nameof(ValidTo)]);
    }
}
