using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace OsbbBilling.Web.Models;

public class PremiseFormViewModel : IValidatableObject
{
    [Range(1, int.MaxValue, ErrorMessage = "Оберіть будинок.")]
    [Display(Name = "Будинок")]
    public int BuildingId { get; set; }

    [Required(ErrorMessage = "Введіть номер приміщення.")]
    [StringLength(20, ErrorMessage = "Номер має містити до 20 символів.")]
    [Display(Name = "Номер приміщення")]
    public string Number { get; set; } = "";

    [Required(ErrorMessage = "Оберіть тип приміщення.")]
    [RegularExpression("^(Apartment|Nonres)$", ErrorMessage = "Оберіть коректний тип приміщення.")]
    [Display(Name = "Тип")]
    public string Kind { get; set; } = PremiseKinds.Apartment;

    [Range(0.01, 99999999.99, ErrorMessage = "Площа має бути від 0,01 до 99 999 999,99 м².")]
    [Display(Name = "Площа, м²")]
    public decimal Area { get; set; }

    [Range(0, short.MaxValue, ErrorMessage = "Кількість мешканців має бути від 0 до 32767.")]
    [Display(Name = "Кількість мешканців")]
    public int ResidentsCount { get; set; }

    [BindNever, ValidateNever]
    public IReadOnlyList<SelectListItem> Buildings { get; set; } = [];

    [BindNever, ValidateNever]
    public bool HasHistory { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Area != decimal.Round(Area, 2))
            yield return new ValidationResult("Для площі можна вказати до двох знаків після коми.", [nameof(Area)]);
    }
}
