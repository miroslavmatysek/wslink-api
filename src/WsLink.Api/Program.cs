using NLog.Web;

namespace WsLink.Api;

public class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        builder.Configuration
            .AddJsonFile("appsettings.json", false, true)
            .AddJsonFile($"appsettings.{builder.Environment.EnvironmentName}.json", true, true)
            .AddEnvironmentVariables()
            .Build();

        // Add services to the container.
        builder.Logging.ClearProviders();
        builder.Host.UseNLog();
        builder.Services.AddControllers();
        // Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
        builder.Services.AddOpenApi();
        builder.Services.AddOptions(builder.Configuration)
            .AddServices()
            .AddHealthChecks();

        var app = builder.Build();

        var logger = app.Services.GetRequiredService<ILogger<Program>>();


        // Configure the HTTP request pipeline.
        if (app.Environment.IsDevelopment())
        {
            app.MapOpenApi();
        }

        app.UseAuthorization();
        app.UseHealthChecks("/health");

        app.MapControllers();
        logger.LogInformation("WsLink.Api try to run [Version: {Version}]", typeof(Program).Assembly.GetName().Version);
        app.Run();
    }
}