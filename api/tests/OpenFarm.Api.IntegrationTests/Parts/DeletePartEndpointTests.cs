using OpenFarm.Api.IntegrationTests.Production;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using FluentAssertions;
using Finbuckle.MultiTenant.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using OpenFarm.Api.Endpoints.Auth;
using OpenFarm.Application.Auth;
using OpenFarm.Application.Parts;
using OpenFarm.Domain.Organizations;
using OpenFarm.Domain.Parts;
using OpenFarm.Domain.Production;
using OpenFarm.Infrastructure.Persistence;
using OpenFarm.Infrastructure.Multitenancy;

namespace OpenFarm.Api.IntegrationTests.Parts;

// Catalog dependency tests do not start printer/discovery workers or snapshot reconciliation.
public class DeletePartEndpointTests(ProductionTestFactory factory) : IClassFixture<ProductionTestFactory>
{
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<CustomWebApplicationFactory, LoginResponse> Tokens = new();

    private async Task<HttpClient> ClientAsync()
    {
        var client = factory.CreateClient();
        if (!Tokens.TryGetValue(factory, out var loginResult))
        {
            var login = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginCommand("admin", "admin-openfarm"));
            login.EnsureSuccessStatusCode();
            loginResult = (await login.Content.ReadFromJsonAsync<LoginResponse>())!;
            Tokens.Add(factory, loginResult);
        }
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", loginResult.Token);
        return client;
    }

    private static Part NewPart(string storagePath) => Part.Create(
        Organization.WellKnownId, $"Removable {Guid.NewGuid():N}", PartFileFormat.GCode, false,
        "part.gcode", storagePath, 10, 0.4m, null, null, EstimateStatus.Available, 60, 20,
        1, 1, $"{Guid.NewGuid():N}", [new FilamentRequirementSpec("PLA", null, null, null, null)], [], []);

    [Fact]
    public async Task DeleteUnusedPart_RetainsSharedFileAndOtherPart_ThenReturns404()
    {
        using var client = await ClientAsync();
        await factory.EnsureTenantInfoResolvedAsync();
        Guid firstId;
        Guid secondId;
        using (var scope = factory.CreateTenantScope())
        {
            var storage = scope.ServiceProvider.GetRequiredService<IPartFileStorage>();
            using var bytes = new MemoryStream(Encoding.UTF8.GetBytes("G28\nG1 X10"));
            var path = await storage.SaveAsync(Organization.WellKnownId, $"{Guid.NewGuid():N}", "part.gcode", bytes, CancellationToken.None);
            var repository = scope.ServiceProvider.GetRequiredService<IPartRepository>();
            var first = NewPart(path);
            var second = NewPart(path);
            await repository.AddAsync(first, CancellationToken.None);
            await repository.AddAsync(second, CancellationToken.None);
            firstId = first.Id;
            secondId = second.Id;
        }

        (await client.DeleteAsync($"/api/v1/parts/{firstId}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await client.GetAsync($"/api/v1/parts/{firstId}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await client.DeleteAsync($"/api/v1/parts/{firstId}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await client.GetAsync($"/api/v1/parts/{secondId}")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.GetStringAsync($"/api/v1/parts/{secondId}/file")).Should().Be("G28\nG1 X10");
    }

    [Fact]
    public async Task DeletePart_FromAnotherTenant_IsNotFoundAndLeavesOwnerPartIntact()
    {
        using var client = await ClientAsync();
        await factory.EnsureTenantInfoResolvedAsync();
        Guid id;
        using (var scope = factory.CreateTenantScope())
        {
            var part = NewPart("unused-test-path.gcode");
            await scope.ServiceProvider.GetRequiredService<IPartRepository>().AddAsync(part, CancellationToken.None);
            id = part.Id;
        }
        using (var scope = factory.CreateTenantScope())
        {
            var setter = scope.ServiceProvider.GetRequiredService<IMultiTenantContextSetter>();
            setter.MultiTenantContext = new MultiTenantContext<AppTenantInfo>(new AppTenantInfo
            {
                Id = Guid.NewGuid().ToString(),
                Identifier = "other-tenant",
                Name = "Other tenant"
            });
            var check = await scope.ServiceProvider.GetRequiredService<IPartDeletionEligibilityReader>().CheckAsync([id], default);
            check[0].Status.Should().Be("Absent");
            check[0].Name.Should().BeNull();
            check[0].Blockers.Should().BeEmpty();
            var handler = scope.ServiceProvider.GetRequiredService<OpenFarm.Application.Common.ICommandHandler<DeletePartCommand, bool>>();
            var action = () => handler.HandleAsync(new DeletePartCommand(id), CancellationToken.None);
            await action.Should().ThrowAsync<PartNotFoundException>();
        }
        (await client.GetAsync($"/api/v1/parts/{id}")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DeletePart_ReferencedByUnresolvedLegacyJob_Returns409(bool completed)
    {
        using var client = await ClientAsync();
        await factory.EnsureTenantInfoResolvedAsync();
        Guid id;
        using (var scope = factory.CreateTenantScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OpenFarmDbContext>();
            var part = NewPart("unused-test-path.gcode");
            var run = ProductionRun.Create(Organization.WellKnownId, "History", null, null, null,
                [new RunLineSpec(RunLineTargetKind.Part, part.Id, 1, null, AssignmentMode.Unassigned, null, 1, null)]);
            if (completed) run.MarkComplete(true);
            db.Parts.Add(part);
            db.ProductionRuns.Add(run);
            var requirement = PartRequirement.Create(Organization.WellKnownId, run.Lines[0].Id, part.Id, 1);
            db.PartRequirements.Add(requirement);
            db.PrintJobs.Add(PrintJob.Create(Organization.WellKnownId, requirement.Id, 1024, 1,
                AssignmentMode.Unassigned, null, part.RevisionHash, part.RevisionNumber, [new JobFilamentRequirementSpec("PLA", null)]));
            await db.SaveChangesAsync();
            id = part.Id;
        }

        var response = await client.DeleteAsync($"/api/v1/parts/{id}");
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await response.Content.ReadAsStringAsync()).Should().Contain("unresolved legacy job");
        (await client.GetAsync($"/api/v1/parts/{id}")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task RepositoryDelete_ReferenceAddedAfterPreflight_ThrowsTypedConflict()
    {
        // Exercise the database backstop directly, bypassing the handler preflight.
        using var client = await ClientAsync();
        await factory.EnsureTenantInfoResolvedAsync();
        using var scope = factory.CreateTenantScope();
        var db = scope.ServiceProvider.GetRequiredService<OpenFarmDbContext>();
        var part = NewPart("unused-test-path.gcode");
        var run = ProductionRun.Create(Organization.WellKnownId, "History", null, null, null,
            [new RunLineSpec(RunLineTargetKind.Part, part.Id, null, null, AssignmentMode.Unassigned, null, 1, null)]);
        db.Parts.Add(part);
        db.ProductionRuns.Add(run);
        db.PartRequirements.Add(PartRequirement.Create(Organization.WellKnownId, run.Lines[0].Id, part.Id, null));
        await db.SaveChangesAsync();
        var repository = scope.ServiceProvider.GetRequiredService<IPartRepository>();
        db.ChangeTracker.Clear();
        var loaded = await repository.GetByIdAsync(part.Id, CancellationToken.None);
        var action = () => repository.RemoveAsync(loaded!, CancellationToken.None);
        await action.Should().ThrowAsync<PartDeletionBlockedException>();
    }
    [Fact]
    public async Task CheckDeletion_MixedDependencies_IsReadOnlyAndMatchesDelete()
    {
        using var client = await ClientAsync();
        await factory.EnsureTenantInfoResolvedAsync();
        Guid blockedId;
        Guid eligibleId;
        Guid assemblyId;
        using (var scope = factory.CreateTenantScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OpenFarmDbContext>();
            var blocked = NewPart("blocked.gcode");
            var eligible = NewPart("eligible.gcode");
            var assembly = OpenFarm.Domain.Assemblies.Assembly.Create(Organization.WellKnownId, "Slab Ass", null,
                [new OpenFarm.Domain.Assemblies.BomLineSpec(OpenFarm.Domain.Assemblies.BomLineTargetKind.Part, blocked.Id, null, null, 1)]);
            var run = ProductionRun.Create(Organization.WellKnownId, "Unlimited slabs", null, null, null,
                [new RunLineSpec(RunLineTargetKind.Part, blocked.Id, null, null, AssignmentMode.Unassigned, null, 1, null)]);
            var requirement = PartRequirement.Create(Organization.WellKnownId, run.Lines[0].Id, blocked.Id, null);
            db.Parts.AddRange(blocked, eligible);
            db.Assemblies.Add(assembly);
            db.ProductionRuns.Add(run);
            db.PartRequirements.Add(requirement);
            db.PrintJobs.Add(PrintJob.Create(Organization.WellKnownId, requirement.Id, 1024, 1, AssignmentMode.Unassigned, null,
                blocked.RevisionHash, blocked.RevisionNumber, [new JobFilamentRequirementSpec("PLA", null)]));
            await db.SaveChangesAsync();
            blockedId = blocked.Id;
            eligibleId = eligible.Id;
            assemblyId = assembly.Id;
        }
        var missingId = Guid.NewGuid();
        var response = await client.PostAsJsonAsync("/api/v1/parts/deletion-check", new { partIds = new[] { blockedId, eligibleId, missingId } });
        response.EnsureSuccessStatusCode();
        var preview = (await response.Content.ReadFromJsonAsync<OpenFarm.Api.Endpoints.Parts.CheckPartDeletionResponse>())!;
        preview.Parts.Select(p => p.Status).Should().Equal("Blocked", "Eligible", "Absent");
        var blockers = preview.Parts[0].Blockers;
        blockers.Select(b => b.Code).Should().BeEquivalentTo("AssemblyReference", "UnlimitedRun", "UnfinishedLegacyJob");
        blockers.Should().Contain(b => b.EntityId == assemblyId && b.Name == "Slab Ass");
        using (var scope = factory.CreateTenantScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OpenFarmDbContext>();
            (await db.Parts.FindAsync(blockedId)).Should().NotBeNull();
            (await db.Parts.FindAsync(eligibleId)).Should().NotBeNull();
            db.PartRequirements.Single(r => r.PartId == blockedId).LibraryPartId.Should().Be(blockedId);
        }
        var deletion = await client.DeleteAsync($"/api/v1/parts/{blockedId}");
        deletion.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var problem = System.Text.Json.JsonDocument.Parse(await deletion.Content.ReadAsStringAsync());
        problem.RootElement.GetProperty("code").GetString().Should().Be("PartDeletionBlocked");
        problem.RootElement.GetProperty("blockers").GetArrayLength().Should().Be(3);
        (await client.DeleteAsync($"/api/v1/parts/{eligibleId}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task CheckDeletion_RejectsInvalidBatches()
    {
        using var client = await ClientAsync();
        var id = Guid.NewGuid();
        foreach (var ids in new[] { Array.Empty<Guid>(), new[] { id, id }, new[] { Guid.Empty }, Enumerable.Range(0, 201).Select(_ => Guid.NewGuid()).ToArray() })
        {
            (await client.PostAsJsonAsync("/api/v1/parts/deletion-check", new { partIds = ids })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        }
    }

    [Fact]
    public async Task CheckDeletion_AssemblyAddedAfterPreview_BlocksLockedDelete()
    {
        using var client = await ClientAsync();
        await factory.EnsureTenantInfoResolvedAsync();
        Guid id;
        using (var scope = factory.CreateTenantScope())
        {
            var part = NewPart("race.gcode");
            await scope.ServiceProvider.GetRequiredService<IPartRepository>().AddAsync(part, default);
            id = part.Id;
        }
        var preview = await client.PostAsJsonAsync("/api/v1/parts/deletion-check", new { partIds = new[] { id } });
        (await preview.Content.ReadFromJsonAsync<OpenFarm.Api.Endpoints.Parts.CheckPartDeletionResponse>())!.Parts[0].Status.Should().Be("Eligible");
        var assembly = await client.PostAsJsonAsync("/api/v1/assemblies", new { name = "Added after preview", lines = new[] { new { targetKind = "Part", targetId = id, quantity = 1 } } });
        assembly.EnsureSuccessStatusCode();
        (await client.DeleteAsync($"/api/v1/parts/{id}")).StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await client.GetAsync($"/api/v1/parts/{id}")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task AssemblyInsertInFlight_DeleteWaitsAndPreservesCommittedReference()
    {
        using var client = await ClientAsync();
        await factory.EnsureTenantInfoResolvedAsync();
        Guid id;
        using (var seedScope = factory.CreateTenantScope())
        {
            var part = NewPart("assembly-race.gcode");
            await seedScope.ServiceProvider.GetRequiredService<IPartRepository>().AddAsync(part, default);
            id = part.Id;
        }
        using var scope = factory.CreateTenantScope();
        var db = scope.ServiceProvider.GetRequiredService<OpenFarmDbContext>();
        await using var transaction = await db.Database.BeginTransactionAsync();
        var assembly = OpenFarm.Domain.Assemblies.Assembly.Create(Organization.WellKnownId, "Concurrent assembly", null,
            [new OpenFarm.Domain.Assemblies.BomLineSpec(OpenFarm.Domain.Assemblies.BomLineTargetKind.Part, id, null, null, 1)]);
        db.Assemblies.Add(assembly);
        // The uncommitted BOM insert holds the FK key-share lock on the part.
        await db.SaveChangesAsync();
        var deletion = client.DeleteAsync($"/api/v1/parts/{id}");
        await transaction.CommitAsync();
        (await deletion).StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await client.GetAsync($"/api/v1/parts/{id}")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.GetAsync($"/api/v1/assemblies/{assembly.Id}")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ConcurrentDeletes_OnlyOneCommits()
    {
        using var client = await ClientAsync();
        await factory.EnsureTenantInfoResolvedAsync();
        Guid id;
        using (var scope = factory.CreateTenantScope())
        {
            var part = NewPart("concurrent.gcode");
            await scope.ServiceProvider.GetRequiredService<IPartRepository>().AddAsync(part, default);
            id = part.Id;
        }
        var responses = await Task.WhenAll(client.DeleteAsync($"/api/v1/parts/{id}"), client.DeleteAsync($"/api/v1/parts/{id}"));
        responses.Count(r => r.StatusCode == HttpStatusCode.NoContent).Should().Be(1);
        responses.Should().Contain(r => r.StatusCode == HttpStatusCode.NotFound || r.StatusCode == HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task DeleteNotificationFailure_DoesNotUndoOrReportFailureForCommittedDeletion()
    {
        using var client = await ClientAsync();
        await factory.EnsureTenantInfoResolvedAsync();
        Guid id;
        using (var scope = factory.CreateTenantScope())
        {
            var part = NewPart("notification.gcode");
            var repository = scope.ServiceProvider.GetRequiredService<IPartRepository>();
            await repository.AddAsync(part, default);
            id = part.Id;
            var handler = new DeletePartCommandHandler(repository, new FailingDeletedPublisher());
            (await handler.HandleAsync(new DeletePartCommand(id), default)).Should().BeTrue();
        }
        (await client.GetAsync($"/api/v1/parts/{id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    private sealed class FailingDeletedPublisher : IPartEventPublisher
    {
        public Task PartDeletedAsync(Guid organizationId, Guid partId, CancellationToken cancellationToken) => throw new IOException("Notification unavailable");
        public Task PartProcessingJobStatusChangedAsync(Guid organizationId, PartProcessingJob job, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task PartUpsertedAsync(Guid organizationId, Guid partId, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task PartEstimateResolvedAsync(Guid organizationId, Guid partId, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task PartEstimateUnavailableAsync(Guid organizationId, Guid partId, CancellationToken cancellationToken) => Task.CompletedTask;
    }

}
