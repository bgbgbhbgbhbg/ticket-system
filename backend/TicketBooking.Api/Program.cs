using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Scalar.AspNetCore;
using System.Text;
using TicketBooking.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddOpenApi();

// CORS 設定:允許前端呼叫 API
builder.Services.AddCors(options =>
{
    options.AddPolicy("Frontend", policy =>
    {
        var frontendUrl = builder.Configuration["FrontendUrl"] ?? "http://localhost:3000";

        policy.WithOrigins(frontendUrl)
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

// JWT 認證(對應 AGENTS.md 第 3 節 & docs/adr/005-api-versioning-and-rbac.md)
var jwtSecret = builder.Configuration["Jwt:SecretKey"];
if (string.IsNullOrEmpty(jwtSecret))
{
    throw new InvalidOperationException("JWT:SecretKey not configured. Please set via 'dotnet user-secrets set \"Jwt:SecretKey\" \"...'\"");
}

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret)),
            ValidateAudience = false,
            ValidateIssuer = false,
            ValidateLifetime = true
        };
    });

builder.Services.AddAuthorization();

// DbContext 配置(對應 SETUP.md 第 5.2 節 & AGENTS.md 第 2 節)
var connectionString = builder.Configuration["ConnectionStrings:Postgres"];
if (string.IsNullOrEmpty(connectionString))
{
    throw new InvalidOperationException(
        "連線字串未配置。請執行:\n" +
        "dotnet user-secrets set \"ConnectionStrings:Postgres\" \"Host=localhost;Database=ticket_booking;Username=ticket_admin;Password=...\"");
}

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(connectionString));

// 註冊 Controllers
builder.Services.AddControllers();

// 註冊 Repository 和 Service (對應 AGENTS.md 第 2 節 Clean Architecture DI 組裝)
builder.Services.AddScoped<TicketBooking.Application.Interfaces.Repositories.ITicketRepository, 
    TicketBooking.Infrastructure.Repositories.TicketRepository>();
builder.Services.AddScoped<TicketBooking.Application.Interfaces.Services.ITicketService, 
    TicketBooking.Application.Services.TicketService>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

// 認證與授權中間件(順序很重要:認證 → 授權 → CORS)
app.UseAuthentication();
app.UseAuthorization();
app.UseCors("Frontend");

app.UseHttpsRedirection();

// Map Controllers
app.MapControllers();

var summaries = new[]
{
    "Freezing", "Bracing", "Chilly", "Cool", "Mild", "Warm", "Balmy", "Hot", "Sweltering", "Scorching"
};

// 所有 API 放在 /api/v1 前綴下(對應 docs/adr/005-api-versioning-and-rbac.md)
var apiGroup = app.MapGroup("/api/v1");

// #sym:weatherforecast - 天氣預測端點(前端參考)
apiGroup.MapGet("/weatherforecast", () =>
{
    var forecast =  Enumerable.Range(1, 5).Select(index =>
        new WeatherForecast
        (
            DateOnly.FromDateTime(DateTime.Now.AddDays(index)),
            Random.Shared.Next(-20, 55),
            summaries[Random.Shared.Next(summaries.Length)]
        ))
        .ToArray();
    return forecast;
})
.WithName("GetWeatherForecast")
.WithOpenApi();

app.Run();

record WeatherForecast(DateOnly Date, int TemperatureC, string? Summary)
{
    public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);
}
