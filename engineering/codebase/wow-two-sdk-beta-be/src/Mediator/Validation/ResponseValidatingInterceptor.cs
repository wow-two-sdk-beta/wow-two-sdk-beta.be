using WoW.Two.Sdk.Beta.Be.Foundation.Errors;
using WoW.Two.Sdk.Beta.Be.Foundation.Results;
using WoW.Two.Sdk.Beta.Be.Foundation.Validation;

namespace WoW.Two.Sdk.Beta.Be.Mediator.Validation;

internal sealed class ResponseValidatingInterceptor<TRequest, TResponse>(
    IEnumerable<IValidator<TResponse>> validators)
    : IRequestInterceptor<TRequest, TResponse>
    where TRequest : notnull
{
    public async ValueTask<TResponse> HandleAsync(
        TRequest request,
        RequestHandlerDelegate<TResponse> nextStep,
        CancellationToken cancellationToken)
    {
        var response = await nextStep().ConfigureAwait(false);

        if (response is IResult { IsSuccess: false })
        {
            return response;
        }

        if (validators.Any(validator => validator.Validate(response) is not null))
        {
            throw AppErrorFactory.Unexpected("The operation produced an invalid response.").ToException();
        }

        return response;
    }
}
