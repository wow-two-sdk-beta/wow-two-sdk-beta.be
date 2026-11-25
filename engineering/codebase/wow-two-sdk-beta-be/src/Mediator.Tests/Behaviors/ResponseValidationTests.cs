using AwesomeAssertions;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using WoW.Two.Sdk.Beta.Be.Foundation.Errors;
using WoW.Two.Sdk.Beta.Be.Foundation.Validation;
using WoW.Two.Sdk.Beta.Be.Mediator.Cqrs;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;
using WoW.Two.Sdk.Beta.Be.Mediator.Validation;
using Xunit;

namespace WoW.Two.Sdk.Beta.Be.Mediator.Tests.Behaviors;

public sealed class ResponseValidationTests
{
    [Theory]
    [InlineData("valid", null)]
    [InlineData("", AppErrorType.Unexpected)]
    [InlineData(null, AppErrorType.NotFound)]
    public async Task SendAsync_ShouldValidateSuccessfulResponsesOnly(string? text, AppErrorType? expectedError)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMediator(typeof(ResponseValidationTests).Assembly);
        services.AddFluentValidatorsFromAssemblies(typeof(ResponseValidationTests).Assembly);
        services.AddMediatorValidatingInterceptor();

        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();
        var response = await sender.SendAsync(new ReadTextQuery(text));

        if (expectedError is null)
        {
            response.Should().BeOfType<AppResult<TextModel>.Success>().Which.Data.Text.Should().Be(text);
            return;
        }

        var error = response.Should().BeOfType<AppResult<TextModel>.Failure>().Which.Error;
        error.Type.Should().Be(expectedError.Value);
        error.Message.Should().NotContain("private validation detail");
    }

    internal sealed record TextModel(string Text);

    internal sealed record ReadTextQuery(string? Text) : IQuery<AppResult<TextModel>>;

    internal sealed class ReadTextQueryHandler : IQueryHandler<ReadTextQuery, AppResult<TextModel>>
    {
        public ValueTask<AppResult<TextModel>> HandleAsync(ReadTextQuery query, CancellationToken cancellationToken)
        {
            var result = query.Text is { } text
                ? AppResult<TextModel>.Ok(new TextModel(text))
                : AppResult<TextModel>.Fail(AppErrorFactory.NotFound());

            return ValueTask.FromResult(result);
        }
    }

    internal sealed class TextResponseValidator : AbstractValidator<AppResult<TextModel>>
    {
        public TextResponseValidator()
        {
            RuleFor(response => response)
                .Must(response => response is AppResult<TextModel>.Success { Data.Text.Length: > 0 })
                .WithMessage("private validation detail");
        }
    }
}
