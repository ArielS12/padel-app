using System.Text;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Padel.Api.Data;
using Padel.Api.Domain;
using Padel.Api.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection("Jwt"));
builder.Services.Configure<GoogleAuthOptions>(builder.Configuration.GetSection("Google"));
builder.Services.Configure<MercadoPagoOptions>(builder.Configuration.GetSection("MercadoPago"));

var databaseProvider = builder.Configuration["Database:Provider"] ?? "SqlServer";
builder.Services.AddDbContext<AppDbContext>(options =>
{
    if (databaseProvider.Equals("PostgreSQL", StringComparison.OrdinalIgnoreCase) ||
        databaseProvider.Equals("Postgres", StringComparison.OrdinalIgnoreCase))
    {
        options.UseNpgsql(GetConnectionString(builder.Configuration, "PostgreSQL"));
        return;
    }

    options.UseSqlServer(GetConnectionString(builder.Configuration, "SqlServer"));
});

builder.Services
    .AddIdentity<ApplicationUser, IdentityRole>(options =>
    {
        options.SignIn.RequireConfirmedEmail = false;
        options.User.RequireUniqueEmail = true;
        options.Password.RequiredLength = 8;
        options.Password.RequireNonAlphanumeric = false;
    })
    .AddEntityFrameworkStores<AppDbContext>()
    .AddDefaultTokenProviders();

var jwtOptions = builder.Configuration.GetSection("Jwt").Get<JwtOptions>() ?? new JwtOptions();
builder.Services
    .AddAuthentication(options =>
    {
        options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
    })
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtOptions.Issuer,
            ValidAudience = jwtOptions.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOptions.Key))
        };
    });

builder.Services.AddAuthorization();
builder.Services.AddCors(options =>
{
    options.AddPolicy("Angular", policy =>
        policy.SetIsOriginAllowed(IsAllowedCorsOrigin)
            .AllowAnyHeader()
            .AllowAnyMethod());
});

builder.Services
    .AddControllers()
    .AddJsonOptions(options => options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));

builder.Services.AddScoped<ISkillMatcher, SkillMatcher>();
builder.Services.AddScoped<ITokenService, TokenService>();
builder.Services.AddScoped<IEmailSender, ConsoleEmailSender>();
builder.Services.AddScoped<IGoogleAuthService, GoogleAuthService>();
builder.Services.AddScoped<INotificationService, NotificationService>();
builder.Services.AddScoped<IAvailabilityService, AvailabilityService>();
builder.Services.AddScoped<IMatchService, MatchService>();
builder.Services.AddScoped<IPasswordHasher<Club>, PasswordHasher<Club>>();
builder.Services.AddHttpClient();
builder.Services.AddHttpClient<IMercadoPagoService, MercadoPagoService>();
builder.Services.AddScoped<IMercadoPagoCustomerCardService, MercadoPagoCustomerCardService>();
builder.Services.AddHostedService<MatchCancellationWorker>();

// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

var app = builder.Build();

await InitializeDatabaseAsync(app, databaseProvider);

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseCors("Angular");
app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();

static string GetConnectionString(IConfiguration configuration, string provider)
{
    var databaseUrl = configuration["DATABASE_URL"];
    if (!string.IsNullOrWhiteSpace(databaseUrl) &&
        (provider.Equals("PostgreSQL", StringComparison.OrdinalIgnoreCase) ||
            provider.Equals("Postgres", StringComparison.OrdinalIgnoreCase)))
    {
        return ConvertPostgresUrl(databaseUrl);
    }

    var connectionString = configuration.GetConnectionString("DefaultConnection");
    if (!string.IsNullOrWhiteSpace(connectionString))
    {
        return connectionString;
    }

    throw new InvalidOperationException("No se encontro la cadena de conexion de base de datos.");
}

static string ConvertPostgresUrl(string databaseUrl)
{
    if (!Uri.TryCreate(databaseUrl, UriKind.Absolute, out var uri))
    {
        return databaseUrl;
    }

    var userInfo = uri.UserInfo.Split(':', 2);
    var username = Uri.UnescapeDataString(userInfo.ElementAtOrDefault(0) ?? string.Empty);
    var password = Uri.UnescapeDataString(userInfo.ElementAtOrDefault(1) ?? string.Empty);
    var database = uri.AbsolutePath.TrimStart('/');
    var port = uri.Port > 0 ? uri.Port : 5432;

    return $"Host={uri.Host};Port={port};Database={database};Username={username};Password={password};SSL Mode=Require;Trust Server Certificate=true";
}

static async Task InitializeDatabaseAsync(WebApplication app, string databaseProvider)
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var isPostgres = databaseProvider.Equals("PostgreSQL", StringComparison.OrdinalIgnoreCase) ||
        databaseProvider.Equals("Postgres", StringComparison.OrdinalIgnoreCase);

    if (isPostgres)
    {
        await db.Database.EnsureCreatedAsync();
        await PostgresSchemaUpdater.ApplyAsync(db);
    }
    else if (app.Environment.IsDevelopment())
    {
        await db.Database.EnsureCreatedAsync();
    }

    await SeedData.InitializeAsync(app.Services, app.Configuration, app.Environment);
}

static bool IsAllowedCorsOrigin(string origin)
{
    if (string.IsNullOrWhiteSpace(origin) || !Uri.TryCreate(origin, UriKind.Absolute, out var uri))
    {
        return false;
    }

    if (uri.Scheme is not ("http" or "https"))
    {
        return false;
    }

    if (uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase))
    {
        return true;
    }

    return uri.Host.EndsWith(".onrender.com", StringComparison.OrdinalIgnoreCase);
}

public partial class Program;
