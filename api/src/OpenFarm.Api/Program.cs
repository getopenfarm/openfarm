using System.Text;
using FastEndpoints;
using FastEndpoints.Swagger;
using Finbuckle.MultiTenant.Abstractions;
using Finbuckle.MultiTenant.AspNetCore.Extensions;
using Finbuckle.MultiTenant.Extensions;
using FluentValidation;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using OpenFarm.Api.Processors;
using OpenFarm.Api.Security;
using OpenFarm.Application.ActivityLog;
using OpenFarm.Application.Auth;
using OpenFarm.Application.Common;
using OpenFarm.Application.ConnectionSecrets;
using OpenFarm.Domain.ConnectionSecrets;
using OpenFarm.Infrastructure.ActivityLog;
using OpenFarm.Infrastructure.Auth;
using OpenFarm.Infrastructure.Identity;
using OpenFarm.Infrastructure.Multitenancy;
using OpenFarm.Infrastructure.Persistence;
using OpenFarm.Infrastructure.Security;
using OpenFarm.Infrastructure.Seed;
using Serilog;
// FastEndpoints and OpenFarm.Application.Common both define an ICommandHandler<,> — alias ours.
using LoginCommandHandlerType = OpenFarm.Application.Common.ICommandHandler<OpenFarm.Application.Auth.LoginCommand, OpenFarm.Application.Auth.LoginResult?>;
using CreateConnectionSecretHandlerType = OpenFarm.Application.Common.ICommandHandler<OpenFarm.Application.ConnectionSecrets.CreateConnectionSecretCommand, OpenFarm.Domain.ConnectionSecrets.ConnectionSecret>;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((ctx, cfg) => cfg
    .ReadFrom.Configuration(ctx.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console(formatProvider: System.Globalization.CultureInfo.InvariantCulture));

var connectionString = builder.Configuration.GetConnectionString("Default")
    ?? throw new InvalidOperationException("ConnectionStrings:Default is not configured.");

builder.Services.AddDbContext<OpenFarmDbContext>(options => options.UseNpgsql(connectionString));

builder.Services
    .AddHealthChecks()
    .AddNpgSql(connectionString, name: "postgres");

builder.Services
    .AddDataProtection()
    .SetApplicationName("OpenFarm")
    .PersistKeysToFileSystem(new DirectoryInfo(builder.Configuration["DataProtection:KeysDirectory"] is { Length: > 0 } keysDir ? keysDir : "./keys"));

builder.Services
    .AddIdentityCore<ApplicationUser>(options => options.User.RequireUniqueEmail = true)
    .AddRoles<ApplicationRole>()
    .AddEntityFrameworkStores<OpenFarmDbContext>();

builder.Services
    .AddMultiTenant<AppTenantInfo>()
    .WithStaticStrategy(OrganizationTenantStore.StaticIdentifier)
    .WithStore<OrganizationTenantStore>(ServiceLifetime.Scoped);

var jwtIssuer = builder.Configuration["Jwt:Issuer"]
    ?? throw new InvalidOperationException("Jwt:Issuer is not configured.");
var jwtAudience = builder.Configuration["Jwt:Audience"]
    ?? throw new InvalidOperationException("Jwt:Audience is not configured.");
var jwtSigningKey = builder.Configuration["Jwt:SigningKey"]
    ?? throw new InvalidOperationException("Jwt:SigningKey is not configured.");

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidIssuer = jwtIssuer,
            ValidAudience = jwtAudience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSigningKey)),
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateIssuerSigningKey = true,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(1),
        };
    });

builder.Services.AddAuthorizationBuilder()
    .AddPolicy("Permission", p => p.Requirements.Add(new PermissionRequirement()));
builder.Services.AddSingleton<IAuthorizationHandler, PermissionAuthorizationHandler>();

var corsAllowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(options =>
    options.AddDefaultPolicy(policy => policy
        .WithOrigins(corsAllowedOrigins)
        .AllowAnyHeader()
        .AllowAnyMethod()));

builder.Services.AddValidatorsFromAssemblyContaining<ISecretProtector>();
builder.Services.AddValidatorsFromAssemblyContaining<Program>();

builder.Services.AddScoped<ISecretProtector, DataProtectionSecretProtector>();
builder.Services.AddScoped<IActivityLogWriter, EfActivityLogWriter>();
builder.Services.AddScoped<IActivityLogReader, EfActivityLogReader>();
builder.Services.AddScoped<IQueryHandler<GetActivityLogQuery, ActivityLogPageResult>, GetActivityLogQueryHandler>();
builder.Services.AddScoped<FirstBootSeeder>();

builder.Services.AddScoped<JwtTokenService>();
builder.Services.AddScoped<IAuthenticationService, IdentityAuthenticationService>();
builder.Services.AddScoped<LoginCommandHandlerType, LoginCommandHandler>();
builder.Services.AddScoped<IQueryHandler<WhoAmIQuery, WhoAmIResult?>, WhoAmIQueryHandler>();

builder.Services.AddScoped<IConnectionSecretRepository, ConnectionSecretRepository>();
builder.Services.AddScoped<CreateConnectionSecretHandlerType, CreateConnectionSecretCommandHandler>();
builder.Services.AddScoped<IQueryHandler<ListConnectionSecretsQuery, IReadOnlyList<ConnectionSecretResult>>, ListConnectionSecretsQueryHandler>();

builder.Services
    .AddFastEndpoints()
    .SwaggerDocument(o =>
    {
        // /api/v1/{tag}/... -> group generated Orval hooks/files by resource.
        o.AutoTagPathSegmentIndex = 3;
        o.ShortSchemaNames = true;
    });

var app = builder.Build();

app.UseSerilogRequestLogging();

app.UseCors();

app.UseMultiTenant();

app.UseAuthentication();
app.UseAuthorization();

app.UseFastEndpoints(c =>
{
    c.Errors.UseProblemDetails();
    c.Endpoints.Configurator = ep =>
    {
        ep.PreProcessors(Order.Before, new CommandValidationPreProcessor());
        ep.PostProcessors(Order.After, new ActivityLogPostProcessor());
    };
});
app.UseSwaggerGen();

app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready");

using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<OpenFarmDbContext>();
    await dbContext.Database.MigrateAsync();

    var seeder = scope.ServiceProvider.GetRequiredService<FirstBootSeeder>();
    await seeder.SeedAsync();
}

app.Run();

public partial class Program;
