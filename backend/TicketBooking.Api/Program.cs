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

app.UseHttpsRedirection(); // 1. 先確保連線安全（建議放在最前面）
// middleware 順序:CORS 必須在 Authentication/Authorization 之前,
// 否則之後 Orders 這類需要 JWT 的 endpoint,瀏覽器送出的 CORS 預檢請求(preflight OPTIONS)
// 會在還沒驗證身份前就被卡住,而且錯誤訊息會誤導你以為是 CORS 設定錯,其實是順序錯
app.UseCors("Frontend");   // 2. 先把 CORS 開大門，讓 OPTIONS 請求安全通過並拿到 Header
app.UseAuthentication();   // 3. 檢查是誰來了（解析 JWT Token）
app.UseAuthorization();    // 4. 檢查這個人有沒有權限

// Map Controllers
app.MapControllers();      // 5. 最後才進入 Controller 執行業務邏輯

app.Run();