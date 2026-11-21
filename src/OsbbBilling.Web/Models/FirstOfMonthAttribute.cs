using System.ComponentModel.DataAnnotations;

namespace OsbbBilling.Web.Models;

public class FirstOfMonthAttribute() : ValidationAttribute("Оберіть перше число місяця.")
{
    public override bool IsValid(object? value) => value is null || value is DateOnly { Day: 1 };
}
