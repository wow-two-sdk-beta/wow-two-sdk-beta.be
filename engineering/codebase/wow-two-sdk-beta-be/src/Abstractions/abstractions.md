# WoW.Two.Sdk.Beta.Be.Abstractions — kernel

> The BCL-only seam core and every domain contract share. Package `WoW2.Sdk.Beta.Be.Abstractions`; core
> (`WoW2.Sdk.Beta.Be`) depends on it, so a product that references core needs no extra reference.

| Folder | Types | Namespace (unchanged by the move) |
|---|---|---|
| [`Foundation/Results/`](./Foundation/Results/results.md) | `Result`, `Result<T>`, `Result<TSuccess, TFailure>`, `IResult`, `AttemptExtensions`, `ResultCombinatorExtensions`, `ResultExtensions` | `…Foundation.Results` |
| `Foundation/Errors/` | `AppError`, `AppAggregateError`, `AppErrorType`, `AppErrorFactory`, `AppException`, `ErrorMessageConstants`, `ErrorNature`, `ErrorOrigin`, `ExceptionChainMapper` | `…Foundation.Errors` |
| `Messaging/` | `IEvent` | `…Messaging` |
| `Identity/CurrentUser/` | `ICurrentUserService`, `UserKind` | `…Identity.CurrentUser` |

- References nothing: no `FrameworkReference`, no `PackageReference`; the release verifier fails a kernel nuspec with a dependency.
- Admits a type only when it is BCL-only and core's own code reads it or the contracts of two repos use it
  ([domain structure § Kernel](../../../../../../../../conventions/development/repo/structure/domain-structure.md#kernel)).
- A moved type keeps its namespace; core forwards it in [`../Properties/KernelTypeForwarders.cs`](../Properties/KernelTypeForwarders.cs),
  so binaries built against an older core keep binding.
- Versions in lockstep with core and publishes with the family.
- Stays in core: the exception mapper, mapping rules, nature mapper and their DI ([`Foundation/Errors`](../Foundation/Errors/errors.md));
  the bus ([`Messaging`](../Messaging/Messaging.md)). `CookieCurrentUserService` and `AddCurrentUser` ship in the
  Identity Auth package ([wow-two-sdk-beta.identity](https://github.com/wow-two-sdk-beta/wow-two-sdk-beta.identity)).
- A domain's `{Domain}.Abstractions` contract references this package only.
