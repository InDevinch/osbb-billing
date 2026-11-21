using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;
using Microsoft.AspNetCore.Mvc.Rendering;
using OsbbBilling.Web.Models.Entities;

namespace OsbbBilling.Web.Models;

public class ChangePayerViewModel : IValidatableObject
{
    [Required(ErrorMessage = "Введіть номер нового рахунку.")]
    [StringLength(30, ErrorMessage = "Номер має містити до 30 символів.")]
    [RegularExpression("^[A-Za-z0-9_-]+$", ErrorMessage = "Використовуйте латинські літери, цифри, дефіс або підкреслення.")]
    [Display(Name = "Номер нового рахунку")]
    public string AccountNumber { get; set; } = "";

    [Range(1, int.MaxValue, ErrorMessage = "Оберіть нового платника.")]
    [Display(Name = "Новий платник")]
    public int OwnerId { get; set; }

    [Required(ErrorMessage = "Оберіть місяць зміни платника.")]
    [DataType(DataType.Date)]
    [Display(Name = "Новий платник з")]
    public DateOnly? ValidFrom { get; set; }

    [BindNever, ValidateNever]
    public Account CurrentAccount { get; set; } = null!;
    [BindNever, ValidateNever]
    public IReadOnlyList<SelectListItem> Owners { get; set; } = [];

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (ValidFrom.HasValue && ValidFrom.Value.Day != 1)
            yield return new("Зміна платника можлива з першого числа місяця.", [nameof(ValidFrom)]);
    }
}
