using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;
using OsbbBilling.Web.Models.Entities;

namespace OsbbBilling.Web.Models;

public class RemoveMeterViewModel
{
    [Required(ErrorMessage = "Оберіть місяць завершення."), FirstOfMonth, DataType(DataType.Date)]
    [Display(Name = "Лічильник не працює з")]
    public DateOnly? RemovedOn { get; set; }
    [BindNever, ValidateNever]
    public Meter CurrentMeter { get; set; } = null!;
}
