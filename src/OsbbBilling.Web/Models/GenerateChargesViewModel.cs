using System.ComponentModel.DataAnnotations;

namespace OsbbBilling.Web.Models;

public class GenerateChargesViewModel
{
    [Required(ErrorMessage = "Оберіть місяць нарахування."), FirstOfMonth, DataType(DataType.Date)]
    [Display(Name = "Місяць нарахування — перше число")]
    public DateOnly? Period { get; set; }
}
