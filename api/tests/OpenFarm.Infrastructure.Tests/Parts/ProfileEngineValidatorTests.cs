using FluentAssertions;
using OpenFarm.Application.Parts;
using OpenFarm.Domain.Parts;
using OpenFarm.Domain.Profiles;
using OpenFarm.Infrastructure.Parts;

namespace OpenFarm.Infrastructure.Tests.Parts;

public sealed class ProfileEngineValidatorTests
{
    private static readonly SlicerRef Orca = new(SlicerEngine.OrcaSlicer, "2.3.1");
    private static ResolvedSlicingProfiles Profiles => new("{}", "{}", "{}", "P1S", "Standard", "PLA")
    { FilamentSourceFormat = ProfileSourceFormat.BambuStudio };

    [Fact]
    public async Task SuccessfulCrossSourceSliceIsCachedAcrossScopesAndConcurrentRequests()
    {
        using var cache = new ProfileEngineValidationCache();
        var engine = new FakeEngine();
        var first = new ProfileEngineValidator(engine, cache);
        var second = new ProfileEngineValidator(engine, cache);
        await Task.WhenAll(first.ValidateAsync(Orca, Profiles, "PLA", 0.4m, default),
            second.ValidateAsync(Orca, Profiles, "PLA", 0.4m, default));
        engine.Calls.Should().Be(1);
        engine.Model.Should().Contain("solid profile_validation");
    }

    [Fact]
    public async Task EngineVersionResolvedSettingsAndNozzleChangesRequireNewEvidence()
    {
        using var cache = new ProfileEngineValidationCache();
        var engine = new FakeEngine();
        var validator = new ProfileEngineValidator(engine, cache);
        await validator.ValidateAsync(Orca, Profiles, "PLA", 0.4m, default);
        await validator.ValidateAsync(new(SlicerEngine.OrcaSlicer, "2.4.0"), Profiles, "PLA", 0.4m, default);
        await validator.ValidateAsync(Orca, Profiles with { FilamentSettingsJson = "{\"flow_ratio\":0.98}" }, "PLA", 0.4m, default);
        await validator.ValidateAsync(Orca, Profiles, "PLA", 0.6m, default);
        engine.Calls.Should().Be(4);
    }

    [Fact]
    public async Task FailedValidationBlocksProductionAndDoesNotRepeatedlyInvokeTheCli()
    {
        using var cache = new ProfileEngineValidationCache();
        var engine = new FakeEngine { Failure = new SlicingFailedException("Unsupported custom G-code") };
        var validator = new ProfileEngineValidator(engine, cache);
        for (var attempt = 0; attempt < 2; attempt++)
            await ((Func<Task>)(() => validator.ValidateAsync(Orca, Profiles, "PLA", 0.4m, default)))
                .Should().ThrowAsync<SlicingProfileUnconfiguredException>().WithMessage("*OrcaSlicer 2.3.1*Unsupported custom G-code*");
        engine.Calls.Should().Be(1);
        engine.Failure = null;
        await validator.ValidateAsync(Orca, Profiles with { FilamentSettingsJson = "{\"fixed\":true}" }, "PLA", 0.4m, default);
        engine.Calls.Should().Be(2);
    }

    [Fact]
    public async Task CancellationIsNotRecordedAsIncompatibility()
    {
        using var cache = new ProfileEngineValidationCache();
        var engine = new FakeEngine { Failure = new OperationCanceledException() };
        var validator = new ProfileEngineValidator(engine, cache);
        await ((Func<Task>)(() => validator.ValidateAsync(Orca, Profiles, "PLA", 0.4m, default))).Should().ThrowAsync<OperationCanceledException>();
        engine.Failure = null;
        await validator.ValidateAsync(Orca, Profiles, "PLA", 0.4m, default);
        engine.Calls.Should().Be(2);
    }

    [Fact]
    public async Task EmptyValidationOutputIsRejected()
    {
        using var cache = new ProfileEngineValidationCache();
        var validator = new ProfileEngineValidator(new FakeEngine { Output = [] }, cache);
        await ((Func<Task>)(() => validator.ValidateAsync(Orca, Profiles, "PLA", 0.4m, default)))
            .Should().ThrowAsync<SlicingProfileUnconfiguredException>().WithMessage("*no output*");
    }

    [Fact]
    public async Task NativePresetsNeedNoExtraSliceAndReverseDirectionIsRejected()
    {
        using var cache = new ProfileEngineValidationCache();
        var engine = new FakeEngine();
        var validator = new ProfileEngineValidator(engine, cache);
        await validator.ValidateAsync(Orca, Profiles with { FilamentSourceFormat = ProfileSourceFormat.OrcaSlicer }, "PLA", 0.4m, default);
        await ((Func<Task>)(() => validator.ValidateAsync(new(SlicerEngine.BambuStudio, "2.7.1"), Profiles, "PLA", 0.4m, default)))
            .Should().ThrowAsync<SlicingProfileUnconfiguredException>();
        engine.Calls.Should().Be(0);
    }

    private sealed class FakeEngine : ISlicerEngine
    {
        public int Calls { get; private set; }
        public string? Model { get; private set; }
        public Exception? Failure { get; set; }
        public byte[] Output { get; set; } = [1];
        public Task<SlicingResult> SliceAsync(SlicerRef slicer, Stream model, PartFileFormat format, string material,
            decimal nozzle, Guid? printerId, CancellationToken ct) => throw new NotSupportedException();
        public async Task<SlicingResult> SliceConfiguredAsync(SlicerRef slicer, Stream model, PartFileFormat format,
            string material, decimal nozzle, Guid printerId, ResolvedSlicingProfiles profiles, CancellationToken ct)
        {
            Calls++;
            using var reader = new StreamReader(model, leaveOpen: true);
            Model = await reader.ReadToEndAsync(ct);
            if (Failure is { } failure) throw failure;
            return new(1, 1, 0.1m, [], Output);
        }
    }
}
