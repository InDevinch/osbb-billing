using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;
using OsbbBilling.Web.Models.Entities;

namespace OsbbBilling.Web.Models;

public class MeterReadingFormViewModel : IValidatableObject
{
    [Range(1, int.MaxValue, ErrorMessage = "Оберіть лічильник.")]
    public int MeterId { get; set; }
    [Required(ErrorMessage = "Оберіть місяць показання."), FirstOfMonth, DataType(DataType.Date)]
    [Display(Name = "За місяць")]
    public DateOnly? Period { get; set; }
    [Required(ErrorMessage = "Оберіть дату зняття показання."), DataType(DataType.Date)]
    [Display(Name = "Дата зняття показання")]
    public DateOnly? ReadingDate { get; set; }
    [Range(typeof(decimal), "0", "999999999999999.999", ParseLimitsInInvariantCulture = true,
        ErrorMessage = "Показання має бути від 0 до 999 999 999 999 999,999.")]
    [Display(Name = "Накопичувальне показання")]
    public decimal ReadingValue { get; set; }
    [BindNever, ValidateNever]
    public Meter CurrentMeter { get; set; } = null!;
    [BindNever, ValidateNever]
    public MeterReading? CurrentReading { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (ReadingValue != decimal.Round(ReadingValue, 3))
            yield return new("Можна вказати до трьох знаків після коми.", [nameof(ReadingValue)]);
    }
}
