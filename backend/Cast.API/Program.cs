using System.Text;
using Cast.API.Auth;
using Cast.API.Data;
using Cast.API.Domain;
using Cast.API.Events;
using Cast.API.Realtime;
using Cast.API.Rooms;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);
var config = builder.Configuration;

// --- Конфигурация ---
builder.Services.Configure<JwtOptions>(config.GetSection(JwtOptions.SectionName));
builder.Services.Configure<RabbitMqOptions>(config.GetSection(RabbitMqOptions.SectionName));

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

// --- SignalR + Redis backplane ---
var signalr = builder.Services.AddSignalR();
var redis = config.GetConnectionString("Redis");
if (!string.IsNullOrWhiteSpace(redis))
    signalr.AddStackExchangeRedis(redis);

// --- API + Swagger + CORS ---
builder.Services.AddControllers();
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
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.Run();
