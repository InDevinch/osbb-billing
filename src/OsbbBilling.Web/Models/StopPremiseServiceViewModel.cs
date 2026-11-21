using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;
using OsbbBilling.Web.Models.Entities;

namespace OsbbBilling.Web.Models;

public class StopPremiseServiceViewModel
{
    [Required(ErrorMessage = "Оберіть місяць завершення."), FirstOfMonth, DataType(DataType.Date)]
    [Display(Name = "Послуга не діє з")]
    public DateOnly? ValidTo { get; set; }
    [BindNever, ValidateNever]
    public PremiseService CurrentAssignment { get; set; } = null!;
}
