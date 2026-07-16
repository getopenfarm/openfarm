using System.Net;
using System.Security.Cryptography.X509Certificates;
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
using OpenFarm.Api.ExceptionHandling;
using OpenFarm.Api.Hubs;
using OpenFarm.Api.Processors;
using OpenFarm.Api.Security;
using OpenFarm.Application.ActivityLog;
using OpenFarm.Application.Auth;
using OpenFarm.Application.Common;
using OpenFarm.Application.ConnectionSecrets;
using OpenFarm.Application.Printers;
using OpenFarm.Application.Profiles;
using OpenFarm.Domain.ConnectionSecrets;
using OpenFarm.Infrastructure.ActivityLog;
using OpenFarm.Infrastructure.Auth;
using OpenFarm.Infrastructure.Identity;
using OpenFarm.Infrastructure.Multitenancy;
using OpenFarm.Infrastructure.Persistence;
using OpenFarm.Infrastructure.Printers;
using OpenFarm.Infrastructure.Printers.Bambu;
using OpenFarm.Infrastructure.Profiles;
using OpenFarm.Infrastructure.Security;
using OpenFarm.Infrastructure.Seed;
using Serilog;
// FastEndpoints and OpenFarm.Application.Common both define an ICommandHandler<,> — alias ours.
using LoginCommandHandlerType = OpenFarm.Application.Common.ICommandHandler<OpenFarm.Application.Auth.LoginCommand, OpenFarm.Application.Auth.LoginResult?>;
using CreateConnectionSecretHandlerType = OpenFarm.Application.Common.ICommandHandler<OpenFarm.Application.ConnectionSecrets.CreateConnectionSecretCommand, OpenFarm.Domain.ConnectionSecrets.ConnectionSecret>;
using RegisterPrinterHandlerType = OpenFarm.Application.Common.ICommandHandler<OpenFarm.Application.Printers.RegisterPrinterCommand, OpenFarm.Domain.Printers.Printer>;
using EditPrinterConnectionHandlerType = OpenFarm.Application.Common.ICommandHandler<OpenFarm.Application.Printers.EditPrinterConnectionCommand, OpenFarm.Domain.Printers.Printer>;
using RemovePrinterHandlerType = OpenFarm.Application.Common.ICommandHandler<OpenFarm.Application.Printers.RemovePrinterCommand, bool>;
using MarkPrinterReadyHandlerType = OpenFarm.Application.Common.ICommandHandler<OpenFarm.Application.Printers.MarkPrinterReadyCommand, OpenFarm.Domain.Printers.Printer>;
using PreviewProfileImportHandlerType = OpenFarm.Application.Common.ICommandHandler<OpenFarm.Application.Profiles.PreviewProfileImportCommand, OpenFarm.Application.Profiles.PreviewProfileImportResult>;
using ConfirmProfileImportHandlerType = OpenFarm.Application.Common.ICommandHandler<OpenFarm.Application.Profiles.ConfirmProfileImportCommand, OpenFarm.Application.Profiles.ConfirmProfileImportResult>;
using UpdateMachineProfileRawDataHandlerType = OpenFarm.Application.Common.ICommandHandler<OpenFarm.Application.Profiles.UpdateMachineProfileRawDataCommand, OpenFarm.Application.Profiles.MachineProfileDetail>;
using UpdateFilamentProfileRawDataHandlerType = OpenFarm.Application.Common.ICommandHandler<OpenFarm.Application.Profiles.UpdateFilamentProfileRawDataCommand, OpenFarm.Application.Profiles.FilamentProfileDetail>;

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

var dataProtectionBuilder = builder.Services
    .AddDataProtection()
    .SetApplicationName("OpenFarm")
    .PersistKeysToFileSystem(new DirectoryInfo(builder.Configuration["DataProtection:KeysDirectory"] is { Length: > 0 } keysDir ? keysDir : "./keys"));

// Without this, DataProtection keys sit on the keys volume in the clear — Linux has no
// DPAPI-equivalent fallback the way Windows does — so anyone with filesystem/backup access to
// that volume can decrypt every stored printer connection secret. Opt-in via config (rather than
// required) since self-hosted installs need a working default with no cert provisioned.
var dataProtectionCertificatePath = builder.Configuration["DataProtection:CertificatePath"];
if (!string.IsNullOrWhiteSpace(dataProtectionCertificatePath))
{
    var dataProtectionCertificatePassword = builder.Configuration["DataProtection:CertificatePassword"];
    dataProtectionBuilder.ProtectKeysWithCertificate(
        X509CertificateLoader.LoadPkcs12FromFile(dataProtectionCertificatePath, dataProtectionCertificatePassword));
}

builder.Services
    .AddIdentityCore<ApplicationUser>(options =>
    {
        options.User.RequireUniqueEmail = true;
        // Explicit (matches Identity's own defaults) so brute-force lockout behavior in
        // IdentityAuthenticationService doesn't silently drift if these are ever tightened
        // elsewhere without updating both spots.
        options.Lockout.AllowedForNewUsers = true;
        options.Lockout.MaxFailedAccessAttempts = 5;
        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(5);
    })
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
        // SignalR's WebSocket/SSE transports can't set an Authorization header, so the client sends
        // the JWT via the "access_token" query string for /hubs/* requests instead (standard pattern).
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                var accessToken = context.Request.Query["access_token"];
                if (!string.IsNullOrEmpty(accessToken) && context.HttpContext.Request.Path.StartsWithSegments("/hubs"))
                {
                    context.Token = accessToken;
                }

                return Task.CompletedTask;
            },
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

builder.Services.AddScoped<IPrinterRepository, PrinterRepository>();
// FakePrinterConnector is the dev/test default (constitution: CI never touches real hardware).
// "Printers:UseFakeConnector" opts into the real BambuLanConnector when explicitly set false —
// defaults to the fake connector outside Development too, since that's the safer default absent
// an explicit opt-in, and this connector has never been validated against physical hardware yet.
var useFakePrinterConnector = builder.Configuration.GetValue("Printers:UseFakeConnector", defaultValue: true);
if (useFakePrinterConnector)
{
    // Singleton so its in-memory per-serial event channels are shared across the long-lived
    // background service and short-lived dev/test callers.
    builder.Services.AddSingleton<FakePrinterConnector>();
    builder.Services.AddScoped<IPrinterConnector>(sp => sp.GetRequiredService<FakePrinterConnector>());
    builder.Services.AddScoped<IPrinterCameraRelay, FakePrinterCameraRelay>();
}
else
{
    builder.Services.AddScoped<IPrinterConnector, BambuLanConnector>();
    builder.Services.AddScoped<IPrinterCameraRelay, BambuCameraRelay>();
}

builder.Services.AddScoped<IPrinterEventPublisher, FarmHubPrinterEventPublisher>();
builder.Services.AddSingleton<PrinterConnectionBackgroundService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<PrinterConnectionBackgroundService>());
builder.Services.AddScoped<IPrinterConnectionManager>(sp => sp.GetRequiredService<PrinterConnectionBackgroundService>());

builder.Services.AddScoped<RegisterPrinterHandlerType, RegisterPrinterCommandHandler>();
builder.Services.AddScoped<EditPrinterConnectionHandlerType, EditPrinterConnectionCommandHandler>();
builder.Services.AddScoped<RemovePrinterHandlerType, RemovePrinterCommandHandler>();
builder.Services.AddScoped<MarkPrinterReadyHandlerType, MarkPrinterReadyCommandHandler>();
builder.Services.AddScoped<IQueryHandler<GetFleetQuery, IReadOnlyList<FleetPrinterSummary>>, GetFleetQueryHandler>();
builder.Services.AddScoped<IQueryHandler<GetPrinterDetailQuery, PrinterDetail?>, GetPrinterDetailQueryHandler>();

builder.Services.AddScoped<IMachineProfileRepository, MachineProfileRepository>();
builder.Services.AddScoped<IFilamentProfileRepository, FilamentProfileRepository>();
builder.Services.AddScoped<IProfileBundleParser, ProfileBundleParser>();
builder.Services.AddScoped<PreviewProfileImportHandlerType, PreviewProfileImportCommandHandler>();
builder.Services.AddScoped<ConfirmProfileImportHandlerType, ConfirmProfileImportCommandHandler>();
builder.Services.AddScoped<IQueryHandler<GetMachineProfilesQuery, IReadOnlyList<MachineProfileSummary>>, GetMachineProfilesQueryHandler>();
builder.Services.AddScoped<IQueryHandler<GetFilamentProfilesQuery, IReadOnlyList<FilamentProfileSummary>>, GetFilamentProfilesQueryHandler>();
builder.Services.AddScoped<IQueryHandler<GetMachineProfileQuery, MachineProfileDetail?>, GetMachineProfileQueryHandler>();
builder.Services.AddScoped<IQueryHandler<GetFilamentProfileQuery, FilamentProfileDetail?>, GetFilamentProfileQueryHandler>();
builder.Services.AddScoped<UpdateMachineProfileRawDataHandlerType, UpdateMachineProfileRawDataCommandHandler>();
builder.Services.AddScoped<UpdateFilamentProfileRawDataHandlerType, UpdateFilamentProfileRawDataCommandHandler>();

builder.Services.AddSignalR();

builder.Services.AddExceptionHandler<DomainExceptionHandler>();
builder.Services.AddProblemDetails();

builder.Services
    .AddFastEndpoints()
    .SwaggerDocument(o =>
    {
        // /api/v1/{tag}/... -> group generated Orval hooks/files by resource.
        o.AutoTagPathSegmentIndex = 3;
        o.ShortSchemaNames = true;
    });

var app = builder.Build();

// Loud on purpose: this flag silently no-ops every real printer connection when left at its
// safe-by-default true (see the registration comment above) — easy to mistake for a connectivity
// bug instead of a config gap.
Program.LogPrinterConnectorSelected(
    app.Logger,
    useFakePrinterConnector ? nameof(FakePrinterConnector) : nameof(BambuLanConnector),
    useFakePrinterConnector);

// LoginEndpoint's Throttle() falls back to Connection.RemoteIpAddress when no X-Forwarded-For
// header is present, and hard-403s ("can't identify client") if that's also null. Kestrel always
// sets it for a real socket, but WebApplicationFactory's in-memory TestServer never does — without
// this, every throttled endpoint would 403 unconditionally under integration tests.
app.Use(async (context, next) =>
{
    context.Connection.RemoteIpAddress ??= IPAddress.Loopback;
    await next();
});

app.UseSerilogRequestLogging();

app.UseExceptionHandler();

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

app.MapHub<FarmHub>("/hubs/farm");

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

public partial class Program
{
    [LoggerMessage(Level = LogLevel.Information, Message = "Printer connector: {ConnectorType} (Printers:UseFakeConnector={UseFakeConnector})")]
    private static partial void LogPrinterConnectorSelected(Microsoft.Extensions.Logging.ILogger logger, string connectorType, bool useFakeConnector);
}
