-- Лише вигадані навчальні дані. Скрипт відмовляється змінювати вже заповнену базу.
USE OSBB_Billing;
SET NOCOUNT ON;
SET ANSI_NULLS ON; SET QUOTED_IDENTIFIER ON; SET ANSI_PADDING ON; SET ANSI_WARNINGS ON; SET ARITHABORT ON; SET CONCAT_NULL_YIELDS_NULL ON; SET NUMERIC_ROUNDABORT OFF;

SET XACT_ABORT ON;
IF EXISTS(SELECT 1 FROM dbo.Buildings) OR EXISTS(SELECT 1 FROM dbo.Users)
    THROW 51100,N'База вже містить дані. Повторне наповнення заборонено.',1;
BEGIN TRY
BEGIN TRAN;
INSERT dbo.Buildings(Address) VALUES(N'м. Київ, вул. Навчальна, 10');
INSERT dbo.Owners(FullName,Phone,Email) VALUES
    (N'Демонстраційний власник 1',NULL,N'owner1@example.test'),
    (N'Демонстраційний власник 2',NULL,N'owner2@example.test');
INSERT dbo.Premises(BuildingId,Number,Kind,Area,ResidentsCount) VALUES
    (1,N'1','Apartment',52.50,2),(1,N'2','Apartment',70.00,3);
INSERT dbo.Accounts(AccountNumber,OwnerId,PremiseId,ValidFrom) VALUES
    ('OSBB-0001',1,1,'20261001'),('OSBB-0002',2,2,'20261001');
INSERT dbo.Users(Login,PasswordHash,RoleCode,OwnerId) VALUES(N'accountant',N'AQAAAAIAAYagAAAAEDSmGzLZSdleRT7yGWF5HbQgSX9rrN+Jj4r1QGMvF5Pg/qE5sX49Brd1uEfmgDqnqA==','Accountant',NULL);
INSERT dbo.Users(Login,PasswordHash,RoleCode,OwnerId) VALUES(N'chair',N'AQAAAAIAAYagAAAAEFglrbnazlj75zO2ZWqfjaUQ3/28ACW/v69I5qLkX/OXasH3bx7anvb0KIVxeE+PIg==','Chairperson',NULL);
INSERT dbo.Users(Login,PasswordHash,RoleCode,OwnerId) VALUES(N'owner1',N'AQAAAAIAAYagAAAAEO4RHzi0UqkDHl0yWXoMIDyMSx0gRQ3L2NAEbvvdbz6NKBtYK/KOZVtGiKsOLsnaig==','Owner',1);
INSERT dbo.Users(Login,PasswordHash,RoleCode,OwnerId) VALUES(N'owner2',N'AQAAAAIAAYagAAAAEHizpHopa1XtH0wcwFnKWwRKMNJwAazoQWEKSac2agQxQ/We4ySr3G71DVden4GLJw==','Owner',2);
INSERT dbo.Services(Name,Unit,CalculationMethod) VALUES
    (N'Утримання будинку',N'м²','Area'),(N'Вивезення відходів',N'особа','Residents'),
    (N'Холодна вода',N'м³','Meter'),(N'Обслуговування домофона',N'місяць','Fixed');
INSERT dbo.Tariffs(BuildingId,ServiceId,UnitPrice,ValidFrom,ValidTo) VALUES
    (1,1,8.00,'20261001','20261101'),(1,1,8.50,'20261101',NULL),
    (1,2,40.00,'20261001',NULL),(1,3,30.00,'20261001',NULL),(1,4,50.00,'20261001',NULL);
INSERT dbo.PremiseServices(PremiseId,ServiceId,ValidFrom) VALUES
    (1,1,'20261001'),(1,2,'20261001'),(1,3,'20261001'),(1,4,'20261001'),
    (2,1,'20261001'),(2,2,'20261001'),(2,3,'20261001'),(2,4,'20261001');
INSERT dbo.Meters(PremiseServiceId,SerialNumber,InstalledOn,InitialReading) VALUES
    (3,N'DEMO-WATER-001','20261001',100),(7,N'DEMO-WATER-002','20261001',200);
INSERT dbo.MeterReadings(MeterId,Period,ReadingDate,ReadingValue) VALUES
    (1,'20261001','20261031',104),(2,'20261001','20261031',205),
    (1,'20261101','20261130',110),(2,'20261101','20261130',212);
EXEC dbo.usp_GenerateMonthlyCharges @Period='20261001',@CreatedBy=1;
EXEC dbo.usp_RegisterPayment 1,'20261031',500,N'DEMO-PAY-001',1;
EXEC dbo.usp_RegisterPayment 2,'20261031',400,N'DEMO-PAY-002',1;
EXEC dbo.usp_GenerateMonthlyCharges @Period='20261101',@CreatedBy=1;
EXEC dbo.usp_RegisterPayment 1,'20261130',1000,N'DEMO-PAY-003',1;
COMMIT;
END TRY
BEGIN CATCH
    IF XACT_STATE()<>0 ROLLBACK;
    THROW;
END CATCH;
SELECT * FROM dbo.v_AccountBalances ORDER BY AccountId;
