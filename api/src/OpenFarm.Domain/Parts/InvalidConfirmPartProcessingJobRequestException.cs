namespace OpenFarm.Domain.Parts;

/// <summary>A Confirm request violated a kind-aware or job-state-aware rule (data-model.md's Validation rules) — e.g. a Replace supplying name/tags, or an override's requirementIndex not matching a preview requirement.</summary>
public sealed class InvalidConfirmPartProcessingJobRequestException(string reason) : Exception(reason);
