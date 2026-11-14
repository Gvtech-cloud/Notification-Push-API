using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi.Models;
using NotificationApi.Models.Configuration;
using NotificationApi.Persistence;
using NotificationApi.Providers;
using NotificationApi.Services;
using NotificationApi.Validation;

var builder = WebApplication.CreateBuilder(args);

var notificationSettingsSection = builder.Configuration.GetSection(NotificationSettings.SectionName);
builder.Services.Configure<NotificationSettings>(notificationSettingsSection);

var connectionString = builder.Configuration.GetConnectionString("NotificationDatabase")
                       ?? "Data Source=notifications.db";

builder.Services.AddDbContext<NotificationDbContext>(options =>
{
    options.UseSqlite(connectionString);
});

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<INotificationRepository, NotificationRepository>();
builder.Services.AddSingleton<INotificationRequestValidator, NotificationRequestValidator>();
builder.Services.AddSingleton<IIdempotencyService, IdempotencyService>();
builder.Services.AddSingleton<INotificationProvider, SimulatedEmailNotificationProvider>();
builder.Services.AddScoped<INotificationProcessingService, NotificationProcessingService>();

builder.Services.AddHostedService<NotificationBackgroundWorker>();

builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
        options.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
    });

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Customer Notification Processing API",
        Version = "v1",
        Description = "A .NET Web API for receiving, validating, deduplicating, and processing customer notifications."
    });
});

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<NotificationDbContext>();
    await db.Database.EnsureCreatedAsync();
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "Notification Processing API v1");
        c.RoutePrefix = string.Empty;
    });
}

app.UseHttpsRedirection();
app.UseAuthorization();
app.MapControllers();

app.Run();

public partial class Program { }
