using Microsoft.EntityFrameworkCore;
using OsbbBilling.Web.Models.Entities;

namespace OsbbBilling.Web.Data;

public class OsbbDbContext(DbContextOptions<OsbbDbContext> options) : DbContext(options)
{
    public DbSet<Building> Buildings => Set<Building>();
    public DbSet<Owner> Owners => Set<Owner>();
    public DbSet<Premise> Premises => Set<Premise>();
    public DbSet<Account> Accounts => Set<Account>();
    public DbSet<AppUser> Users => Set<AppUser>();
    public DbSet<Service> Services => Set<Service>();
    public DbSet<Tariff> Tariffs => Set<Tariff>();
    public DbSet<PremiseService> PremiseServices => Set<PremiseService>();
    public DbSet<Meter> Meters => Set<Meter>();
    public DbSet<MeterReading> MeterReadings => Set<MeterReading>();
    public DbSet<Charge> Charges => Set<Charge>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<PaymentAllocation> PaymentAllocations => Set<PaymentAllocation>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("dbo");

        modelBuilder.Entity<Building>(entity =>
        {
            entity.ToTable("Buildings");
            entity.HasKey(x => x.BuildingId);
            entity.Property(x => x.Address).HasMaxLength(250);
            entity.HasIndex(x => x.Address).IsUnique();
        });

        modelBuilder.Entity<Owner>(entity =>
        {
            entity.ToTable("Owners");
            entity.HasKey(x => x.OwnerId);
            entity.Property(x => x.FullName).HasMaxLength(150);
            entity.Property(x => x.Phone).HasMaxLength(30);
            entity.Property(x => x.Email).HasMaxLength(254);
        });

        modelBuilder.Entity<Premise>(entity =>
        {
            entity.ToTable("Premises", table =>
            {
                table.HasTrigger("tr_Premises_Building");
                table.UseSqlOutputClause(false);
            });
            entity.HasKey(x => x.PremiseId);
            entity.Property(x => x.Number).HasMaxLength(20);
            entity.Property(x => x.Kind).HasMaxLength(12).IsUnicode(false);
            entity.Property(x => x.Area).HasPrecision(10, 2);
            entity.HasIndex(x => new { x.BuildingId, x.Number }).IsUnique();
            entity.HasOne(x => x.Building).WithMany(x => x.Premises).HasForeignKey(x => x.BuildingId);
        });

        modelBuilder.Entity<Account>(entity =>
        {
            entity.ToTable("Accounts", table =>
            {
                table.HasTrigger("tr_Accounts_Intervals");
                table.UseSqlOutputClause(false);
            });
            entity.HasKey(x => x.AccountId);
            entity.Property(x => x.AccountNumber).HasMaxLength(30).IsUnicode(false);
            entity.Property(x => x.ValidTo).IsConcurrencyToken();
            entity.HasIndex(x => x.AccountNumber).IsUnique();
            entity.HasOne(x => x.Owner).WithMany(x => x.Accounts).HasForeignKey(x => x.OwnerId);
            entity.HasOne(x => x.Premise).WithMany(x => x.Accounts).HasForeignKey(x => x.PremiseId);
        });

        modelBuilder.Entity<AppUser>(entity =>
        {
            entity.ToTable("Users");
            entity.HasKey(x => x.UserId);
            entity.Property(x => x.Login).HasMaxLength(100);
            entity.Property(x => x.PasswordHash).HasMaxLength(512);
            entity.Property(x => x.RoleCode).HasMaxLength(12).IsUnicode(false);
            entity.Property(x => x.IsActive).HasDefaultValue(true);
            entity.HasIndex(x => x.Login).IsUnique();
            entity.HasIndex(x => x.OwnerId).IsUnique().HasFilter("[OwnerId] IS NOT NULL");
            entity.HasOne(x => x.Owner).WithOne(x => x.User).HasForeignKey<AppUser>(x => x.OwnerId);
        });

        modelBuilder.Entity<Service>(entity =>
        {
            entity.ToTable("Services", table =>
            {
                table.HasTrigger("tr_Services_Method");
                table.UseSqlOutputClause(false);
            });
            entity.HasKey(x => x.ServiceId);
            entity.Property(x => x.Name).HasMaxLength(100);
            entity.Property(x => x.Unit).HasMaxLength(20);
            entity.Property(x => x.CalculationMethod).HasMaxLength(10).IsUnicode(false);
            entity.HasIndex(x => x.Name).IsUnique();
        });

        modelBuilder.Entity<Tariff>(entity =>
        {
            entity.ToTable("Tariffs", table =>
            {
                table.HasTrigger("tr_Tariffs_Intervals");
                table.UseSqlOutputClause(false);
            });
            entity.HasKey(x => x.TariffId);
            entity.Property(x => x.ValidTo).IsConcurrencyToken();
            entity.Property(x => x.UnitPrice).HasPrecision(18, 4);
            entity.HasIndex(x => new { x.BuildingId, x.ServiceId, x.ValidFrom }).IsUnique();
            entity.HasOne(x => x.Building).WithMany(x => x.Tariffs).HasForeignKey(x => x.BuildingId);
            entity.HasOne(x => x.Service).WithMany(x => x.Tariffs).HasForeignKey(x => x.ServiceId);
        });

        modelBuilder.Entity<PremiseService>(entity =>
        {
            entity.ToTable("PremiseServices", table =>
            {
                table.HasTrigger("tr_PremiseServices_Intervals");
                table.UseSqlOutputClause(false);
            });
            entity.HasKey(x => x.PremiseServiceId);
            entity.Property(x => x.ValidTo).IsConcurrencyToken();
            entity.HasIndex(x => new { x.PremiseId, x.ServiceId, x.ValidFrom }).IsUnique();
            entity.HasOne(x => x.Premise).WithMany(x => x.PremiseServices).HasForeignKey(x => x.PremiseId);
            entity.HasOne(x => x.Service).WithMany(x => x.PremiseServices).HasForeignKey(x => x.ServiceId);
        });

        modelBuilder.Entity<Meter>(entity =>
        {
            entity.ToTable("Meters", table =>
            {
                table.HasTrigger("tr_Meters_Intervals");
                table.UseSqlOutputClause(false);
            });
            entity.HasKey(x => x.MeterId);
            entity.Property(x => x.SerialNumber).HasMaxLength(50);
            entity.Property(x => x.InitialReading).HasPrecision(18, 3);
            entity.HasIndex(x => x.SerialNumber).IsUnique();
            entity.HasOne(x => x.PremiseService).WithMany(x => x.Meters).HasForeignKey(x => x.PremiseServiceId);
        });

        modelBuilder.Entity<MeterReading>(entity =>
        {
            entity.ToTable("MeterReadings", table =>
            {
                table.HasTrigger("tr_Readings_Validate");
                table.UseSqlOutputClause(false);
            });
            entity.HasKey(x => x.MeterReadingId);
            entity.Property(x => x.ReadingValue).HasPrecision(18, 3);
            entity.HasIndex(x => new { x.MeterId, x.Period }).IsUnique();
            entity.HasOne(x => x.Meter).WithMany(x => x.MeterReadings).HasForeignKey(x => x.MeterId);
        });

        modelBuilder.Entity<Charge>(entity =>
        {
            entity.ToTable("Charges", table =>
            {
                table.HasTrigger("tr_Charges_Validate");
                table.UseSqlOutputClause(false);
            });
            entity.HasKey(x => x.ChargeId);
            entity.Property(x => x.Quantity).HasPrecision(18, 3);
            entity.Property(x => x.UnitPrice).HasPrecision(18, 4);
            entity.Property(x => x.Amount).HasPrecision(19, 2)
                .HasComputedColumnSql("CONVERT(decimal(19,2),ROUND(Quantity*UnitPrice,2))", stored: true);
            entity.Property(x => x.CreatedAt).HasPrecision(0).HasDefaultValueSql("SYSUTCDATETIME()");
            entity.HasIndex(x => new { x.AccountId, x.PremiseServiceId, x.Period }).IsUnique();
            entity.HasOne(x => x.Account).WithMany(x => x.Charges).HasForeignKey(x => x.AccountId);
            entity.HasOne(x => x.PremiseService).WithMany(x => x.Charges).HasForeignKey(x => x.PremiseServiceId);
            entity.HasOne(x => x.Tariff).WithMany(x => x.Charges).HasForeignKey(x => x.TariffId);
            entity.HasOne(x => x.CreatedByUser).WithMany(x => x.CreatedCharges).HasForeignKey(x => x.CreatedBy);
        });

        modelBuilder.Entity<Payment>(entity =>
        {
            entity.ToTable("Payments", table =>
            {
                table.HasTrigger("tr_Payments_Validate");
                table.UseSqlOutputClause(false);
            });
            entity.HasKey(x => x.PaymentId);
            entity.Property(x => x.Amount).HasPrecision(19, 2);
            entity.Property(x => x.Reference).HasMaxLength(100);
            entity.Property(x => x.CreatedAt).HasPrecision(0).HasDefaultValueSql("SYSUTCDATETIME()");
            entity.HasIndex(x => x.Reference).IsUnique();
            entity.HasOne(x => x.Account).WithMany(x => x.Payments).HasForeignKey(x => x.AccountId);
            entity.HasOne(x => x.CreatedByUser).WithMany(x => x.CreatedPayments).HasForeignKey(x => x.CreatedBy);
        });

        modelBuilder.Entity<PaymentAllocation>(entity =>
        {
            entity.ToTable("PaymentAllocations", table =>
            {
                table.HasTrigger("tr_Allocations_Validate");
                table.UseSqlOutputClause(false);
            });
            entity.HasKey(x => new { x.PaymentId, x.ChargeId });
            entity.Property(x => x.Amount).HasPrecision(19, 2);
            entity.HasOne(x => x.Payment).WithMany(x => x.PaymentAllocations).HasForeignKey(x => x.PaymentId);
            entity.HasOne(x => x.Charge).WithMany(x => x.PaymentAllocations).HasForeignKey(x => x.ChargeId);
        });

        // Видалення батьківського запису не має каскадно стирати облікову історію.
        foreach (var foreignKey in modelBuilder.Model.GetEntityTypes().SelectMany(x => x.GetForeignKeys()))
            foreignKey.DeleteBehavior = DeleteBehavior.NoAction;
    }
}
