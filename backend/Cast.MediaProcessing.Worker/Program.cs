using Cast.MediaProcessing.Worker.Audio;
using Cast.MediaProcessing.Worker.Data;
using Cast.MediaProcessing.Worker.Processing;
using Cast.MediaProcessing.Worker.Storage;
using Cast.ServiceDefaults;
using Microsoft.EntityFrameworkCore;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

public class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        var config = builder.Configuration;

        // Serilog + OpenTelemetry
        builder.AddCastServiceDefaults();

        builder.Services.AddOpenTelemetry()
            .WithTracing(t => t.AddEntityFrameworkCoreInstrumentation())
            .WithMetrics(m => m.AddPrometheusExporter());

        builder.Services.Configure<StorageOptions>(config.GetSection(StorageOptions.SectionName));

        var postgres = config.GetConnectionString("Postgres")!;
        builder.Services.AddDbContext<MediaDbContext>(o => o.UseNpgsql(postgres));

        builder.Services.AddSingleton<MinioStorage>();
        builder.Services.AddSingleton<AudioNormalizationService>();
        builder.Services.AddHostedService<MediaProcessingService>();

        builder.Services.AddHealthChecks().AddNpgSql(postgres);

        var app = builder.Build();

        app.MapGet("/", () => "Cast.MediaProcessing.Worker");
        app.MapHealthChecks("/health");
        app.MapPrometheusScrapingEndpoint();

        app.Run();
    }
}