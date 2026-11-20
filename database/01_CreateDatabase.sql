-- ОСББ-Білінг. Індевич Олег, ІК-42. Структура БД.
-- Виконати цілком у SSMS або sqlcmd від імені адміністратора локального SQL Server.
-- Скрипт навмисно зупиняється, якщо база вже існує. DROP DATABASE відсутній.
USE master;
SET ANSI_NULLS ON; SET QUOTED_IDENTIFIER ON;
SET NOCOUNT ON;
IF DB_ID(N'OSBB_Billing') IS NOT NULL
    THROW 51000,N'OSBB_Billing вже існує. Скрипт її не перезаписує.',1;
EXEC(N'CREATE DATABASE OSBB_Billing COLLATE Ukrainian_100_CI_AS');
EXEC(N'USE OSBB_Billing; SET XACT_ABORT ON; BEGIN TRY BEGIN TRAN;
SET ANSI_NULLS ON; SET QUOTED_IDENTIFIER ON; SET ANSI_PADDING ON; SET ANSI_WARNINGS ON; SET ARITHABORT ON; SET CONCAT_NULL_YIELDS_NULL ON; SET NUMERIC_ROUNDABORT OFF;
CREATE TABLE dbo.Buildings (
    BuildingId int IDENTITY PRIMARY KEY,
    Address nvarchar(250) NOT NULL UNIQUE
);

CREATE TABLE dbo.Owners (
    OwnerId int IDENTITY PRIMARY KEY,
    FullName nvarchar(150) NOT NULL,
    Phone nvarchar(30) NULL,
    Email nvarchar(254) NULL
);

CREATE TABLE dbo.Premises (
    PremiseId int IDENTITY PRIMARY KEY,
    BuildingId int NOT NULL REFERENCES dbo.Buildings(BuildingId),
    Number nvarchar(20) NOT NULL,
    Kind varchar(12) NOT NULL CHECK (Kind IN (''Apartment'',''Nonres'')),
    Area decimal(10,2) NOT NULL CHECK (Area > 0),
    ResidentsCount smallint NOT NULL CHECK (ResidentsCount >= 0),
    CONSTRAINT UQ_Premises_Number UNIQUE (BuildingId, Number)
);

CREATE TABLE dbo.Accounts (
    AccountId int IDENTITY PRIMARY KEY,
    AccountNumber varchar(30) NOT NULL UNIQUE,
    OwnerId int NOT NULL REFERENCES dbo.Owners(OwnerId),
    PremiseId int NOT NULL REFERENCES dbo.Premises(PremiseId),
    ValidFrom date NOT NULL,
    ValidTo date NULL,
    CONSTRAINT CK_Accounts_Dates CHECK (DAY(ValidFrom)=1 AND (ValidTo IS NULL OR (DAY(ValidTo)=1 AND ValidTo>ValidFrom)))
);

CREATE TABLE dbo.Users (
    UserId int IDENTITY PRIMARY KEY,
    Login nvarchar(100) NOT NULL UNIQUE,
    PasswordHash nvarchar(512) NOT NULL,
    RoleCode varchar(12) NOT NULL CHECK (RoleCode IN (''Accountant'',''Chairperson'',''Owner'')),
    OwnerId int NULL REFERENCES dbo.Owners(OwnerId),
    IsActive bit NOT NULL DEFAULT 1,
    CONSTRAINT CK_Users_Owner CHECK ((RoleCode=''Owner'' AND OwnerId IS NOT NULL) OR (RoleCode<>''Owner'' AND OwnerId IS NULL))
);

CREATE TABLE dbo.Services (
    ServiceId int IDENTITY PRIMARY KEY,
    Name nvarchar(100) NOT NULL UNIQUE,
    Unit nvarchar(20) NOT NULL,
    CalculationMethod varchar(10) NOT NULL CHECK (CalculationMethod IN (''Area'',''Residents'',''Meter'',''Fixed''))
);

CREATE TABLE dbo.Tariffs (
    TariffId int IDENTITY PRIMARY KEY,
    BuildingId int NOT NULL REFERENCES dbo.Buildings(BuildingId),
    ServiceId int NOT NULL REFERENCES dbo.Services(ServiceId),
    UnitPrice decimal(18,4) NOT NULL CHECK (UnitPrice >= 0),
    ValidFrom date NOT NULL,
    ValidTo date NULL,
    CONSTRAINT UQ_Tariffs_Start UNIQUE (BuildingId,ServiceId,ValidFrom),
    CONSTRAINT CK_Tariffs_Dates CHECK (DAY(ValidFrom)=1 AND (ValidTo IS NULL OR (DAY(ValidTo)=1 AND ValidTo>ValidFrom)))
);

CREATE TABLE dbo.PremiseServices (
    PremiseServiceId int IDENTITY PRIMARY KEY,
    PremiseId int NOT NULL REFERENCES dbo.Premises(PremiseId),
    ServiceId int NOT NULL REFERENCES dbo.Services(ServiceId),
    ValidFrom date NOT NULL,
    ValidTo date NULL,
    CONSTRAINT UQ_PremiseServices_Start UNIQUE (PremiseId,ServiceId,ValidFrom),
    CONSTRAINT CK_PremiseServices_Dates CHECK (DAY(ValidFrom)=1 AND (ValidTo IS NULL OR (DAY(ValidTo)=1 AND ValidTo>ValidFrom)))
);

CREATE TABLE dbo.Meters (
    MeterId int IDENTITY PRIMARY KEY,
    PremiseServiceId int NOT NULL REFERENCES dbo.PremiseServices(PremiseServiceId),
    SerialNumber nvarchar(50) NOT NULL UNIQUE,
    InstalledOn date NOT NULL,
    RemovedOn date NULL,
    InitialReading decimal(18,3) NOT NULL CHECK (InitialReading >= 0),
    CONSTRAINT CK_Meters_Dates CHECK (DAY(InstalledOn)=1 AND (RemovedOn IS NULL OR (DAY(RemovedOn)=1 AND RemovedOn>InstalledOn)))
);

CREATE TABLE dbo.MeterReadings (
    MeterReadingId int IDENTITY PRIMARY KEY,
    MeterId int NOT NULL REFERENCES dbo.Meters(MeterId),
    Period date NOT NULL CHECK (DAY(Period)=1),
    ReadingDate date NOT NULL,
    ReadingValue decimal(18,3) NOT NULL CHECK (ReadingValue >= 0),
    CONSTRAINT UQ_MeterReadings_Period UNIQUE (MeterId,Period),
    CONSTRAINT CK_MeterReadings_Date CHECK (ReadingDate>=Period AND ReadingDate<=EOMONTH(Period))
);

CREATE TABLE dbo.Charges (
    ChargeId int IDENTITY PRIMARY KEY,
    AccountId int NOT NULL REFERENCES dbo.Accounts(AccountId),
    PremiseServiceId int NOT NULL REFERENCES dbo.PremiseServices(PremiseServiceId),
    TariffId int NOT NULL REFERENCES dbo.Tariffs(TariffId),
    Period date NOT NULL CHECK (DAY(Period)=1),
    Quantity decimal(18,3) NOT NULL CHECK (Quantity >= 0),
    UnitPrice decimal(18,4) NOT NULL CHECK (UnitPrice >= 0),
    Amount AS CONVERT(decimal(19,2),ROUND(Quantity*UnitPrice,2)) PERSISTED,
    CreatedAt datetime2(0) NOT NULL DEFAULT SYSUTCDATETIME(),
    CreatedBy int NOT NULL REFERENCES dbo.Users(UserId),
    CONSTRAINT UQ_Charges_Month UNIQUE (AccountId,PremiseServiceId,Period)
);

CREATE TABLE dbo.Payments (
    PaymentId int IDENTITY PRIMARY KEY,
    AccountId int NOT NULL REFERENCES dbo.Accounts(AccountId),
    PaymentDate date NOT NULL,
    Amount decimal(19,2) NOT NULL CHECK (Amount > 0),
    Reference nvarchar(100) NOT NULL UNIQUE,
    CreatedAt datetime2(0) NOT NULL DEFAULT SYSUTCDATETIME(),
    CreatedBy int NOT NULL REFERENCES dbo.Users(UserId)
);

CREATE TABLE dbo.PaymentAllocations (
    PaymentId int NOT NULL REFERENCES dbo.Payments(PaymentId),
    ChargeId int NOT NULL REFERENCES dbo.Charges(ChargeId),
    Amount decimal(19,2) NOT NULL CHECK (Amount > 0),
    CONSTRAINT PK_PaymentAllocations PRIMARY KEY (PaymentId,ChargeId)
);

CREATE INDEX IX_Accounts_Owner ON dbo.Accounts(OwnerId);
CREATE INDEX IX_Accounts_PremiseDates ON dbo.Accounts(PremiseId,ValidFrom,ValidTo);
CREATE UNIQUE INDEX UX_Users_Owner ON dbo.Users(OwnerId) WHERE OwnerId IS NOT NULL;
CREATE INDEX IX_Tariffs_Lookup ON dbo.Tariffs(BuildingId,ServiceId,ValidFrom,ValidTo) INCLUDE(UnitPrice);
CREATE INDEX IX_PremiseServices_Interval ON dbo.PremiseServices(PremiseId,ServiceId,ValidFrom,ValidTo);
CREATE INDEX IX_Meters_Interval ON dbo.Meters(PremiseServiceId,InstalledOn,RemovedOn);
CREATE INDEX IX_Charges_Period ON dbo.Charges(Period,AccountId) INCLUDE(Amount);
CREATE INDEX IX_Charges_Tariff ON dbo.Charges(TariffId);
CREATE INDEX IX_Charges_Service ON dbo.Charges(PremiseServiceId,Period);
CREATE INDEX IX_Payments_AccountDate ON dbo.Payments(AccountId,PaymentDate,PaymentId) INCLUDE(Amount);
CREATE INDEX IX_Allocations_Charge ON dbo.PaymentAllocations(ChargeId) INCLUDE(Amount);


EXEC(N''CREATE TRIGGER dbo.tr_Accounts_Intervals ON dbo.Accounts AFTER INSERT,UPDATE AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @lock bigint;
    SELECT @lock=COUNT_BIG(*) FROM dbo.Accounts WITH (TABLOCKX,HOLDLOCK);
    IF EXISTS (SELECT 1 FROM dbo.Accounts a JOIN dbo.Accounts b ON a.PremiseId=b.PremiseId
        AND a.AccountId<b.AccountId AND a.ValidFrom<ISNULL(b.ValidTo,''''99991231'''')
        AND b.ValidFrom<ISNULL(a.ValidTo,''''99991231''''))
        THROW 51001,N''''Інтервали дії не повинні перетинатися.'''',1;
    IF EXISTS (SELECT 1 FROM inserted i JOIN deleted d ON d.AccountId=i.AccountId
        WHERE (i.OwnerId<>d.OwnerId OR i.PremiseId<>d.PremiseId OR i.ValidFrom<>d.ValidFrom)
        AND (EXISTS(SELECT 1 FROM dbo.Charges c WHERE c.AccountId=i.AccountId)
          OR EXISTS(SELECT 1 FROM dbo.Payments p WHERE p.AccountId=i.AccountId)))
        THROW 51002,N''''Не змінюйте платника рахунку з фінансовою історією. Створіть новий рахунок.'''',1;
        IF EXISTS (SELECT 1 FROM inserted i JOIN dbo.Charges c ON c.AccountId=i.AccountId
        WHERE c.Period<i.ValidFrom OR c.Period>=ISNULL(i.ValidTo,''''99991231''''))
        THROW 51003,N''''Інтервал рахунку виключає існуючі нарахування.'''',1;
END;'');

EXEC(N''CREATE TRIGGER dbo.tr_Tariffs_Intervals ON dbo.Tariffs AFTER INSERT,UPDATE AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @lock bigint;
    SELECT @lock=COUNT_BIG(*) FROM dbo.Tariffs WITH (TABLOCKX,HOLDLOCK);
    IF EXISTS (SELECT 1 FROM dbo.Tariffs a JOIN dbo.Tariffs b ON a.BuildingId=b.BuildingId AND a.ServiceId=b.ServiceId
        AND a.TariffId<b.TariffId AND a.ValidFrom<ISNULL(b.ValidTo,''''99991231'''')
        AND b.ValidFrom<ISNULL(a.ValidTo,''''99991231''''))
        THROW 51001,N''''Інтервали дії не повинні перетинатися.'''',1;
    IF EXISTS (SELECT 1 FROM inserted i JOIN deleted d ON i.TariffId=d.TariffId
        JOIN dbo.Charges c ON c.TariffId=i.TariffId
        WHERE i.BuildingId<>d.BuildingId OR i.ServiceId<>d.ServiceId OR i.UnitPrice<>d.UnitPrice
          OR i.ValidFrom<>d.ValidFrom OR c.Period>=ISNULL(i.ValidTo,''''99991231''''))
        THROW 51004,N''''Використаний тариф не можна змінювати; додайте нову версію.'''',1;
END;'');

EXEC(N''CREATE TRIGGER dbo.tr_PremiseServices_Intervals ON dbo.PremiseServices AFTER INSERT,UPDATE AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @lock bigint;
    SELECT @lock=COUNT_BIG(*) FROM dbo.PremiseServices WITH (TABLOCKX,HOLDLOCK);
    IF EXISTS (SELECT 1 FROM dbo.PremiseServices a JOIN dbo.PremiseServices b ON a.PremiseId=b.PremiseId AND a.ServiceId=b.ServiceId
        AND a.PremiseServiceId<b.PremiseServiceId AND a.ValidFrom<ISNULL(b.ValidTo,''''99991231'''')
        AND b.ValidFrom<ISNULL(a.ValidTo,''''99991231''''))
        THROW 51001,N''''Інтервали дії не повинні перетинатися.'''',1;
    IF EXISTS (SELECT 1 FROM inserted i JOIN deleted d ON i.PremiseServiceId=d.PremiseServiceId
        WHERE (i.PremiseId<>d.PremiseId OR i.ServiceId<>d.ServiceId OR i.ValidFrom<>d.ValidFrom)
        AND (EXISTS(SELECT 1 FROM dbo.Charges c WHERE c.PremiseServiceId=i.PremiseServiceId)
          OR EXISTS(SELECT 1 FROM dbo.Meters m WHERE m.PremiseServiceId=i.PremiseServiceId)))
        THROW 51005,N''''Використане підключення не можна перепризначати.'''',1;
        IF EXISTS (SELECT 1 FROM inserted i JOIN dbo.Charges c ON c.PremiseServiceId=i.PremiseServiceId
        WHERE c.Period<i.ValidFrom OR c.Period>=ISNULL(i.ValidTo,''''99991231''''))
        THROW 51006,N''''Інтервал послуги виключає нарахування.'''',1;
        IF EXISTS (SELECT 1 FROM inserted i JOIN dbo.Meters m ON m.PremiseServiceId=i.PremiseServiceId
        WHERE m.InstalledOn<i.ValidFrom OR (i.ValidTo IS NOT NULL AND (m.RemovedOn IS NULL OR m.RemovedOn>i.ValidTo)))
        THROW 51007,N''''Лічильник виходить за інтервал підключення послуги.'''',1;
END;'');

EXEC(N''CREATE TRIGGER dbo.tr_Meters_Intervals ON dbo.Meters AFTER INSERT,UPDATE AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @lock bigint;
    SELECT @lock=COUNT_BIG(*) FROM dbo.Meters WITH (TABLOCKX,HOLDLOCK);
    IF EXISTS (SELECT 1 FROM dbo.Meters a JOIN dbo.Meters b ON a.PremiseServiceId=b.PremiseServiceId
        AND a.MeterId<b.MeterId AND a.InstalledOn<ISNULL(b.RemovedOn,''''99991231'''')
        AND b.InstalledOn<ISNULL(a.RemovedOn,''''99991231''''))
        THROW 51001,N''''Інтервали дії не повинні перетинатися.'''',1;
    IF EXISTS (SELECT 1 FROM inserted i JOIN dbo.PremiseServices ps ON ps.PremiseServiceId=i.PremiseServiceId
        JOIN dbo.Services s ON s.ServiceId=ps.ServiceId
        WHERE s.CalculationMethod<>''''Meter'''' OR i.InstalledOn<ps.ValidFrom
        OR (ps.ValidTo IS NOT NULL AND (i.RemovedOn IS NULL OR i.RemovedOn>ps.ValidTo)))
        THROW 51008,N''''Лічильник дозволено лише для чинної послуги з методом Meter.'''',1;
        IF EXISTS (SELECT 1 FROM inserted i JOIN deleted d ON d.MeterId=i.MeterId
        WHERE (i.PremiseServiceId<>d.PremiseServiceId OR i.InstalledOn<>d.InstalledOn OR i.InitialReading<>d.InitialReading)
        AND EXISTS(SELECT 1 FROM dbo.MeterReadings r WHERE r.MeterId=i.MeterId))
        THROW 51009,N''''Початкові дані лічильника з показаннями незмінні.'''',1;
        IF EXISTS (SELECT 1 FROM inserted i JOIN dbo.MeterReadings r ON r.MeterId=i.MeterId
        WHERE r.Period>=ISNULL(i.RemovedOn,''''99991231''''))
        THROW 51010,N''''Дата зняття лічильника виключає існуючі показання.'''',1;
END;'');

EXEC(N''CREATE TRIGGER dbo.tr_Premises_Building ON dbo.Premises AFTER UPDATE AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (SELECT 1 FROM inserted i JOIN deleted d ON d.PremiseId=i.PremiseId
        WHERE i.BuildingId<>d.BuildingId AND
        (EXISTS(SELECT 1 FROM dbo.Accounts a WHERE a.PremiseId=i.PremiseId)
        OR EXISTS(SELECT 1 FROM dbo.PremiseServices ps WHERE ps.PremiseId=i.PremiseId)))
        THROW 51023,N''''Приміщення з обліковою історією не можна переносити до іншого будинку.'''',1;
END;'');

EXEC(N''CREATE TRIGGER dbo.tr_Services_Method ON dbo.Services AFTER UPDATE AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (SELECT 1 FROM inserted i JOIN deleted d ON d.ServiceId=i.ServiceId
        WHERE (i.CalculationMethod<>d.CalculationMethod OR i.Unit<>d.Unit)
        AND EXISTS(SELECT 1 FROM dbo.PremiseServices ps WHERE ps.ServiceId=i.ServiceId))
        THROW 51011,N''''Для нового методу або одиниці створіть окрему послугу.'''',1;
END;'');

EXEC(N''CREATE TRIGGER dbo.tr_Readings_Validate ON dbo.MeterReadings AFTER INSERT,UPDATE,DELETE AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @lock bigint;
    SELECT @lock=COUNT_BIG(*) FROM dbo.MeterReadings WITH (TABLOCKX,HOLDLOCK);
    IF EXISTS (SELECT 1 FROM inserted i JOIN dbo.Meters m ON m.MeterId=i.MeterId
        WHERE i.Period<m.InstalledOn OR i.Period>=ISNULL(m.RemovedOn,''''99991231'''')
        OR i.ReadingValue<m.InitialReading)
        THROW 51012,N''''Показання не відповідає інтервалу або початковому значенню лічильника.'''',1;
    IF EXISTS (SELECT 1 FROM dbo.MeterReadings a JOIN dbo.MeterReadings b
        ON a.MeterId=b.MeterId AND a.Period<b.Period WHERE a.ReadingValue>b.ReadingValue)
        THROW 51013,N''''Поточне накопичувальне показання не може бути меншим за попереднє.'''',1;
    IF EXISTS (SELECT 1 FROM deleted d JOIN dbo.Meters m ON m.MeterId=d.MeterId
        JOIN dbo.Charges c ON c.PremiseServiceId=m.PremiseServiceId
        AND c.Period IN (d.Period,DATEADD(month,1,d.Period)))
        THROW 51014,N''''Показання, що вже використані в нарахуванні, незмінні.'''',1;
END;'');

EXEC(N''CREATE TRIGGER dbo.tr_Charges_Validate ON dbo.Charges AFTER INSERT,UPDATE,DELETE AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS(SELECT 1 FROM deleted)
        THROW 51015,N''''Проведені нарахування незмінні.'''',1;
    IF EXISTS (SELECT 1 FROM inserted c
        JOIN dbo.Accounts a ON a.AccountId=c.AccountId
        JOIN dbo.PremiseServices ps ON ps.PremiseServiceId=c.PremiseServiceId
        JOIN dbo.Premises p ON p.PremiseId=ps.PremiseId
        JOIN dbo.Tariffs t ON t.TariffId=c.TariffId
        JOIN dbo.Users u ON u.UserId=c.CreatedBy
        WHERE a.PremiseId<>ps.PremiseId OR t.BuildingId<>p.BuildingId OR t.ServiceId<>ps.ServiceId
        OR c.UnitPrice<>t.UnitPrice OR c.Period<a.ValidFrom OR c.Period>=ISNULL(a.ValidTo,''''99991231'''')
        OR c.Period<ps.ValidFrom OR c.Period>=ISNULL(ps.ValidTo,''''99991231'''')
        OR c.Period<t.ValidFrom OR c.Period>=ISNULL(t.ValidTo,''''99991231'''')
        OR u.RoleCode<>''''Accountant'''' OR u.IsActive=0)
        THROW 51016,N''''Неправильний рахунок, тариф, місяць або автор нарахування.'''',1;
END;'');

EXEC(N''CREATE TRIGGER dbo.tr_Payments_Validate ON dbo.Payments AFTER INSERT,UPDATE,DELETE AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS(SELECT 1 FROM deleted)
        THROW 51017,N''''Зареєстровані оплати незмінні.'''',1;
    IF EXISTS(SELECT 1 FROM inserted p JOIN dbo.Users u ON u.UserId=p.CreatedBy
        WHERE u.RoleCode<>''''Accountant'''' OR u.IsActive=0)
        THROW 51018,N''''Оплати реєструє активний бухгалтер.'''',1;
END;'');

EXEC(N''CREATE TRIGGER dbo.tr_Allocations_Validate ON dbo.PaymentAllocations AFTER INSERT,UPDATE,DELETE AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS(SELECT 1 FROM deleted)
        THROW 51019,N''''Проведені розподіли оплат незмінні.'''',1;
    DECLARE @lock bigint;
    SELECT @lock=COUNT_BIG(*) FROM dbo.Payments WITH (TABLOCKX,HOLDLOCK);
    SELECT @lock=COUNT_BIG(*) FROM dbo.Charges WITH (TABLOCKX,HOLDLOCK);
    IF EXISTS(SELECT 1 FROM inserted i JOIN dbo.Payments p ON p.PaymentId=i.PaymentId
        JOIN dbo.Charges c ON c.ChargeId=i.ChargeId WHERE p.AccountId<>c.AccountId)
        THROW 51020,N''''Оплата і нарахування повинні належати одному особовому рахунку.'''',1;
    IF EXISTS(SELECT 1 FROM dbo.PaymentAllocations a JOIN dbo.Payments p ON p.PaymentId=a.PaymentId
        GROUP BY p.PaymentId,p.Amount HAVING SUM(a.Amount)>p.Amount)
        THROW 51021,N''''Розподілена сума перевищує суму оплати.'''',1;
    IF EXISTS(SELECT 1 FROM dbo.PaymentAllocations a JOIN dbo.Charges c ON c.ChargeId=a.ChargeId
        GROUP BY c.ChargeId,c.Amount HAVING SUM(a.Amount)>c.Amount)
        THROW 51022,N''''Розподілена сума перевищує суму нарахування.'''',1;
END;'');

EXEC(N''CREATE VIEW dbo.v_ChargeBalances AS
SELECT c.ChargeId,c.AccountId,c.PremiseServiceId,c.TariffId,c.Period,c.Quantity,c.UnitPrice,c.Amount,
    CONVERT(decimal(19,2),ISNULL(a.PaidAmount,0)) PaidAmount,
    CONVERT(decimal(19,2),c.Amount-ISNULL(a.PaidAmount,0)) DebtAmount
FROM dbo.Charges c LEFT JOIN
    (SELECT ChargeId,SUM(Amount) PaidAmount FROM dbo.PaymentAllocations GROUP BY ChargeId) a
    ON a.ChargeId=c.ChargeId;'');

EXEC(N''CREATE VIEW dbo.v_AccountBalances AS
SELECT a.AccountId,a.AccountNumber,a.OwnerId,a.PremiseId,
    CONVERT(decimal(19,2),ISNULL(c.TotalCharged,0)) TotalCharged,
    CONVERT(decimal(19,2),ISNULL(p.TotalPaid,0)) TotalPaid,
    CONVERT(decimal(19,2),ISNULL(c.TotalDebt,0)) DebtAmount,
    CONVERT(decimal(19,2),ISNULL(p.TotalPaid,0)-ISNULL(c.Allocated,0)) CreditAmount,
    CONVERT(decimal(19,2),ISNULL(c.TotalCharged,0)-ISNULL(p.TotalPaid,0)) NetBalance
FROM dbo.Accounts a
LEFT JOIN (SELECT AccountId,SUM(Amount) TotalCharged,SUM(DebtAmount) TotalDebt,
    SUM(PaidAmount) Allocated FROM dbo.v_ChargeBalances GROUP BY AccountId) c ON c.AccountId=a.AccountId
LEFT JOIN (SELECT AccountId,SUM(Amount) TotalPaid FROM dbo.Payments GROUP BY AccountId) p ON p.AccountId=a.AccountId;'');

EXEC(N''CREATE VIEW dbo.v_InvoiceLines AS
SELECT cb.*,a.AccountNumber,a.OwnerId,o.FullName,p.BuildingId,b.Address,p.Number PremiseNumber,
    s.ServiceId,s.Name ServiceName,s.Unit,s.CalculationMethod
FROM dbo.v_ChargeBalances cb JOIN dbo.Accounts a ON a.AccountId=cb.AccountId
JOIN dbo.Owners o ON o.OwnerId=a.OwnerId JOIN dbo.Premises p ON p.PremiseId=a.PremiseId
JOIN dbo.Buildings b ON b.BuildingId=p.BuildingId
JOIN dbo.PremiseServices ps ON ps.PremiseServiceId=cb.PremiseServiceId
JOIN dbo.Services s ON s.ServiceId=ps.ServiceId;'');

EXEC(N''CREATE PROCEDURE dbo.usp_ApplyPayments @AccountId int AS
BEGIN
    SET NOCOUNT ON; SET XACT_ABORT ON;
    BEGIN TRY
    BEGIN TRAN;
    DECLARE @lr int;
    EXEC @lr=sys.sp_getapplock @Resource=N''''OSBB_Finance'''',@LockMode=''''Exclusive'''',
        @LockOwner=''''Transaction'''',@LockTimeout=10000;
    IF @lr<0 THROW 51030,N''''Не вдалося отримати блокування розрахунків. Повторіть операцію.'''',1;
    DECLARE @p int,@c int,@free decimal(19,2),@debt decimal(19,2),@part decimal(19,2);
    WHILE 1=1
    BEGIN
        SET @p=NULL; SET @c=NULL;
        SELECT TOP(1) @p=p.PaymentId,@free=p.Amount-ISNULL(x.Allocated,0)
        FROM dbo.Payments p OUTER APPLY
            (SELECT SUM(Amount) Allocated FROM dbo.PaymentAllocations a WHERE a.PaymentId=p.PaymentId) x
        WHERE p.AccountId=@AccountId AND p.Amount>ISNULL(x.Allocated,0)
        ORDER BY p.PaymentDate,p.PaymentId;
        SELECT TOP(1) @c=ChargeId,@debt=DebtAmount FROM dbo.v_ChargeBalances
        WHERE AccountId=@AccountId AND DebtAmount>0 ORDER BY Period,ChargeId;
        IF @p IS NULL OR @c IS NULL BREAK;
        SET @part=CASE WHEN @free<@debt THEN @free ELSE @debt END;
        INSERT dbo.PaymentAllocations(PaymentId,ChargeId,Amount) VALUES(@p,@c,@part);
    END;
    COMMIT;
    END TRY
    BEGIN CATCH
        IF XACT_STATE()<>0 ROLLBACK;
        THROW;
    END CATCH;
END;'');

EXEC(N''CREATE PROCEDURE dbo.usp_RegisterPayment
    @AccountId int,@PaymentDate date,@Amount decimal(19,2),@Reference nvarchar(100),@CreatedBy int AS
BEGIN
    SET NOCOUNT ON; SET XACT_ABORT ON;
    BEGIN TRY
    BEGIN TRAN;
    DECLARE @lr int;
    EXEC @lr=sys.sp_getapplock @Resource=N''''OSBB_Finance'''',@LockMode=''''Exclusive'''',
        @LockOwner=''''Transaction'''',@LockTimeout=10000;
    IF @lr<0 THROW 51030,N''''Не вдалося отримати блокування розрахунків. Повторіть операцію.'''',1;
    IF NOT EXISTS(SELECT 1 FROM dbo.Users WHERE UserId=@CreatedBy AND RoleCode=''''Accountant'''' AND IsActive=1)
        THROW 51031,N''''Операція дозволена активному бухгалтеру.'''',1;
    INSERT dbo.Payments(AccountId,PaymentDate,Amount,Reference,CreatedBy)
        VALUES(@AccountId,@PaymentDate,@Amount,@Reference,@CreatedBy);
    EXEC dbo.usp_ApplyPayments @AccountId;
    COMMIT;
    END TRY
    BEGIN CATCH
        IF XACT_STATE()<>0 ROLLBACK;
        THROW;
    END CATCH;
END;'');

EXEC(N''CREATE PROCEDURE dbo.usp_GenerateMonthlyCharges @Period date,@CreatedBy int AS
BEGIN
    SET NOCOUNT ON; SET XACT_ABORT ON;
    BEGIN TRY
    BEGIN TRAN;
    DECLARE @lr int;
    EXEC @lr=sys.sp_getapplock @Resource=N''''OSBB_Finance'''',@LockMode=''''Exclusive'''',
        @LockOwner=''''Transaction'''',@LockTimeout=10000;
    IF @lr<0 THROW 51030,N''''Не вдалося отримати блокування розрахунків. Повторіть операцію.'''',1;
    IF @Period IS NULL OR DAY(@Period)<>1 THROW 51032,N''''Період повинен бути першим числом місяця.'''',1;
    IF NOT EXISTS(SELECT 1 FROM dbo.Users WHERE UserId=@CreatedBy AND RoleCode=''''Accountant'''' AND IsActive=1)
        THROW 51031,N''''Операція дозволена активному бухгалтеру.'''',1;
    SELECT ps.PremiseServiceId,p.PremiseId,a.AccountId,t.TariffId,t.UnitPrice,s.CalculationMethod,
        p.Area,p.ResidentsCount,m.MeterId,m.InstalledOn,m.InitialReading,
        r.ReadingValue,prev.ReadingValue PreviousReading
    INTO #Basis FROM dbo.PremiseServices ps
    JOIN dbo.Premises p ON p.PremiseId=ps.PremiseId
    JOIN dbo.Services s ON s.ServiceId=ps.ServiceId
    LEFT JOIN dbo.Accounts a ON a.PremiseId=p.PremiseId
        AND @Period>=a.ValidFrom AND @Period<ISNULL(a.ValidTo,''''99991231'''')
    LEFT JOIN dbo.Tariffs t ON t.BuildingId=p.BuildingId AND t.ServiceId=ps.ServiceId
        AND @Period>=t.ValidFrom AND @Period<ISNULL(t.ValidTo,''''99991231'''')
    LEFT JOIN dbo.Meters m ON m.PremiseServiceId=ps.PremiseServiceId
        AND @Period>=m.InstalledOn AND @Period<ISNULL(m.RemovedOn,''''99991231'''')
    LEFT JOIN dbo.MeterReadings r ON r.MeterId=m.MeterId AND r.Period=@Period
    LEFT JOIN dbo.MeterReadings prev ON prev.MeterId=m.MeterId AND prev.Period=DATEADD(month,-1,@Period)
    WHERE @Period>=ps.ValidFrom AND @Period<ISNULL(ps.ValidTo,''''99991231'''')
        AND NOT EXISTS(SELECT 1 FROM dbo.Charges c JOIN dbo.Accounts ca ON ca.AccountId=c.AccountId
            WHERE ca.PremiseId=p.PremiseId AND c.PremiseServiceId=ps.PremiseServiceId AND c.Period=@Period);
    IF EXISTS(SELECT 1 FROM #Basis WHERE AccountId IS NULL OR TariffId IS NULL)
        THROW 51033,N''''Для активної послуги відсутній особовий рахунок або тариф.'''',1;
    IF EXISTS(SELECT 1 FROM #Basis WHERE CalculationMethod=''''Meter'''' AND
        (MeterId IS NULL OR ReadingValue IS NULL OR (PreviousReading IS NULL AND InstalledOn<>@Period)))
        THROW 51034,N''''Відсутній лічильник, поточне або попереднє місячне показання.'''',1;
    INSERT dbo.Charges(AccountId,PremiseServiceId,TariffId,Period,Quantity,UnitPrice,CreatedBy)
    SELECT AccountId,PremiseServiceId,TariffId,@Period,
        CASE CalculationMethod WHEN ''''Area'''' THEN Area WHEN ''''Residents'''' THEN ResidentsCount
        WHEN ''''Fixed'''' THEN 1 WHEN ''''Meter'''' THEN ReadingValue-ISNULL(PreviousReading,InitialReading) END,
        UnitPrice,@CreatedBy FROM #Basis;
    DECLARE @account int;
    DECLARE accounts_cursor CURSOR LOCAL FAST_FORWARD FOR SELECT AccountId FROM dbo.Accounts;
    OPEN accounts_cursor; FETCH NEXT FROM accounts_cursor INTO @account;
    WHILE @@FETCH_STATUS=0
    BEGIN
        EXEC dbo.usp_ApplyPayments @account;
        FETCH NEXT FROM accounts_cursor INTO @account;
    END;
    CLOSE accounts_cursor; DEALLOCATE accounts_cursor;
    COMMIT;
    END TRY
    BEGIN CATCH
        IF XACT_STATE()<>0 ROLLBACK;
        THROW;
    END CATCH;
END;'');
COMMIT; END TRY BEGIN CATCH IF XACT_STATE()<>0 ROLLBACK; THROW; END CATCH;');
PRINT N'OSBB_Billing створено успішно.';
