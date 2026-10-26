// Types that moved from this assembly into the BCL-only kernel WoW2.Sdk.Beta.Be.Abstractions
// (assembly WoW.Two.Sdk.Beta.Be.Abstractions, src/Abstractions/). Their namespaces are unchanged, so source keeps
// compiling; these forwarders keep binaries compiled against an older core binding, and keep assembly-qualified
// names written by an older core resolving. Nested types (Result.Success, Result<T>.Failure, …) forward with their
// containing type. A type added to the kernel by moving it out of this assembly gets a line here.
using System.Runtime.CompilerServices;
using WoW.Two.Sdk.Beta.Be.Foundation.Errors;
using WoW.Two.Sdk.Beta.Be.Foundation.Results;
using WoW.Two.Sdk.Beta.Be.Identity.CurrentUser;
using WoW.Two.Sdk.Beta.Be.Messaging;

// Foundation/Results
[assembly: TypeForwardedTo(typeof(IResult))]
[assembly: TypeForwardedTo(typeof(Result))]
[assembly: TypeForwardedTo(typeof(Result<>))]
[assembly: TypeForwardedTo(typeof(Result<,>))]
[assembly: TypeForwardedTo(typeof(AttemptExtensions))]
[assembly: TypeForwardedTo(typeof(ResultCombinatorExtensions))]
[assembly: TypeForwardedTo(typeof(ResultExtensions))]

// Foundation/Errors — value types; the exception mapper, mapping rules, nature mapper and their DI stay in core
[assembly: TypeForwardedTo(typeof(AppError))]
[assembly: TypeForwardedTo(typeof(AppAggregateError))]
[assembly: TypeForwardedTo(typeof(AppErrorType))]
[assembly: TypeForwardedTo(typeof(AppErrorFactory))]
[assembly: TypeForwardedTo(typeof(AppException))]
[assembly: TypeForwardedTo(typeof(ErrorMessageConstants))]
[assembly: TypeForwardedTo(typeof(ErrorNature))]
[assembly: TypeForwardedTo(typeof(ErrorOrigin))]
[assembly: TypeForwardedTo(typeof(ExceptionChainMapper))]

// Messaging
[assembly: TypeForwardedTo(typeof(IEvent))]

// Identity/CurrentUser — the seam only; CookieCurrentUserService and its registration ship in WoW2.Sdk.Beta.Be.Identity.Auth
[assembly: TypeForwardedTo(typeof(ICurrentUserService))]
[assembly: TypeForwardedTo(typeof(UserKind))]
