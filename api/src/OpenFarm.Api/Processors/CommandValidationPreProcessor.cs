using FastEndpoints;
using FluentValidation;

namespace OpenFarm.Api.Processors;

public sealed class CommandValidationPreProcessor : IGlobalPreProcessor
{
    public async Task PreProcessAsync(IPreProcessorContext context, CancellationToken ct)
    {
        var request = context.Request;
        if (request is null)
        {
            return;
        }

        var validatorType = typeof(IValidator<>).MakeGenericType(request.GetType());
        if (context.HttpContext.RequestServices.GetService(validatorType) is not IValidator validator)
        {
            return;
        }

        var result = await validator.ValidateAsync(new FluentValidation.ValidationContext<object>(request), ct);

        if (!result.IsValid)
        {
            context.ValidationFailures.AddRange(result.Errors);
            await context.HttpContext.Response.SendErrorsAsync(context.ValidationFailures, cancellation: ct);
        }
    }
}
