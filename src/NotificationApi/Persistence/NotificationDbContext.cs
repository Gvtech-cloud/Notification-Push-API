using Microsoft.EntityFrameworkCore;
using NotificationApi.Domain.Models;

namespace NotificationApi.Persistence;

public class NotificationDbContext : DbContext
{
    public NotificationDbContext(DbContextOptions<NotificationDbContext> options)
        : base(options)
    {
    }

    public DbSet<Notification> Notifications => Set<Notification>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Notification>(entity =>
        {
            entity.HasKey(e => e.Id);

            entity.Property(e => e.CustomerId)
                .IsRequired()
                .HasMaxLength(100);

            entity.Property(e => e.Recipient)
                .IsRequired()
                .HasMaxLength(256);

            entity.Property(e => e.Subject)
                .HasMaxLength(256);

            entity.Property(e => e.Message)
                .IsRequired()
                .HasMaxLength(4000);

            entity.Property(e => e.IdempotencyHash)
                .IsRequired()
                .HasMaxLength(64);

            entity.Property(e => e.FailureReason)
                .HasMaxLength(2000);

            entity.HasIndex(e => e.CustomerId)
                .HasDatabaseName("IX_Notifications_CustomerId");

            entity.HasIndex(e => new { e.IdempotencyHash, e.CreatedAt })
                .HasDatabaseName("IX_Notifications_Idempotency_CreatedAt");

            entity.HasIndex(e => new { e.Status, e.Priority, e.RequestedDeliveryTime, e.CreatedAt })
                .HasDatabaseName("IX_Notifications_ProcessingQueue");
        });
    }
}
