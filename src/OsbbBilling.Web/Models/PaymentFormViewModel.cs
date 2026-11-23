using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace OsbbBilling.Web.Models;

public class PaymentFormViewModel : IValidatableObject
{
    [Range(1, int.MaxValue, ErrorMessage = "Оберіть особовий рахунок.")]
    [Display(Name = "Особовий рахунок")]
    public int AccountId { get; set; }
    [Required(ErrorMessage = "Вкажіть дату оплати."), DataType(DataType.Date)]
    [Display(Name = "Дата оплати")]
    public DateOnly? PaymentDate { get; set; }
    [Range(typeof(decimal), "0.01", "99999999999999999.99", ParseLimitsInInvariantCulture = true,
        ErrorMessage = "Сума має бути від 0,01 до 99 999 999 999 999 999,99 грн.")]
    [Display(Name = "Сума, грн")]
    public decimal Amount { get; set; }
    [Required(ErrorMessage = "Вкажіть номер платіжного документа.")]
    [StringLength(100, ErrorMessage = "Номер документа може містити до 100 символів.")]
    [Display(Name = "Номер платіжного документа")]
    public string Reference { get; set; } = "";
    [BindNever, ValidateNever]
    public IReadOnlyList<SelectListItem> Accounts { get; set; } = [];

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Amount != decimal.Round(Amount, 2))
            yield return new("Сума може містити до двох знаків після коми.", [nameof(Amount)]);
    }
}
