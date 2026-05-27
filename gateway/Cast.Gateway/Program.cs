using Cast.ServiceDefaults;

public class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        builder.AddCastServiceDefaults();

        // Реверс-прокси (YARP) — единая точка входа. Маршруты/кластеры берём из конфига,
        // чтобы менять цели без пересборки. Падение шлюза не роняет сервисы за ним, а
        // падение одного бэкенда не затрагивает остальные маршруты
        builder.Services.AddReverseProxy()
            .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

        var app = builder.Build();

        // Доступность самого шлюза
        app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

        app.MapReverseProxy();

        app.Run();
    }
}