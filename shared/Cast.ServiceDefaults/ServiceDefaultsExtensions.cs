using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;

namespace Cast.ServiceDefaults;

/// <summary>
/// Единый bootstrap наблюдаемости для всех сервисов (паттерн ServiceDefaults).
/// Подключает структурное логирование (Serilog: консоль + опционально Seq) и
/// OpenTelemetry (трейсы и метрики ASP.NET Core/HTTP/Runtime, экспорт по OTLP).
/// Каждый сервис вызывает один метод и получает одинаковую телеметрию
/// </summary>
public static class ServiceDefaultsExtensions
{
    public static WebApplicationBuilder AddCastServiceDefaults(this WebApplicationBuilder builder)
    {
        var serviceName = builder.Environment.ApplicationName;

        // --- Логи (Serilog) ---
        builder.Host.UseSerilog((ctx, cfg) =>
        {
            cfg.MinimumLevel.Information()
               .Enrich.FromLogContext()
               .Enrich.WithMachineName()
               .Enrich.WithThreadId()
               .Enrich.WithProperty("service", serviceName)
               .WriteTo.Console();

            var seq = ctx.Configuration["Seq:ServerUrl"];
            if (!string.IsNullOrWhiteSpace(seq))
                cfg.WriteTo.Seq(seq);
        });

        // --- Трейсы и метрики (OpenTelemetry) ---
        builder.Services.AddOpenTelemetry()
            .ConfigureResource(r => r.AddService(serviceName))
            .WithTracing(t => t
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddOtlpExporter())
            .WithMetrics(m => m
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddRuntimeInstrumentation()
                .AddOtlpExporter());

        return builder;
    }
}