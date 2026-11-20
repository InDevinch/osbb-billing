# ОСББ — облік нарахувань та оплат

Курсова робота: вебзастосунок для обліку послуг ОСББ, нарахувань і платежів мешканців.

Технології: C#, ASP.NET Core MVC (.NET 10), SQL Server, Bootstrap.

Поки реалізовано початкову структуру застосунку та перевірку підключення до БД. Облік і авторизація ще в розробці.

## Запуск

Потрібні .NET SDK 10 і SQL Server. Команди виконуються з кореня репозиторію.

1. У SSMS виконайте `database/01_CreateDatabase.sql`. Він створить базу `OSBB_Billing`. Якщо вона вже створена, цей крок пропустіть.
2. За потреби додайте тестові дані скриптом `database/02_DemoData.sql` у порожню базу.
3. Налаштуйте локальне підключення та запустіть застосунок:

```powershell
dotnet restore --locked-mode
dotnet user-secrets set "ConnectionStrings:OsbbBilling" "Server=localhost;Database=OSBB_Billing;Integrated Security=True;Encrypt=True;TrustServerCertificate=True" --project src/OsbbBilling.Web
dotnet run --project src/OsbbBilling.Web --launch-profile http
```

Приклад підключення розрахований на локальний SQL Server із Windows Authentication. Налаштування зберігається поза репозиторієм у User Secrets. `TrustServerCertificate=True` використовується для локального сервера.

Відкрийте адресу з консолі та перейдіть на «Перевірка БД». Для створеної бази має відображатися 13 таблиць, 3 представлення і 3 процедури. Ця сторінка доступна лише в Development.

## Файли проєкту

- `src/OsbbBilling.Web` — MVC-застосунок.
- `database` — структура БД, тестові дані, приклади операцій і перевірки.

`03_Demonstration.sql` виконує операції з даними, а `04_Verification.sql` перевіряє правила БД. Використовуйте їх у тестовій базі. Застосунок самостійно не створює та не змінює схему БД.
