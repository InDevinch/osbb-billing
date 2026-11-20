-- Запити лише для читання. У SSMS можна виділяти та виконувати окремі блоки.
USE OSBB_Billing;
SET NOCOUNT ON;
-- 1. Таблиці, первинні та зовнішні ключі видно також у Object Explorer / Database Diagrams.
SELECT name FROM sys.tables ORDER BY name;
SELECT name,OBJECT_NAME(parent_object_id) AS ChildTable,
    OBJECT_NAME(referenced_object_id) AS ParentTable FROM sys.foreign_keys ORDER BY ChildTable;
-- 2. Історія тарифів. Межа ValidTo не включається до інтервалу.
SELECT s.Name,t.UnitPrice,t.ValidFrom,t.ValidTo FROM dbo.Tariffs t
JOIN dbo.Services s ON s.ServiceId=t.ServiceId ORDER BY s.ServiceId,t.ValidFrom;
-- 3. Рядки квитанції конкретного власника за місяць.
DECLARE @OwnerId int=1,@Period date='20261101';
SELECT AccountNumber,Address,PremiseNumber,ServiceName,Quantity,Unit,UnitPrice,Amount
FROM dbo.v_InvoiceLines WHERE OwnerId=@OwnerId AND Period=@Period ORDER BY ChargeId;
-- 4. Підсумки нарахувань по місяцях.
SELECT AccountId,Period,SUM(Amount) Charged FROM dbo.Charges GROUP BY AccountId,Period ORDER BY AccountId,Period;
-- 5. Борг та нерозподілений аванс виводяться окремо, NetBalance може бути від'ємним.
SELECT * FROM dbo.v_AccountBalances ORDER BY AccountId;
-- 6. Перелік боржників.
SELECT a.AccountNumber,o.FullName,b.Address,p.Number,a.DebtAmount
FROM dbo.v_AccountBalances a JOIN dbo.Owners o ON o.OwnerId=a.OwnerId
JOIN dbo.Premises p ON p.PremiseId=a.PremiseId JOIN dbo.Buildings b ON b.BuildingId=p.BuildingId
WHERE a.DebtAmount>0 ORDER BY a.DebtAmount DESC;
-- 7. Звіт за послугами та періодом на поточний момент.
SELECT Period,ServiceName,SUM(Amount) Charged,SUM(PaidAmount) Paid,SUM(DebtAmount) Debt
FROM dbo.v_InvoiceLines WHERE Period>='20261001' AND Period<'20261201'
GROUP BY Period,ServiceName ORDER BY Period,ServiceName;
-- 8. Як оплати закрили найстаріші нарахування.
SELECT p.Reference,p.PaymentDate,c.Period,s.Name,a.Amount
FROM dbo.PaymentAllocations a JOIN dbo.Payments p ON p.PaymentId=a.PaymentId
JOIN dbo.Charges c ON c.ChargeId=a.ChargeId
JOIN dbo.PremiseServices ps ON ps.PremiseServiceId=c.PremiseServiceId
JOIN dbo.Services s ON s.ServiceId=ps.ServiceId ORDER BY p.PaymentId,c.Period,c.ChargeId;
-- 9. Історія оплат власника. У застосунку OwnerId береться з автентифікованого користувача.
SELECT p.PaymentDate,p.Reference,p.Amount FROM dbo.Payments p
JOIN dbo.Accounts a ON a.AccountId=p.AccountId WHERE a.OwnerId=@OwnerId ORDER BY p.PaymentDate,p.PaymentId;
-- 10. Борг за місяцями є поточним залишком цих нарахувань, а не балансом на минулу дату.
SELECT AccountId,Period,SUM(DebtAmount) CurrentDebtForPeriod
FROM dbo.v_ChargeBalances GROUP BY AccountId,Period ORDER BY AccountId,Period;
