using Cast.API.Auth;
using Cast.API.Bets;
using Cast.API.Data;
using Cast.API.Domain;
using Cast.API.Economy;
using Cast.API.Events;
using Cast.API.Games;
using Cast.API.Media;
using Cast.API.Mods;
using Cast.API.Realtime;
using Cast.API.Rooms;
using Cast.API.Social;
using Cast.API.Storage;
using Cast.API.Streamer;
using Cast.ServiceDefaults;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using System.Text;
using System.Text.Json.Serialization;

public class Program
{
    public static async Task Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        var config = builder.Configuration;

        builder.AddCastServiceDefaults();

        // --- Конфигурация ---
        builder.Services.Configure<JwtOptions>(config.GetSection(JwtOptions.SectionName));
        builder.Services.Configure<RabbitMqOptions>(config.GetSection(RabbitMqOptions.SectionName));
        builder.Services.Configure<StorageOptions>(config.GetSection(StorageOptions.SectionName));

        // --- БД (PostgreSQL) ---
        builder.Services.AddDbContext<CastDbContext>(opt =>
            opt.UseNpgsql(config.GetConnectionString("Postgres")));

        // --- Аккаунты (Identity) ---
        builder.Services
            .AddIdentityCore<ApplicationUser>(o =>
            {
                o.User.RequireUniqueEmail = true;
                o.Password.RequiredLength = 6;
                o.Password.RequireNonAlphanumeric = false;
                o.Password.RequireUppercase = false;
            })
            .AddRoles<IdentityRole<Guid>>()
            .AddEntityFrameworkStores<CastDbContext>();

        builder.Services.AddSingleton<JwtTokenService>();
        builder.Services.AddScoped<RoomService>();
        builder.Services.AddScoped<WalletService>();
        builder.Services.AddScoped<PresenceService>();
        builder.Services.AddScoped<BettingService>();
        builder.Services.AddScoped<GameService>();
        builder.Services.AddSingleton<StorageService>();
        builder.Services.AddScoped<StatusService>();
        builder.Services.AddScoped<SocialService>();
        builder.Services.AddScoped<OnlinePresenceService>();
        builder.Services.AddScoped<MediaService>();
        builder.Services.AddScoped<Cast.API.Tags.TagService>();
        builder.Services.AddScoped<AnalyticsService>();
        builder.Services.AddHostedService<WatchTimeAccrualService>();

        // --- Каталог манифестов игр (авторитетный белый список событий) ---
        builder.Services.AddSingleton(sp => new ManifestCatalog(
            Path.Combine(builder.Environment.ContentRootPath, "Manifests"),
            sp.GetRequiredService<ILogger<ManifestCatalog>>()));

        // --- Пакеты модов для десктоп-клиента (Mod Manager) из БД игр ---
        builder.Services.AddScoped<ModService>();

        // --- Шина событий (RabbitMQ) ---
        builder.Services.AddSingleton<RabbitMqConnection>();
        builder.Services.AddSingleton<IEventBus, RabbitMqEventBus>();
        builder.Services.AddHostedService<EventConsumerService>();

        // --- Аутентификация (JWT) ---
        var jwt = config.GetSection(JwtOptions.SectionName).Get<JwtOptions>() ?? new JwtOptions();
        builder.Services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    ValidIssuer = jwt.Issuer,
                    ValidAudience = jwt.Audience,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey))
                };

                options.Events = new JwtBearerEvents
                {
                    OnMessageReceived = ctx =>
                    {
                        var token = ctx.Request.Query["access_token"];
                        var path = ctx.HttpContext.Request.Path;
                        if (!string.IsNullOrEmpty(token) && path.StartsWithSegments("/hubs"))
                            ctx.Token = token;
                        return Task.CompletedTask;
                    }
                };
            });
        builder.Services.AddAuthorization();

        // --- SignalR + MessagePack + Redis backplane ---
        // MessagePack: компактнее и быстрее JSON в горячем пути (меньше байт и
        // аллокаций). Клиент должен подключать парный протокол и коннектиться напрямую
        // по WebSockets (skipNegotiation: true), без negotiate-хопа и sticky sessions
        var signalr = builder.Services.AddSignalR().AddMessagePackProtocol();
        var redis = config.GetConnectionString("Redis");
        if (!string.IsNullOrWhiteSpace(redis))
            signalr.AddStackExchangeRedis(redis);

        // --- API + Swagger + CORS ---
        builder.Services.AddControllers()
            .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSwaggerGen(c =>
        {
            c.SwaggerDoc("v1", new OpenApiInfo { Title = "Copiuma.Cast API", Version = "v1" });
            c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT",
                Description = "Введите JWT (без префикса Bearer)."
            });
            c.AddSecurityRequirement(new OpenApiSecurityRequirement
            {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
            },
            Array.Empty<string>()
        }
            });
        });

        var allowedOrigins = config.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? Array.Empty<string>();
        builder.Services.AddCors(o => o.AddDefaultPolicy(p => p
            .WithOrigins(allowedOrigins)
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials()));

        var app = builder.Build();

        if (app.Environment.IsDevelopment())
        {
            app.UseSwagger();
            app.UseSwaggerUI();
        }

        app.UseCors();
        app.UseAuthentication();
        app.UseAuthorization();

        app.MapControllers();
        app.MapHub<RoomHub>("/hubs/room");
        app.MapHub<PresenceHub>("/hubs/presence");
        app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

        // --- Сидирование ролей и (опц.) администратора ---
        using (var scope = app.Services.CreateScope())
        {
            var sp = scope.ServiceProvider;

            var dbContext = sp.GetRequiredService<CastDbContext>();
            await dbContext.Database.MigrateAsync();

            var roleManager = sp.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
            foreach (var role in new[] { "Admin", "Streamer" })
                if (!await roleManager.RoleExistsAsync(role))
                    await roleManager.CreateAsync(new IdentityRole<Guid>(role));

            var adminEmail = config["Admin:Email"];
            var adminPassword = config["Admin:Password"];
            if (!string.IsNullOrWhiteSpace(adminEmail) && !string.IsNullOrWhiteSpace(adminPassword))
            {
                var userManager = sp.GetRequiredService<UserManager<ApplicationUser>>();
                var admin = await userManager.FindByEmailAsync(adminEmail);
                if (admin is null)
                {
                    admin = new ApplicationUser
                    {
                        UserName = adminEmail,
                        Email = adminEmail,
                        DisplayName = "Administrator",
                        Handle = "admin",
                        Language = "en"
                    };
                    await userManager.CreateAsync(admin, adminPassword);
                }
                if (!await userManager.IsInRoleAsync(admin, "Admin"))
                    await userManager.AddToRoleAsync(admin, "Admin");
            }
        }

        app.Run();
    }
}