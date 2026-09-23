using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Platega.Demo.Data;

public sealed class DemoDbContext(DbContextOptions<DemoDbContext> options) : DbContext(options)
{
    public DbSet<Order> Orders => Set<Order>();

    public DbSet<SubscriptionRecord> Subscriptions => Set<SubscriptionRecord>();

    public DbSet<SubscriptionChargeRecord> SubscriptionCharges => Set<SubscriptionChargeRecord>();

    public DbSet<CallbackLogEntry> CallbackLog => Set<CallbackLogEntry>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // SQLite cannot compare or order DateTimeOffset natively; the binary form keeps ordering correct.
        configurationBuilder.Properties<DateTimeOffset>().HaveConversion<DateTimeOffsetToBinaryConverter>();
        configurationBuilder.Properties<decimal>().HaveConversion<string>();
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Order>(order =>
        {
            order.Property(item => item.Status).HasConversion<string>().HasMaxLength(32);
            order.Property(item => item.Method).HasConversion<string>().HasMaxLength(32);
            order.Property(item => item.UpdatedAt).IsConcurrencyToken();
            order.HasIndex(item => item.TransactionId).IsUnique();
            order.HasIndex(item => new { item.Status, item.CreatedAt });
        });

        modelBuilder.Entity<SubscriptionRecord>(subscription =>
        {
            subscription.Property(item => item.Id).ValueGeneratedNever();
            subscription.Property(item => item.Status).HasConversion<string>().HasMaxLength(32);
            subscription.Property(item => item.Interval).HasConversion<string>().HasMaxLength(16);
            subscription.Property(item => item.UpdatedAt).IsConcurrencyToken();
            subscription.HasMany(item => item.Charges).WithOne().HasForeignKey(charge => charge.SubscriptionId);
        });

        modelBuilder.Entity<SubscriptionChargeRecord>(charge =>
        {
            charge.Property(item => item.Id).ValueGeneratedNever();
            charge.Property(item => item.Status).HasConversion<string>().HasMaxLength(32);
        });

        modelBuilder.Entity<CallbackLogEntry>(entry => entry.HasIndex(item => item.ReceivedAt));
    }
}
