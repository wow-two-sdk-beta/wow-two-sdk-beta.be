using WoW.Two.Sdk.Beta.Be.Foundation.Errors;

namespace WoW.Two.Sdk.Beta.Be.Web.ErrorMapping;

/// <summary>Defines mapping an <see cref="AppError"/> to an HTTP status code.</summary>
public interface IErrorHttpStatusCodeMapper
{
    /// <summary>Maps <paramref name="error"/> to an HTTP status code.</summary>
    /// <param name="error">The error to map.</param>
    int ToStatusCode(AppError error);
}
