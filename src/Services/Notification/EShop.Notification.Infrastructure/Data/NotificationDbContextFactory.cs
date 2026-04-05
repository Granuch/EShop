using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace EShop.Notification.Infrastructure.Data;

public sealed class NotificationDbContextFactory : IDesignTimeDbContextFactory<NotificationDbContext>
{
    public NotificationDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("ConnectionStrings__NotificationDb")
            ?? Environment.GetEnvironmentVariable("NOTIFICATION_DB_CONNECTION")
            ?? "Host=localhost;Port=5435;Database=eshop_notification;Username=postgres;Password=CHANGE_ME_notification_postgres_password";

        var optionsBuilder = new DbContextOptionsBuilder<NotificationDbContext>();
        optionsBuilder.UseNpgsql(connectionString);

        return new NotificationDbContext(optionsBuilder.Options);
    }
}
