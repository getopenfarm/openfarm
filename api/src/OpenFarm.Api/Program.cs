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
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using OpenFarm.Api.ExceptionHandling;
using OpenFarm.Api.Hubs;
using OpenFarm.Api.Processors;
using OpenFarm.Api.Security;
using OpenFarm.Application.ActivityLog;
using OpenFarm.Application.Assemblies;
using OpenFarm.Application.Auth;
using OpenFarm.Application.Common;
using OpenFarm.Application.ConnectionSecrets;
using OpenFarm.Application.FilamentTracking;
using OpenFarm.Application.Organizations;
using OpenFarm.Application.Parts;
using OpenFarm.Application.Printers;
using OpenFarm.Application.Production;
using OpenFarm.Application.Profiles;
using OpenFarm.Domain.ConnectionSecrets;
using OpenFarm.Domain.Parts;
using OpenFarm.Infrastructure.ActivityLog;
using OpenFarm.Infrastructure.Assemblies;
using OpenFarm.Infrastructure.Auth;
using OpenFarm.Infrastructure.FilamentTracking;
using OpenFarm.Infrastructure.Identity;
using OpenFarm.Infrastructure.Multitenancy;
using OpenFarm.Infrastructure.Parts;
using OpenFarm.Infrastructure.Parts.SlicerInvocation;
using OpenFarm.Infrastructure.Persistence;
using OpenFarm.Infrastructure.Printers;
using OpenFarm.Infrastructure.Printers.Simulator;
using OpenFarm.Infrastructure.Production;
using OpenFarm.Infrastructure.Profiles;
using OpenFarm.Infrastructure.Security;
using OpenFarm.Infrastructure.Seed;
using Serilog;
// FastEndpoints and OpenFarm.Application.Common both define an ICommandHandler<,> — alias ours.
using LoginCommandHandlerType = OpenFarm.Application.Common.ICommandHandler<OpenFarm.Application.Auth.LoginCommand, OpenFarm.Application.Auth.LoginResult?>;
using CreateConnectionSecretHandlerType = OpenFarm.Application.Common.ICommandHandler<OpenFarm.Application.ConnectionSecrets.CreateConnectionSecretCommand, OpenFarm.Domain.ConnectionSecrets.ConnectionSecret>;
using RegisterPrinterHandlerType = OpenFarm.Application.Common.ICommandHandler<OpenFarm.Application.Printers.RegisterPrinterCommand, OpenFarm.Domain.Printers.Printer>;
using EditPrinterHandlerType = OpenFarm.Application.Common.ICommandHandler<OpenFarm.Application.Printers.EditPrinterCommand, OpenFarm.Domain.Printers.Printer>;
using RemovePrinterHandlerType = OpenFarm.Application.Common.ICommandHandler<OpenFarm.Application.Printers.RemovePrinterCommand, bool>;
using MarkPrinterReadyHandlerType = OpenFarm.Application.Common.ICommandHandler<OpenFarm.Application.Printers.MarkPrinterReadyCommand, OpenFarm.Domain.Printers.Printer>;
using PreviewProfileImportHandlerType = OpenFarm.Application.Common.ICommandHandler<OpenFarm.Application.Profiles.PreviewProfileImportCommand, OpenFarm.Application.Profiles.PreviewProfileImportResult>;
using ConfirmProfileImportHandlerType = OpenFarm.Application.Common.ICommandHandler<OpenFarm.Application.Profiles.ConfirmProfileImportCommand, OpenFarm.Application.Profiles.ConfirmProfileImportResult>;
using UpdateMachineProfileRawDataHandlerType = OpenFarm.Application.Common.ICommandHandler<OpenFarm.Application.Profiles.UpdateMachineProfileRawDataCommand, OpenFarm.Application.Profiles.MachineProfileDetail>;
using UpdateFilamentProfileRawDataHandlerType = OpenFarm.Application.Common.ICommandHandler<OpenFarm.Application.Profiles.UpdateFilamentProfileRawDataCommand, OpenFarm.Application.Profiles.FilamentProfileDetail>;
using CreateVendorHandlerType = OpenFarm.Application.Common.ICommandHandler<OpenFarm.Application.FilamentTracking.CreateVendorCommand, OpenFarm.Domain.FilamentTracking.Vendor>;
using CreateFilamentHandlerType = OpenFarm.Application.Common.ICommandHandler<OpenFarm.Application.FilamentTracking.CreateFilamentCommand, OpenFarm.Domain.FilamentTracking.Filament>;
using AddFilamentVariantHandlerType = OpenFarm.Application.Common.ICommandHandler<OpenFarm.Application.FilamentTracking.AddFilamentVariantCommand, OpenFarm.Domain.FilamentTracking.FilamentVariant>;
using RegisterSpoolHandlerType = OpenFarm.Application.Common.ICommandHandler<OpenFarm.Application.FilamentTracking.RegisterSpoolCommand, OpenFarm.Domain.FilamentTracking.Spool>;
using LoadSpoolHandlerType = OpenFarm.Application.Common.ICommandHandler<OpenFarm.Application.FilamentTracking.LoadSpoolOntoFeedSlotCommand, OpenFarm.Application.FilamentTracking.LoadSpoolResult>;
using UnloadSpoolHandlerType = OpenFarm.Application.Common.ICommandHandler<OpenFarm.Application.FilamentTracking.UnloadSpoolCommand, OpenFarm.Domain.FilamentTracking.Spool>;
using RecordSpoolUsageHandlerType = OpenFarm.Application.Common.ICommandHandler<OpenFarm.Application.FilamentTracking.RecordSpoolUsageCommand, OpenFarm.Application.FilamentTracking.RecordSpoolUsageResult>;
using CorrectRemainingWeightHandlerType = OpenFarm.Application.Common.ICommandHandler<OpenFarm.Application.FilamentTracking.CorrectRemainingWeightCommand, OpenFarm.Domain.FilamentTracking.Spool>;
using AddFeedUnitHandlerType = OpenFarm.Application.Common.ICommandHandler<OpenFarm.Application.FilamentTracking.AddFeedUnitCommand, OpenFarm.Domain.FilamentTracking.FeedUnit>;
using RemoveFeedUnitHandlerType = OpenFarm.Application.Common.ICommandHandler<OpenFarm.Application.FilamentTracking.RemoveFeedUnitCommand, OpenFarm.Domain.FilamentTracking.FeedUnit>;
using AddPartHandlerType = OpenFarm.Application.Common.ICommandHandler<OpenFarm.Application.Parts.AddPartCommand, OpenFarm.Application.Parts.AddPartResult>;
using ReplacePartFileHandlerType = OpenFarm.Application.Common.ICommandHandler<OpenFarm.Application.Parts.ReplacePartFileCommand, OpenFarm.Application.Parts.AddPartResult>;
using ConfirmPartProcessingJobHandlerType = OpenFarm.Application.Common.ICommandHandler<OpenFarm.Application.Parts.ConfirmPartProcessingJobCommand, OpenFarm.Domain.Parts.Part>;
using DiscardPartProcessingJobHandlerType = OpenFarm.Application.Common.ICommandHandler<OpenFarm.Application.Parts.DiscardPartProcessingJobCommand, bool>;
using DeletePartHandlerType = OpenFarm.Application.Common.ICommandHandler<OpenFarm.Application.Parts.DeletePartCommand, bool>;
using TagPartHandlerType = OpenFarm.Application.Common.ICommandHandler<OpenFarm.Application.Parts.TagPartCommand, OpenFarm.Application.Parts.PartTagsResult>;
using UntagPartHandlerType = OpenFarm.Application.Common.ICommandHandler<OpenFarm.Application.Parts.UntagPartCommand, OpenFarm.Application.Parts.PartTagsResult>;
using UpdatePartFilamentRequirementNarrowingHandlerType = OpenFarm.Application.Common.ICommandHandler<OpenFarm.Application.Parts.UpdatePartFilamentRequirementNarrowingCommand, OpenFarm.Domain.Parts.PartFilamentRequirement>;
using UpdateSlicingDefaultsHandlerType = OpenFarm.Application.Common.ICommandHandler<OpenFarm.Application.Parts.UpdateSlicingDefaultsCommand, OpenFarm.Application.Parts.SlicingDefaults>;
using CreateAssemblyHandlerType = OpenFarm.Application.Common.ICommandHandler<OpenFarm.Application.Assemblies.CreateAssemblyCommand, OpenFarm.Domain.Assemblies.Assembly>;
using EditAssemblyHandlerType = OpenFarm.Application.Common.ICommandHandler<OpenFarm.Application.Assemblies.EditAssemblyCommand, OpenFarm.Domain.Assemblies.Assembly>;
using DeleteAssemblyHandlerType = OpenFarm.Application.Common.ICommandHandler<OpenFarm.Application.Assemblies.DeleteAssemblyCommand, bool>;
using AddBomLineHandlerType = OpenFarm.Application.Common.ICommandHandler<OpenFarm.Application.Assemblies.AddBomLineCommand, OpenFarm.Domain.Assemblies.BomLine>;
using UpdateBomLineQuantityHandlerType = OpenFarm.Application.Common.ICommandHandler<OpenFarm.Application.Assemblies.UpdateBomLineQuantityCommand, OpenFarm.Domain.Assemblies.BomLine>;
using RemoveBomLineHandlerType = OpenFarm.Application.Common.ICommandHandler<OpenFarm.Application.Assemblies.RemoveBomLineCommand, bool>;
using CreateNonPrintedComponentHandlerType = OpenFarm.Application.Common.ICommandHandler<OpenFarm.Application.Assemblies.CreateNonPrintedComponentCommand, OpenFarm.Domain.Assemblies.NonPrintedComponent>;
using EditNonPrintedComponentHandlerType = OpenFarm.Application.Common.ICommandHandler<OpenFarm.Application.Assemblies.EditNonPrintedComponentCommand, OpenFarm.Domain.Assemblies.NonPrintedComponent>;
using DeleteNonPrintedComponentHandlerType = OpenFarm.Application.Common.ICommandHandler<OpenFarm.Application.Assemblies.DeleteNonPrintedComponentCommand, bool>;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((ctx, cfg) => cfg
    .ReadFrom.Configuration(ctx.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console(formatProvider: System.Globalization.CultureInfo.InvariantCulture));

var connectionString = builder.Configuration.GetConnectionString("Default")
    ?? throw new InvalidOperationException("ConnectionStrings:Default is not configured.");

builder.Services.AddDbContext<OpenFarmDbContext>(options => options
    .UseNpgsql(connectionString)
    // The Part Library migrations (AddPartsCatalog/AddPrinterMachineProfileLink/
    // AddOrganizationSlicingDefaults) were authored without `dotnet ef` tooling (no .NET SDK access
    // in the sandbox that wrote them — see those migrations' own header comments), so
    // OpenFarmDbContextModelSnapshot.cs was never regenerated to match. The migrations' Up()/Down()
    // methods are themselves accurate; this only suppresses EF's design-time-oriented "did you use
    // `dotnet ef migrations add`" check, which is the mitigation EF Core's own warning message
    // recommends. Regenerate the snapshot for real (`dotnet ef migrations add`, expect an empty
    // diff) once SDK access is available, then this can be removed.
    .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning)));

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
        // Relaxed from Identity's defaults (uppercase/digit no longer required) so the seeded
        // operator password can be a plain lowercase phrase; operators are expected to rotate it
        // after first login regardless.
        options.Password.RequireUppercase = false;
        options.Password.RequireDigit = false;
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
// Singleton so the simulator's in-memory per-serial state is shared across the long-lived
// background service and short-lived callers (tests). Connector selection between the simulator
// and the real BambuLanConnector happens per-printer inside PrinterConnectorFactory, keyed off
// each printer's own IP address — no process-wide switch (research.md §2).
builder.Services.AddSingleton<SimulatorPrinterConnector>();
builder.Services.AddSingleton<SimulatorPrinterCameraRelay>();
builder.Services.AddScoped<IPrinterConnectorFactory, PrinterConnectorFactory>();

builder.Services.AddScoped<IPrinterEventPublisher, FarmHubPrinterEventPublisher>();
builder.Services.AddSingleton<PrinterConnectionBackgroundService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<PrinterConnectionBackgroundService>());
// Must be Singleton, not Scoped — same reasoning as IPartProcessingDispatcher's registration
// below: a Scoped factory returning an already-singleton IDisposable gets that instance disposed
// at the end of whichever HTTP request scope first resolves it, killing the shared connection
// manager's worker loops for the rest of the process's lifetime. Dormant here only because no
// exercised code path happens to resolve IPrinterConnectionManager from within a request scope yet.
builder.Services.AddSingleton<IPrinterConnectionManager>(sp => sp.GetRequiredService<PrinterConnectionBackgroundService>());

builder.Services.AddScoped<RegisterPrinterHandlerType, RegisterPrinterCommandHandler>();
builder.Services.AddScoped<EditPrinterHandlerType, EditPrinterCommandHandler>();
builder.Services.AddScoped<RemovePrinterHandlerType, RemovePrinterCommandHandler>();
builder.Services.AddScoped<MarkPrinterReadyHandlerType, MarkPrinterReadyCommandHandler>();
builder.Services.AddScoped<IQueryHandler<GetFleetQuery, IReadOnlyList<FleetPrinterSummary>>, GetFleetQueryHandler>();
builder.Services.AddScoped<IQueryHandler<GetPrinterDetailQuery, PrinterDetail?>, GetPrinterDetailQueryHandler>();
builder.Services.AddScoped<IQueryHandler<GetPrinterCatalogQuery, PrinterCatalogResult>, GetPrinterCatalogQueryHandler>();

builder.Services.AddScoped<IMachineProfileRepository, MachineProfileRepository>();
builder.Services.AddScoped<IFilamentProfileRepository, FilamentProfileRepository>();
builder.Services.AddScoped<IProcessProfileRepository, ProcessProfileRepository>();
builder.Services.AddScoped<IProfileBundleParser, ProfileBundleParser>();
builder.Services.AddScoped<VendorProfileIndexReader>();
builder.Services.AddScoped<ISystemProfileIngestionService, SystemProfileIngestionService>();
builder.Services.AddScoped<PreviewProfileImportHandlerType, PreviewProfileImportCommandHandler>();
builder.Services.AddScoped<ConfirmProfileImportHandlerType, ConfirmProfileImportCommandHandler>();
builder.Services.AddScoped<IQueryHandler<GetMachineProfilesQuery, ProfilePage<MachineProfileSummary>>, GetMachineProfilesQueryHandler>();
builder.Services.AddScoped<IQueryHandler<GetFilamentProfilesQuery, ProfilePage<FilamentProfileSummary>>, GetFilamentProfilesQueryHandler>();
builder.Services.AddScoped<IQueryHandler<GetProcessProfilesQuery, ProfilePage<ProcessProfileSummary>>, GetProcessProfilesQueryHandler>();
builder.Services.AddScoped<IQueryHandler<GetMachineProfileQuery, MachineProfileDetail?>, GetMachineProfileQueryHandler>();
builder.Services.AddScoped<IQueryHandler<GetFilamentProfileQuery, FilamentProfileDetail?>, GetFilamentProfileQueryHandler>();
builder.Services.AddScoped<IQueryHandler<GetProcessProfileQuery, ProcessProfileDetail?>, GetProcessProfileQueryHandler>();
builder.Services.AddScoped<UpdateMachineProfileRawDataHandlerType, UpdateMachineProfileRawDataCommandHandler>();
builder.Services.AddScoped<UpdateFilamentProfileRawDataHandlerType, UpdateFilamentProfileRawDataCommandHandler>();

builder.Services.AddScoped<IVendorRepository, VendorRepository>();
builder.Services.AddScoped<IFilamentRepository, FilamentRepository>();
builder.Services.AddScoped<IFeedUnitRepository, FeedUnitRepository>();
builder.Services.AddScoped<ISpoolRepository, SpoolRepository>();
builder.Services.AddScoped<IFeedSlotTelemetrySync, FeedSlotTelemetrySyncHandler>();
builder.Services.AddScoped<IFilamentTrackingEventPublisher, FarmHubFilamentTrackingEventPublisher>();

builder.Services.AddScoped<CreateVendorHandlerType, CreateVendorCommandHandler>();
builder.Services.AddScoped<IQueryHandler<GetVendorsQuery, IReadOnlyList<VendorSummary>>, GetVendorsQueryHandler>();
builder.Services.AddScoped<CreateFilamentHandlerType, CreateFilamentCommandHandler>();
builder.Services.AddScoped<AddFilamentVariantHandlerType, AddFilamentVariantCommandHandler>();
builder.Services.AddScoped<IQueryHandler<GetFilamentsQuery, IReadOnlyList<FilamentSummary>>, GetFilamentsQueryHandler>();
builder.Services.AddScoped<IQueryHandler<GetFeedUnitsQuery, IReadOnlyList<FeedUnitDetail>>, GetFeedUnitsQueryHandler>();
builder.Services.AddScoped<RegisterSpoolHandlerType, RegisterSpoolCommandHandler>();
builder.Services.AddScoped<IQueryHandler<GetSpoolsQuery, IReadOnlyList<SpoolSummary>>, GetSpoolsQueryHandler>();
builder.Services.AddScoped<LoadSpoolHandlerType, LoadSpoolOntoFeedSlotCommandHandler>();
builder.Services.AddScoped<UnloadSpoolHandlerType, UnloadSpoolCommandHandler>();
builder.Services.AddScoped<ISpoolUsageRecordRepository, SpoolUsageRecordRepository>();
builder.Services.AddScoped<IAutoContinuationResolver, SpoolUsageRecordedHandler>();
builder.Services.AddScoped<RecordSpoolUsageHandlerType, RecordSpoolUsageCommandHandler>();
builder.Services.AddScoped<CorrectRemainingWeightHandlerType, CorrectRemainingWeightCommandHandler>();
builder.Services.AddScoped<AddFeedUnitHandlerType, AddFeedUnitCommandHandler>();
builder.Services.AddScoped<RemoveFeedUnitHandlerType, RemoveFeedUnitCommandHandler>();

builder.Services.Configure<PartFileStorageOptions>(builder.Configuration.GetSection(PartFileStorageOptions.SectionName));
builder.Services.Configure<SlicerOptions>(builder.Configuration.GetSection(SlicerOptions.SectionName));
builder.Services.Configure<SlicerBundleOptions>(builder.Configuration.GetSection(SlicerBundleOptions.SectionName));

builder.Services.AddScoped<IOrganizationRepository, OrganizationRepository>();
builder.Services.AddScoped<IPartRepository, PartRepository>();
builder.Services.AddScoped<ITagRepository, TagRepository>();
builder.Services.AddScoped<IPartProcessingJobRepository, PartProcessingJobRepository>();
builder.Services.AddScoped<IPartFileStorage, LocalDiskPartFileStorage>();
builder.Services.AddScoped<IPartEventPublisher, FarmHubPartEventPublisher>();
builder.Services.AddScoped<IPartFileClassifier, PartFileClassifier>();
builder.Services.AddScoped<IAlreadySlicedFileMetadataParser, AlreadySlicedFileMetadataParser>();
builder.Services.AddScoped<IUnslicedProjectMaterialResolver, UnslicedProjectMaterialResolver>();

builder.Services.AddScoped<IAssemblyRepository, AssemblyRepository>();
builder.Services.AddScoped<INonPrintedComponentRepository, NonPrintedComponentRepository>();
builder.Services.AddScoped<IAssemblyGraphQueryService, AssemblyGraphQueryService>();
builder.Services.AddScoped<OpenFarm.Api.Endpoints.Assemblies.BomLineTargetNameResolver>();

builder.Services.AddScoped<IProductionRunRepository, ProductionRunRepository>();
builder.Services.AddScoped<IPartRequirementRepository, PartRequirementRepository>();
builder.Services.AddScoped<IPrintJobRepository, PrintJobRepository>();
builder.Services.AddScoped<ProductionDispatchQueryService>();
builder.Services.AddScoped<IJobFileResolver, JobFileResolver>();
builder.Services.AddScoped<IJobTelemetrySync, JobTelemetrySync>();
builder.Services.AddScoped<IProductionEventPublisher, FarmHubProductionEventPublisher>();

builder.Services.AddSingleton<ProductionDispatchBackgroundService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<ProductionDispatchBackgroundService>());
// Must be Singleton, not Scoped — same reasoning as IPartProcessingDispatcher's registration above.
builder.Services.AddSingleton<IProductionDispatcher>(sp => sp.GetRequiredService<ProductionDispatchBackgroundService>());

// Resolved once at process startup below (not lazily on first request, research.md §2) — the
// eager-singleton registration avoids the race the spec calls out where a slice is requested while
// the backend is still discovering.
builder.Services.AddSingleton<IHostPlatformProvider, HostPlatform>();
builder.Services.AddSingleton<BundledSlicerScanner>();
builder.Services.AddSingleton<IAvailableSlicerCatalog, AvailableSlicerCatalog>();
builder.Services.AddSingleton<ISlicerInvocation, PrusaSliceFamilyInvocation>();
builder.Services.AddSingleton<ISlicerInvocation, SuperSlicerInvocation>();
builder.Services.AddScoped<ResolveSlicerSelection>();
builder.Services.AddScoped<RepresentativeFilamentResolver>();
builder.Services.AddScoped<ResolveSlicingProfileSelection>();

builder.Services.AddScoped<ISlicerEngine, CliSlicerEngine>();
builder.Services.AddScoped<ISlicerDefaultsProvider, SlicerDefaultsProvider>();
builder.Services.AddScoped<IPartFileProcessor, PartFileProcessor>();

builder.Services.AddSingleton(sp => new PartProcessingBackgroundService(
    sp.GetRequiredService<IServiceScopeFactory>(),
    sp.GetRequiredService<ILogger<PartProcessingBackgroundService>>(),
    sp.GetRequiredService<IOptions<SlicerOptions>>().Value.MaxConcurrentSlicingOperations));
builder.Services.AddHostedService(sp => sp.GetRequiredService<PartProcessingBackgroundService>());
// Must be Singleton, not Scoped: the factory returns an already-singleton BackgroundService
// (IDisposable via its base class). A Scoped registration whose factory returns a disposable
// instance gets that instance added to the CURRENT scope's own disposables list — meaning every
// HTTP request that resolves IPartProcessingDispatcher (i.e. every part upload) would dispose the
// shared PartProcessingBackgroundService singleton when that request's scope ends, cancelling its
// stoppingToken and permanently killing every worker loop after the very first upload. Silent: no
// exception, just every future job stuck "Processing" forever since nothing ever picks it up again.
builder.Services.AddSingleton<IPartProcessingDispatcher>(sp => sp.GetRequiredService<PartProcessingBackgroundService>());

builder.Services.AddScoped<AddPartHandlerType, AddPartCommandHandler>();
builder.Services.AddScoped<ReplacePartFileHandlerType, ReplacePartFileCommandHandler>();
builder.Services.AddScoped<ConfirmPartProcessingJobHandlerType, ConfirmPartProcessingJobCommandHandler>();
builder.Services.AddScoped<DiscardPartProcessingJobHandlerType, DiscardPartProcessingJobCommandHandler>();
builder.Services.AddScoped<DeletePartHandlerType, DeletePartCommandHandler>();
builder.Services.AddScoped<TagPartHandlerType, TagPartCommandHandler>();
builder.Services.AddScoped<UntagPartHandlerType, UntagPartCommandHandler>();
builder.Services.AddScoped<UpdatePartFilamentRequirementNarrowingHandlerType, UpdatePartFilamentRequirementNarrowingCommandHandler>();
builder.Services.AddScoped<UpdateSlicingDefaultsHandlerType, UpdateSlicingDefaultsCommandHandler>();
builder.Services.AddScoped<IQueryHandler<GetPartProcessingJobQuery, PartProcessingJobDetail?>, GetPartProcessingJobQueryHandler>();
builder.Services.AddScoped<IQueryHandler<GetPartsQuery, PartsPage>, GetPartsQueryHandler>();
builder.Services.AddScoped<IQueryHandler<GetPartQuery, PartDetail?>, GetPartQueryHandler>();
builder.Services.AddScoped<IQueryHandler<GetPartFileQuery, PartFileDownload?>, GetPartFileQueryHandler>();
builder.Services.AddScoped<IQueryHandler<ListTagsQuery, IReadOnlyList<TagSummary>>, ListTagsQueryHandler>();
builder.Services.AddScoped<IQueryHandler<GetSlicingDefaultsQuery, SlicingDefaults>, GetSlicingDefaultsQueryHandler>();

builder.Services.AddScoped<CreateAssemblyHandlerType, CreateAssemblyCommandHandler>();
builder.Services.AddScoped<EditAssemblyHandlerType, EditAssemblyCommandHandler>();
builder.Services.AddScoped<DeleteAssemblyHandlerType, DeleteAssemblyCommandHandler>();
builder.Services.AddScoped<AddBomLineHandlerType, AddBomLineCommandHandler>();
builder.Services.AddScoped<UpdateBomLineQuantityHandlerType, UpdateBomLineQuantityCommandHandler>();
builder.Services.AddScoped<RemoveBomLineHandlerType, RemoveBomLineCommandHandler>();
builder.Services.AddScoped<CreateNonPrintedComponentHandlerType, CreateNonPrintedComponentCommandHandler>();
builder.Services.AddScoped<EditNonPrintedComponentHandlerType, EditNonPrintedComponentCommandHandler>();
builder.Services.AddScoped<DeleteNonPrintedComponentHandlerType, DeleteNonPrintedComponentCommandHandler>();
builder.Services.AddScoped<IQueryHandler<GetAssembliesQuery, AssembliesPage>, GetAssembliesQueryHandler>();
builder.Services.AddScoped<IQueryHandler<GetAssemblyQuery, AssemblyDetail?>, GetAssemblyQueryHandler>();
builder.Services.AddScoped<IQueryHandler<ExplodeAssemblyQuery, BomExplosionResult>, ExplodeAssemblyQueryHandler>();
builder.Services.AddScoped<IQueryHandler<CalculateBuildableUnitsQuery, BuildableUnitsResult>, CalculateBuildableUnitsQueryHandler>();
builder.Services.AddScoped<IQueryHandler<ListNonPrintedComponentsQuery, NonPrintedComponentsPage>, ListNonPrintedComponentsQueryHandler>();

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

// System Profile ingestion CLI entry point (009, contracts/system-profile-ingestion.md) — re-runnable
// any time against the same bundle root BundledSlicerScanner already reads, not just at provisioning.
// Runs against the fully-migrated/seeded host above, then exits before the web server ever starts.
if (args.Contains("ingest-system-profiles"))
{
    using var scope = app.Services.CreateScope();

    // Finbuckle's tenant resolution is HTTP-middleware-based (UseMultiTenant()), which never runs
    // for this CLI-only scope — without setting it explicitly, every tenant-scoped EF query filter
    // (e.g. MachineProfiles', ProcessProfiles', FilamentProfiles') throws a NullReferenceException
    // against a null TenantInfo. Same pattern as PrinterConnectionBackgroundService.CreateTenantScope.
    var tenantStore = scope.ServiceProvider.GetRequiredService<IMultiTenantStore<AppTenantInfo>>();
    var tenantInfo = await tenantStore.GetByIdentifierAsync(OrganizationTenantStore.StaticIdentifier)
        ?? throw new InvalidOperationException("No organization found — FirstBootSeeder must run before system profile ingestion.");
    var contextSetter = scope.ServiceProvider.GetRequiredService<IMultiTenantContextSetter>();
    contextSetter.MultiTenantContext = new MultiTenantContext<AppTenantInfo>(tenantInfo);

    var scanner = scope.ServiceProvider.GetRequiredService<BundledSlicerScanner>();
    var ingestionService = scope.ServiceProvider.GetRequiredService<ISystemProfileIngestionService>();
    var ingestionLogger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();

    var leaves = scanner.ScanLeafDirectories();
    // CA1848 (LoggerMessage source-generation) and CA1873 (avoid evaluating log arguments when the
    // level is disabled) are both hot-path optimizations — this CLI entry point logs a handful of
    // times total per process lifetime, never in a request/loop-processing path.
#pragma warning disable CA1848, CA1873
    ingestionLogger.LogInformation("System Profile ingestion: found {LeafCount} bundled slicer leaf/leaves under the configured root.", leaves.Count);

    foreach (var leaf in leaves)
    {
        var summary = await ingestionService.IngestAsync(leaf, CancellationToken.None);
        ingestionLogger.LogInformation(
            "Ingested '{Leaf}': vendors scanned={VendorsScanned}, skipped={VendorsSkipped}; " +
            "machine created={MachineCreated} updated={MachineUpdated} stale={MachineStale}; " +
            "process created={ProcessCreated} updated={ProcessUpdated} stale={ProcessStale}; " +
            "filament created={FilamentCreated} updated={FilamentUpdated} stale={FilamentStale}.",
            leaf, summary.VendorsScanned, summary.VendorsSkipped,
            summary.Machine.Created, summary.Machine.Updated, summary.Machine.MarkedStale,
            summary.Process.Created, summary.Process.Updated, summary.Process.MarkedStale,
            summary.Filament.Created, summary.Filament.Updated, summary.Filament.MarkedStale);
#pragma warning restore CA1848, CA1873
    }

    return;
}

// Forces the eager-singleton scan to run now, before the host starts accepting requests (research.md
// §2) — a bare AddSingleton registration is otherwise only constructed lazily on first resolution.
app.Services.GetRequiredService<IAvailableSlicerCatalog>();

app.Run();

public partial class Program;
