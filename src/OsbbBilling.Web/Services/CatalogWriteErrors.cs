using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace OsbbBilling.Web.Services;

public static class CatalogWriteErrors
{
    public static bool IsDatabaseError(Exception exception) =>
        exception is DbUpdateException or SqlException
        || exception is InvalidOperationException && exception.GetBaseException() is SqlException { Number: 1205 };

    public static string Message(Exception exception)
    {
        if (exception is DbUpdateConcurrencyException)
            return "Дані вже змінено іншим запитом. Оновіть сторінку.";
        return (exception.GetBaseException() as SqlException)?.Number switch
        {
            2601 or 2627 => "Такий запис уже існує. Перевірте номер, назву або період.",
            51001 => "Періоди дії не повинні перетинатися.",
            51004 => "Використаний тариф не можна змінювати. Оберіть місяць після наявних нарахувань.",
            51006 => "Дата завершення виключає наявні нарахування.",
            51007 => "Період роботи лічильника виходить за межі підключення послуги.",
            51008 => "Перевірте спосіб розрахунку послуги та період роботи лічильника.",
            51009 => "Початкові дані лічильника з показаннями змінювати не можна.",
            51010 => "Дата завершення роботи виключає наявні показання.",
            51011 => "Для нового способу розрахунку або одиниці виміру створіть окрему послугу.",
            51012 => "Показання не відповідає періоду роботи або початковому значенню лічильника.",
            51013 => "Показання не можуть зменшуватися з часом.",
            51014 => "Показання вже використане в нарахуванні та не може бути змінене.",
            547 => "Обрані дані більше не доступні. Оновіть сторінку.",
            1205 => "Дані одночасно змінювалися іншим запитом. Повторіть операцію.",
            _ => "Не вдалося зберегти зміни. Операцію скасовано."
        };
    }
}
