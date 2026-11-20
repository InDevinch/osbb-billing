USE OSBB_Billing;
SET NOCOUNT ON;
SET ANSI_NULLS ON; SET QUOTED_IDENTIFIER ON; SET ANSI_PADDING ON; SET ANSI_WARNINGS ON; SET ARITHABORT ON; SET CONCAT_NULL_YIELDS_NULL ON; SET NUMERIC_ROUNDABORT OFF;

DECLARE @error1 int=0;
BEGIN TRY
    BEGIN TRAN;
    EXEC(N'IF (SELECT COUNT(*) FROM sys.tables)<>13 THROW 51999,N''Кількість таблиць'',1;');
    IF XACT_STATE()<>0 ROLLBACK;
END TRY
BEGIN CATCH
    SET @error1=ERROR_NUMBER();
    IF XACT_STATE()<>0 ROLLBACK;
END CATCH;
IF @error1<>0
BEGIN
    PRINT CONCAT(N'FAIL 01 13 таблиць: ',@error1);
    THROW 51998,N'Перевірка не пройдена',1;
END;
PRINT N'PASS 01 13 таблиць';
DECLARE @error2 int=0;
BEGIN TRY
    BEGIN TRAN;
    EXEC(N'IF (SELECT COUNT(*) FROM dbo.Charges)<>16 THROW 51999,N''Кількість нарахувань'',1;');
    IF XACT_STATE()<>0 ROLLBACK;
END TRY
BEGIN CATCH
    SET @error2=ERROR_NUMBER();
    IF XACT_STATE()<>0 ROLLBACK;
END CATCH;
IF @error2<>0
BEGIN
    PRINT CONCAT(N'FAIL 02 16 нарахувань за два місяці: ',@error2);
    THROW 51998,N'Перевірка не пройдена',1;
END;
PRINT N'PASS 02 16 нарахувань за два місяці';
DECLARE @error3 int=0;
BEGIN TRY
    BEGIN TRAN;
    EXEC(N'IF EXISTS(SELECT 1 FROM (VALUES
(1,CONVERT(date,''20261001''),CONVERT(decimal(19,2),670)),(1,''20261101'',756.25),
(2,''20261001'',880),(2,''20261101'',975)) e(AccountId,Period,Expected)
OUTER APPLY(SELECT SUM(Amount) Actual FROM dbo.Charges c WHERE c.AccountId=e.AccountId AND c.Period=e.Period) a
WHERE ISNULL(a.Actual,-1)<>e.Expected) THROW 51999,N''Суми за формулами'',1;');
    IF XACT_STATE()<>0 ROLLBACK;
END TRY
BEGIN CATCH
    SET @error3=ERROR_NUMBER();
    IF XACT_STATE()<>0 ROLLBACK;
END CATCH;
IF @error3<>0
BEGIN
    PRINT CONCAT(N'FAIL 03 Чотири формули і новий тариф: ',@error3);
    THROW 51998,N'Перевірка не пройдена',1;
END;
PRINT N'PASS 03 Чотири формули і новий тариф';
DECLARE @error4 int=0;
BEGIN TRY
    BEGIN TRAN;
    EXEC(N'IF NOT EXISTS(SELECT 1 FROM dbo.v_AccountBalances WHERE AccountId=1 AND DebtAmount=0 AND CreditAmount=73.75 AND NetBalance=-73.75)
OR NOT EXISTS(SELECT 1 FROM dbo.v_AccountBalances WHERE AccountId=2 AND DebtAmount=1455 AND CreditAmount=0)
THROW 51999,N''Баланс рахунків'',1;');
    IF XACT_STATE()<>0 ROLLBACK;
END TRY
BEGIN CATCH
    SET @error4=ERROR_NUMBER();
    IF XACT_STATE()<>0 ROLLBACK;
END CATCH;
IF @error4<>0
BEGIN
    PRINT CONCAT(N'FAIL 04 Часткова оплата і аванс: ',@error4);
    THROW 51998,N'Перевірка не пройдена',1;
END;
PRINT N'PASS 04 Часткова оплата і аванс';
DECLARE @error5 int=0;
BEGIN TRY
    BEGIN TRAN;
    EXEC(N'EXEC dbo.usp_GenerateMonthlyCharges ''20261101'',1; IF (SELECT COUNT(*) FROM dbo.Charges)<>16 THROW 51999,N''Дубль нарахувань'',1;');
    IF XACT_STATE()<>0 ROLLBACK;
END TRY
BEGIN CATCH
    SET @error5=ERROR_NUMBER();
    IF XACT_STATE()<>0 ROLLBACK;
END CATCH;
IF @error5<>0
BEGIN
    PRINT CONCAT(N'FAIL 05 Повторний розрахунок без дублів: ',@error5);
    THROW 51998,N'Перевірка не пройдена',1;
END;
PRINT N'PASS 05 Повторний розрахунок без дублів';
DECLARE @error6 int=0;
BEGIN TRY
    BEGIN TRAN;
    EXEC(N'IF EXISTS(SELECT 1 FROM dbo.v_ChargeBalances WHERE AccountId=2 AND Period=''20261101'' AND PaidAmount<>0) THROW 51999,N''Порушення FIFO'',1;');
    IF XACT_STATE()<>0 ROLLBACK;
END TRY
BEGIN CATCH
    SET @error6=ERROR_NUMBER();
    IF XACT_STATE()<>0 ROLLBACK;
END CATCH;
IF @error6<>0
BEGIN
    PRINT CONCAT(N'FAIL 06 FIFO залишає борг за найстарішим незакритим рядком: ',@error6);
    THROW 51998,N'Перевірка не пройдена',1;
END;
PRINT N'PASS 06 FIFO залишає борг за найстарішим незакритим рядком';
DECLARE @error7 int=0;
BEGIN TRY
    BEGIN TRAN;
    EXEC(N'EXEC dbo.usp_GenerateMonthlyCharges ''20261201'',1;');
    IF XACT_STATE()<>0 ROLLBACK;
END TRY
BEGIN CATCH
    SET @error7=ERROR_NUMBER();
    IF XACT_STATE()<>0 ROLLBACK;
END CATCH;
IF @error7<>51034
BEGIN
    PRINT CONCAT(N'FAIL 07 Заборона відсутніх показань: ',@error7);
    THROW 51998,N'Перевірка не пройдена',1;
END;
PRINT N'PASS 07 Заборона відсутніх показань';
DECLARE @error8 int=0;
BEGIN TRY
    BEGIN TRAN;
    EXEC(N'IF EXISTS(SELECT 1 FROM dbo.Charges WHERE Period=''20261201'') THROW 51999,N''Часткове проведення'',1;');
    IF XACT_STATE()<>0 ROLLBACK;
END TRY
BEGIN CATCH
    SET @error8=ERROR_NUMBER();
    IF XACT_STATE()<>0 ROLLBACK;
END CATCH;
IF @error8<>0
BEGIN
    PRINT CONCAT(N'FAIL 08 Невдалий розрахунок не залишив часткових рядків: ',@error8);
    THROW 51998,N'Перевірка не пройдена',1;
END;
PRINT N'PASS 08 Невдалий розрахунок не залишив часткових рядків';
DECLARE @error9 int=0;
BEGIN TRY
    BEGIN TRAN;
    EXEC(N'INSERT dbo.Services(Name,Unit,CalculationMethod) VALUES(N''QA без тарифу'',N''місяць'',''Fixed''); DECLARE @s int=SCOPE_IDENTITY(); INSERT dbo.PremiseServices(PremiseId,ServiceId,ValidFrom) VALUES(1,@s,''20261101''); EXEC dbo.usp_GenerateMonthlyCharges ''20261101'',1;');
    IF XACT_STATE()<>0 ROLLBACK;
END TRY
BEGIN CATCH
    SET @error9=ERROR_NUMBER();
    IF XACT_STATE()<>0 ROLLBACK;
END CATCH;
IF @error9<>51033
BEGIN
    PRINT CONCAT(N'FAIL 09 Відсутній тариф зупиняє місяць: ',@error9);
    THROW 51998,N'Перевірка не пройдена',1;
END;
PRINT N'PASS 09 Відсутній тариф зупиняє місяць';
DECLARE @error10 int=0;
BEGIN TRY
    BEGIN TRAN;
    EXEC(N'INSERT dbo.Premises(BuildingId,Number,Kind,Area,ResidentsCount) VALUES(1,N''1'',''Apartment'',1,0);');
    IF XACT_STATE()<>0 ROLLBACK;
END TRY
BEGIN CATCH
    SET @error10=ERROR_NUMBER();
    IF XACT_STATE()<>0 ROLLBACK;
END CATCH;
IF @error10<>2627
BEGIN
    PRINT CONCAT(N'FAIL 10 Повторний номер приміщення: ',@error10);
    THROW 51998,N'Перевірка не пройдена',1;
END;
PRINT N'PASS 10 Повторний номер приміщення';
DECLARE @error11 int=0;
BEGIN TRY
    BEGIN TRAN;
    EXEC(N'INSERT dbo.Premises(BuildingId,Number,Kind,Area,ResidentsCount) VALUES(1,N''QA'',''Apartment'',-1,0);');
    IF XACT_STATE()<>0 ROLLBACK;
END TRY
BEGIN CATCH
    SET @error11=ERROR_NUMBER();
    IF XACT_STATE()<>0 ROLLBACK;
END CATCH;
IF @error11<>547
BEGIN
    PRINT CONCAT(N'FAIL 11 Від’ємна площа: ',@error11);
    THROW 51998,N'Перевірка не пройдена',1;
END;
PRINT N'PASS 11 Від’ємна площа';
DECLARE @error12 int=0;
BEGIN TRY
    BEGIN TRAN;
    EXEC(N'INSERT dbo.Tariffs(BuildingId,ServiceId,UnitPrice,ValidFrom) VALUES(1,1,9,''20261201'');');
    IF XACT_STATE()<>0 ROLLBACK;
END TRY
BEGIN CATCH
    SET @error12=ERROR_NUMBER();
    IF XACT_STATE()<>0 ROLLBACK;
END CATCH;
IF @error12<>51001
BEGIN
    PRINT CONCAT(N'FAIL 12 Перетин тарифів: ',@error12);
    THROW 51998,N'Перевірка не пройдена',1;
END;
PRINT N'PASS 12 Перетин тарифів';
DECLARE @error13 int=0;
BEGIN TRY
    BEGIN TRAN;
    EXEC(N'INSERT dbo.Accounts(AccountNumber,OwnerId,PremiseId,ValidFrom) VALUES(''QA'',2,1,''20261201'');');
    IF XACT_STATE()<>0 ROLLBACK;
END TRY
BEGIN CATCH
    SET @error13=ERROR_NUMBER();
    IF XACT_STATE()<>0 ROLLBACK;
END CATCH;
IF @error13<>51001
BEGIN
    PRINT CONCAT(N'FAIL 13 Перетин особових рахунків: ',@error13);
    THROW 51998,N'Перевірка не пройдена',1;
END;
PRINT N'PASS 13 Перетин особових рахунків';
DECLARE @error14 int=0;
BEGIN TRY
    BEGIN TRAN;
    EXEC(N'INSERT dbo.PremiseServices(PremiseId,ServiceId,ValidFrom) VALUES(1,1,''20261201'');');
    IF XACT_STATE()<>0 ROLLBACK;
END TRY
BEGIN CATCH
    SET @error14=ERROR_NUMBER();
    IF XACT_STATE()<>0 ROLLBACK;
END CATCH;
IF @error14<>51001
BEGIN
    PRINT CONCAT(N'FAIL 14 Перетин підключень: ',@error14);
    THROW 51998,N'Перевірка не пройдена',1;
END;
PRINT N'PASS 14 Перетин підключень';
DECLARE @error15 int=0;
BEGIN TRY
    BEGIN TRAN;
    EXEC(N'INSERT dbo.Meters(PremiseServiceId,SerialNumber,InstalledOn,InitialReading) VALUES(3,N''QA-METER'',''20261201'',0);');
    IF XACT_STATE()<>0 ROLLBACK;
END TRY
BEGIN CATCH
    SET @error15=ERROR_NUMBER();
    IF XACT_STATE()<>0 ROLLBACK;
END CATCH;
IF @error15<>51001
BEGIN
    PRINT CONCAT(N'FAIL 15 Другий активний лічильник: ',@error15);
    THROW 51998,N'Перевірка не пройдена',1;
END;
PRINT N'PASS 15 Другий активний лічильник';
DECLARE @error16 int=0;
BEGIN TRY
    BEGIN TRAN;
    EXEC(N'INSERT dbo.Meters(PremiseServiceId,SerialNumber,InstalledOn,InitialReading) VALUES(4,N''QA-FIXED'',''20261201'',0);');
    IF XACT_STATE()<>0 ROLLBACK;
END TRY
BEGIN CATCH
    SET @error16=ERROR_NUMBER();
    IF XACT_STATE()<>0 ROLLBACK;
END CATCH;
IF @error16<>51008
BEGIN
    PRINT CONCAT(N'FAIL 16 Лічильник для фіксованої послуги: ',@error16);
    THROW 51998,N'Перевірка не пройдена',1;
END;
PRINT N'PASS 16 Лічильник для фіксованої послуги';
DECLARE @error17 int=0;
BEGIN TRY
    BEGIN TRAN;
    EXEC(N'INSERT dbo.MeterReadings(MeterId,Period,ReadingDate,ReadingValue) VALUES(1,''20261201'',''20261231'',109);');
    IF XACT_STATE()<>0 ROLLBACK;
END TRY
BEGIN CATCH
    SET @error17=ERROR_NUMBER();
    IF XACT_STATE()<>0 ROLLBACK;
END CATCH;
IF @error17<>51013
BEGIN
    PRINT CONCAT(N'FAIL 17 Зменшення показань: ',@error17);
    THROW 51998,N'Перевірка не пройдена',1;
END;
PRINT N'PASS 17 Зменшення показань';
DECLARE @error18 int=0;
BEGIN TRY
    BEGIN TRAN;
    EXEC(N'UPDATE dbo.MeterReadings SET ReadingValue=105 WHERE MeterId=1 AND Period=''20261001'';');
    IF XACT_STATE()<>0 ROLLBACK;
END TRY
BEGIN CATCH
    SET @error18=ERROR_NUMBER();
    IF XACT_STATE()<>0 ROLLBACK;
END CATCH;
IF @error18<>51014
BEGIN
    PRINT CONCAT(N'FAIL 18 Редагування використаного показання: ',@error18);
    THROW 51998,N'Перевірка не пройдена',1;
END;
PRINT N'PASS 18 Редагування використаного показання';
DECLARE @error19 int=0;
BEGIN TRY
    BEGIN TRAN;
    EXEC(N'UPDATE dbo.Tariffs SET UnitPrice=99 WHERE TariffId=1;');
    IF XACT_STATE()<>0 ROLLBACK;
END TRY
BEGIN CATCH
    SET @error19=ERROR_NUMBER();
    IF XACT_STATE()<>0 ROLLBACK;
END CATCH;
IF @error19<>51004
BEGIN
    PRINT CONCAT(N'FAIL 19 Зміна використаного тарифу: ',@error19);
    THROW 51998,N'Перевірка не пройдена',1;
END;
PRINT N'PASS 19 Зміна використаного тарифу';
DECLARE @error20 int=0;
BEGIN TRY
    BEGIN TRAN;
    EXEC(N'UPDATE dbo.Charges SET Quantity=99 WHERE ChargeId=1;');
    IF XACT_STATE()<>0 ROLLBACK;
END TRY
BEGIN CATCH
    SET @error20=ERROR_NUMBER();
    IF XACT_STATE()<>0 ROLLBACK;
END CATCH;
IF @error20<>51015
BEGIN
    PRINT CONCAT(N'FAIL 20 Зміна проведеного нарахування: ',@error20);
    THROW 51998,N'Перевірка не пройдена',1;
END;
PRINT N'PASS 20 Зміна проведеного нарахування';
DECLARE @error21 int=0;
BEGIN TRY
    BEGIN TRAN;
    EXEC(N'UPDATE dbo.Payments SET Amount=1 WHERE PaymentId=1;');
    IF XACT_STATE()<>0 ROLLBACK;
END TRY
BEGIN CATCH
    SET @error21=ERROR_NUMBER();
    IF XACT_STATE()<>0 ROLLBACK;
END CATCH;
IF @error21<>51017
BEGIN
    PRINT CONCAT(N'FAIL 21 Зміна зареєстрованої оплати: ',@error21);
    THROW 51998,N'Перевірка не пройдена',1;
END;
PRINT N'PASS 21 Зміна зареєстрованої оплати';
DECLARE @error22 int=0;
BEGIN TRY
    BEGIN TRAN;
    EXEC(N'EXEC dbo.usp_RegisterPayment 1,''20261130'',10,N''QA-ROLE'',2;');
    IF XACT_STATE()<>0 ROLLBACK;
END TRY
BEGIN CATCH
    SET @error22=ERROR_NUMBER();
    IF XACT_STATE()<>0 ROLLBACK;
END CATCH;
IF @error22<>51031
BEGIN
    PRINT CONCAT(N'FAIL 22 Оплата від голови правління: ',@error22);
    THROW 51998,N'Перевірка не пройдена',1;
END;
PRINT N'PASS 22 Оплата від голови правління';
DECLARE @error23 int=0;
BEGIN TRY
    BEGIN TRAN;
    EXEC(N'EXEC dbo.usp_RegisterPayment 1,''20261130'',0,N''QA-ZERO'',1;');
    IF XACT_STATE()<>0 ROLLBACK;
END TRY
BEGIN CATCH
    SET @error23=ERROR_NUMBER();
    IF XACT_STATE()<>0 ROLLBACK;
END CATCH;
IF @error23<>547
BEGIN
    PRINT CONCAT(N'FAIL 23 Оплата з нульовою сумою: ',@error23);
    THROW 51998,N'Перевірка не пройдена',1;
END;
PRINT N'PASS 23 Оплата з нульовою сумою';
DECLARE @error24 int=0;
BEGIN TRY
    BEGIN TRAN;
    EXEC(N'EXEC dbo.usp_RegisterPayment 1,''20261130'',10,N''DEMO-PAY-001'',1;');
    IF XACT_STATE()<>0 ROLLBACK;
END TRY
BEGIN CATCH
    SET @error24=ERROR_NUMBER();
    IF XACT_STATE()<>0 ROLLBACK;
END CATCH;
IF @error24<>2627
BEGIN
    PRINT CONCAT(N'FAIL 24 Повторний документ оплати: ',@error24);
    THROW 51998,N'Перевірка не пройдена',1;
END;
PRINT N'PASS 24 Повторний документ оплати';
DECLARE @error25 int=0;
BEGIN TRY
    BEGIN TRAN;
    EXEC(N'INSERT dbo.PaymentAllocations(PaymentId,ChargeId,Amount) SELECT 3,MIN(ChargeId),1 FROM dbo.Charges WHERE AccountId=2;');
    IF XACT_STATE()<>0 ROLLBACK;
END TRY
BEGIN CATCH
    SET @error25=ERROR_NUMBER();
    IF XACT_STATE()<>0 ROLLBACK;
END CATCH;
IF @error25<>51020
BEGIN
    PRINT CONCAT(N'FAIL 25 Розподіл між чужими рахунками: ',@error25);
    THROW 51998,N'Перевірка не пройдена',1;
END;
PRINT N'PASS 25 Розподіл між чужими рахунками';
DECLARE @error26 int=0;
BEGIN TRY
    BEGIN TRAN;
    EXEC(N'DECLARE @c int=(SELECT MIN(ChargeId) FROM dbo.Charges WHERE AccountId=2 AND Period=''20261101''); INSERT dbo.PaymentAllocations VALUES(2,@c,1);');
    IF XACT_STATE()<>0 ROLLBACK;
END TRY
BEGIN CATCH
    SET @error26=ERROR_NUMBER();
    IF XACT_STATE()<>0 ROLLBACK;
END CATCH;
IF @error26<>51021
BEGIN
    PRINT CONCAT(N'FAIL 26 Перевищення суми оплати: ',@error26);
    THROW 51998,N'Перевірка не пройдена',1;
END;
PRINT N'PASS 26 Перевищення суми оплати';
DECLARE @error27 int=0;
BEGIN TRY
    BEGIN TRAN;
    EXEC(N'INSERT dbo.Payments(AccountId,PaymentDate,Amount,Reference,CreatedBy) VALUES(2,''20261130'',5000,N''QA-LARGE'',1); DECLARE @p int=SCOPE_IDENTITY(); DECLARE @c int=(SELECT MIN(ChargeId) FROM dbo.Charges WHERE AccountId=2); INSERT dbo.PaymentAllocations VALUES(@p,@c,161);');
    IF XACT_STATE()<>0 ROLLBACK;
END TRY
BEGIN CATCH
    SET @error27=ERROR_NUMBER();
    IF XACT_STATE()<>0 ROLLBACK;
END CATCH;
IF @error27<>51022
BEGIN
    PRINT CONCAT(N'FAIL 27 Перевищення суми нарахування: ',@error27);
    THROW 51998,N'Перевірка не пройдена',1;
END;
PRINT N'PASS 27 Перевищення суми нарахування';
DECLARE @error28 int=0;
BEGIN TRY
    BEGIN TRAN;
    EXEC(N'EXEC dbo.usp_GenerateMonthlyCharges ''20261102'',1;');
    IF XACT_STATE()<>0 ROLLBACK;
END TRY
BEGIN CATCH
    SET @error28=ERROR_NUMBER();
    IF XACT_STATE()<>0 ROLLBACK;
END CATCH;
IF @error28<>51032
BEGIN
    PRINT CONCAT(N'FAIL 28 Місяць починається з першого числа: ',@error28);
    THROW 51998,N'Перевірка не пройдена',1;
END;
PRINT N'PASS 28 Місяць починається з першого числа';
DECLARE @error29 int=0;
BEGIN TRY
    BEGIN TRAN;
    EXEC(N'INSERT dbo.MeterReadings(MeterId,Period,ReadingDate,ReadingValue) VALUES(1,''20261201'',''20261231'',112),(2,''20261201'',''20261231'',214);
EXEC dbo.usp_GenerateMonthlyCharges ''20261201'',1;
IF NOT EXISTS(SELECT 1 FROM dbo.v_AccountBalances WHERE AccountId=1 AND CreditAmount=0 AND DebtAmount=562.50)
THROW 51999,N''Не використано аванс'',1;');
    IF XACT_STATE()<>0 ROLLBACK;
END TRY
BEGIN CATCH
    SET @error29=ERROR_NUMBER();
    IF XACT_STATE()<>0 ROLLBACK;
END CATCH;
IF @error29<>0
BEGIN
    PRINT CONCAT(N'FAIL 29 Аванс автоматично покриває новий місяць: ',@error29);
    THROW 51998,N'Перевірка не пройдена',1;
END;
PRINT N'PASS 29 Аванс автоматично покриває новий місяць';
DECLARE @error30 int=0;
BEGIN TRY
    BEGIN TRAN;
    EXEC(N'UPDATE dbo.Accounts SET ValidTo=''20261201'' WHERE AccountId=2;
INSERT dbo.Accounts(AccountNumber,OwnerId,PremiseId,ValidFrom) VALUES(''QA-NEW-OWNER'',1,2,''20261201'');
IF NOT EXISTS(SELECT 1 FROM dbo.v_AccountBalances WHERE AccountId=2 AND OwnerId=2 AND DebtAmount=1455)
THROW 51999,N''Втрачено історію старого власника'',1;');
    IF XACT_STATE()<>0 ROLLBACK;
END TRY
BEGIN CATCH
    SET @error30=ERROR_NUMBER();
    IF XACT_STATE()<>0 ROLLBACK;
END CATCH;
IF @error30<>0
BEGIN
    PRINT CONCAT(N'FAIL 30 Зміна власника зберігає старі борги: ',@error30);
    THROW 51998,N'Перевірка не пройдена',1;
END;
PRINT N'PASS 30 Зміна власника зберігає старі борги';
DECLARE @error31 int=0;
BEGIN TRY
    BEGIN TRAN;
    EXEC(N'UPDATE dbo.Premises SET ResidentsCount=0 WHERE PremiseId=1;
INSERT dbo.MeterReadings(MeterId,Period,ReadingDate,ReadingValue) VALUES(1,''20261201'',''20261231'',110),(2,''20261201'',''20261231'',212);
EXEC dbo.usp_GenerateMonthlyCharges ''20261201'',1;
IF (SELECT COUNT(*) FROM dbo.Charges c JOIN dbo.PremiseServices ps ON ps.PremiseServiceId=c.PremiseServiceId
WHERE c.AccountId=1 AND c.Period=''20261201'' AND ps.ServiceId IN(2,3) AND c.Amount=0)<>2
THROW 51999,N''Нульові обсяги'',1;');
    IF XACT_STATE()<>0 ROLLBACK;
END TRY
BEGIN CATCH
    SET @error31=ERROR_NUMBER();
    IF XACT_STATE()<>0 ROLLBACK;
END CATCH;
IF @error31<>0
BEGIN
    PRINT CONCAT(N'FAIL 31 Нульове споживання та нуль мешканців дозволені: ',@error31);
    THROW 51998,N'Перевірка не пройдена',1;
END;
PRINT N'PASS 31 Нульове споживання та нуль мешканців дозволені';
DECLARE @error32 int=0;
BEGIN TRY
    BEGIN TRAN;
    EXEC(N'INSERT dbo.Buildings(Address) VALUES(N''QA будинок''); DECLARE @b int=SCOPE_IDENTITY(); UPDATE dbo.Premises SET BuildingId=@b WHERE PremiseId=1;');
    IF XACT_STATE()<>0 ROLLBACK;
END TRY
BEGIN CATCH
    SET @error32=ERROR_NUMBER();
    IF XACT_STATE()<>0 ROLLBACK;
END CATCH;
IF @error32<>51023
BEGIN
    PRINT CONCAT(N'FAIL 32 Перенесення приміщення з історією в інший будинок: ',@error32);
    THROW 51998,N'Перевірка не пройдена',1;
END;
PRINT N'PASS 32 Перенесення приміщення з історією в інший будинок';
DECLARE @error33 int=0;
BEGIN TRY
    BEGIN TRAN;
    EXEC(N'INSERT dbo.Services(Name,Unit,CalculationMethod) VALUES(N''QA округлення'',N''місяць'',''Fixed'');
DECLARE @s int=SCOPE_IDENTITY();
INSERT dbo.Tariffs(BuildingId,ServiceId,UnitPrice,ValidFrom) VALUES(1,@s,1.005,''20261101'');
INSERT dbo.PremiseServices(PremiseId,ServiceId,ValidFrom) VALUES(1,@s,''20261101'');
DECLARE @ps int=SCOPE_IDENTITY(); EXEC dbo.usp_GenerateMonthlyCharges ''20261101'',1;
IF NOT EXISTS(SELECT 1 FROM dbo.Charges WHERE PremiseServiceId=@ps AND Amount=1.01)
THROW 51999,N''Помилка округлення'',1;');
    IF XACT_STATE()<>0 ROLLBACK;
END TRY
BEGIN CATCH
    SET @error33=ERROR_NUMBER();
    IF XACT_STATE()<>0 ROLLBACK;
END CATCH;
IF @error33<>0
BEGIN
    PRINT CONCAT(N'FAIL 33 Округлення до копійки: ',@error33);
    THROW 51998,N'Перевірка не пройдена',1;
END;
PRINT N'PASS 33 Округлення до копійки';
PRINT N'Успішно: 33 перевірок. Демонстраційні дані збережено.';
