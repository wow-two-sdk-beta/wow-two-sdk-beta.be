# Security (Envelope Cryptography) — spec

*Last updated: 2026-10-30*

> **Concrete API + usage.** Updated when public surface changes.

NuGet: `WoW.Two.Sdk.Beta.Be` (mono-lib) · namespace `WoW.Two.Sdk.Beta.Be.Foundation.Security`.

## Quick start

```csharp
using WoW.Two.Sdk.Beta.Be.Foundation.Security;

builder.Services.AddEnvelopeCryptography();                                   // env-var key source (MASTER_KEY)
builder.Services.AddEnvelopeCryptography(o => o.MasterKeyEnvironmentVariable = "APP_MASTER_KEY");

// at startup:
app.Services.GetRequiredService<ISealService>().Unseal();

// or read the key file on every use:
builder.Services.AddFileMasterKeyBroker(o => o.Path = "/srv/app/root.key");
```

## Public API

### `SecurityServiceCollectionExtensions`

| Method | Returns | Notes |
|---|---|---|
| `AddEnvelopeCryptography(this IServiceCollection, Action<EnvelopeCryptographyOptions>? configure = null)` | `IServiceCollection` | Registers `IValueCipher`, `ISealService`, and the default `IMasterKeyBroker` as singletons (all `TryAdd*`). Idempotent. |
| `AddFileMasterKeyBroker(this IServiceCollection, Action<FileMasterKeyBrokerOptions>? configure = null)` | `IServiceCollection` | Registers `FileMasterKeyBroker` as `IMasterKeyBroker` (replacing the env broker in either order), `MasterKeyProofTracker` and `MasterKeyUnsealService` — singletons. Section `EnvelopeCryptography:KeyFile`; `Path` must be absolute. |

### `IValueCipher` (default `ValueCipher`, singleton)

| Member | Returns | Notes |
|---|---|---|
| `Encrypt(byte[] plaintext, byte[] dataKey, string associatedData)` | `EncryptedPayload` | AES-256-GCM under the DEK; AAD authenticated, not encrypted. |
| `Decrypt(EncryptedPayload payload, byte[] dataKey, string associatedData)` | `byte[]` | Throws `AuthenticationTagMismatchException` on tamper / wrong key / wrong AAD. |

### `ISealService` (default `SealService`, singleton)

| Member | Returns | Notes |
|---|---|---|
| `IsSealed` | `bool` | `true` until the KEK is loaded. |
| `Unseal()` | `void` | Loads the KEK via `IMasterKeyBroker`; no-op (stays sealed) when none configured; throws `MasterKeyFormatException` when malformed. |
| `GenerateDataKey()` | `byte[]` | Fresh random 256-bit DEK. |
| `WrapDataKey(byte[] dataKey, string associatedData)` | `EncryptedPayload` | Encrypts the DEK under the KEK. Throws `InvalidOperationException` if sealed. |
| `UnwrapDataKey(EncryptedPayload wrapped, string associatedData)` | `byte[]` | Decrypts the DEK. Throws if sealed / AAD mismatch. |

### `IMasterKeyBroker` (default `EnvironmentMasterKeyBroker`, singleton)

| Member | Returns | Notes |
|---|---|---|
| `Location` | `string` | Where the key is read from — `env:{variable}` or the file path; safe to show. |
| `Read()` | `MasterKeyRead` | Reads now: a fresh key copy the caller clears, or the problem; the stamp changes with the content. |
| `CheckAccess()` | `string?` | `missing` / `unreadable` / `exposed` without reading the key; `null` passes. |
| `TryLoadKey()` | `byte[]?` | `null` when no key configured; throws `MasterKeyFormatException` on bad base64 / wrong length. Default: from `Read()`. |

### `FileMasterKeyBroker` (`IMasterKeyBroker`, singleton)

`FileMasterKeyBroker(string path)` — one base64 256-bit line; over 1 KiB, empty or not 32 bytes → `invalid`; a file the
process may not read → `missing`; stamp `"{lastWriteTicks}:{length}"`; `CheckAccess()` → `missing` / `unreadable` /
`exposed` (group or others may open it), `null` when owner-only. `TryLoadKey()` throws `MasterKeyFormatException` on
`invalid`, returns `null` otherwise without a key.

### `MasterKeyRead` (sealed record)

Init properties `Key` (`byte[]?`), `Problem` (`string?`), `Stamp` (`required string`). Problem codes: `Missing` `"missing"`, `Invalid` `"invalid"`,
`Unreadable` `"unreadable"`, `Exposed` `"exposed"`, `Mismatch` `"mismatch"`. `ToString()` never prints the key.

### `MasterKeyUnsealService` (singleton) · `MasterKeyProofTracker` (singleton)

| Member | Returns | Notes |
|---|---|---|
| `UnsealAsync(Func<byte[], CancellationToken, Task<bool>> prove, CancellationToken)` | `Task<MasterKeyRead>` | Reads the key; proves a new stamp once; a failed proof (or `CryptographicException`) clears the key → `mismatch`; any other exception clears it and propagates. |
| `IsProven(stamp)` / `Prove(stamp)` / `Forget()` | | The one stamp a proof accepted; holds no key. |

### `ValueCipherExtensions` (on `IValueCipher`)

| Member | Returns | Notes |
|---|---|---|
| `EncryptText(string value, byte[] dataKey, string associatedData)` | `EncryptedPayload` | UTF-8; plaintext bytes cleared. |
| `DecryptText(EncryptedPayload payload, byte[] dataKey, string associatedData)` | `string` | Throws `CryptographicException` on a wrong key / AAD / bytes. |
| `CreateCanary(byte[] masterKey, string keyContext, string valueContext)` | `KeyCanary` | Random 32-byte key wrapped under the master key; random 32-byte value sealed under it; its SHA-256. |
| `VerifyCanary(KeyCanary canary, byte[] masterKey, string keyContext, string valueContext)` | `bool` | Fixed-time digest compare; `false` on any decrypt failure. |

`KeyCanary` (sealed record): `WrappedKey`, `Value` (`EncryptedPayload`), `Digest` (`byte[]`).

### `KeyFingerprintHasher` · `ValueFingerprintHasher`

| Member | Returns | Notes |
|---|---|---|
| `new KeyFingerprintHasher(label).Hash(ReadOnlySpan<byte> key)` | `string` | First 20 hex of SHA-256(label UTF-8 ‖ 0x00 ‖ key), groups of four; `Pattern` matches it. |
| `new ValueFingerprintHasher(purpose).Hash(string value, byte[] dataKey)` | `string` | First 16 hex of HMAC-SHA256(HKDF-SHA256(dataKey, 32, info: purpose), value UTF-8). |

### `EncryptedPayload` (sealed record)

Positional: `Nonce` (12 bytes), `Tag` (16 bytes), `CipherText` (`byte[]`). Persist all three together.

### `EnvelopeCryptographyOptions` (sealed record · section `EnvelopeCryptography`)

| Member | Type | Default | Notes |
|---|---|---|---|
| `MasterKeyEnvironmentVariable` | `string` | `"MASTER_KEY"` | Env var holding the base64 256-bit KEK (used by the default provider). |

### `FileMasterKeyBrokerOptions` (sealed record · section `EnvelopeCryptography:KeyFile`)

| Member | Type | Default | Notes |
|---|---|---|---|
| `Path` | `string` | `""` | The key file's absolute path; required. |

### `MasterKeyFormatException` (sealed, `Exception`)

Thrown for a present-but-malformed key. Message is key-free (safe to log). A *missing* key is not an error.

## Custom key source (KMS / HSM)

```csharp
internal sealed class KmsMasterKeyBroker(IKmsClient kms) : IMasterKeyBroker
{
    public string Location => "kms:app-kek";
    public string? CheckAccess() => kms.IsProvisioned ? null : MasterKeyRead.Missing;
    public MasterKeyRead Read() => kms.Decrypt(/* wrapped KEK */) is { } key
        ? new MasterKeyRead { Key = key, Stamp = kms.KeyVersion }
        : new MasterKeyRead { Problem = MasterKeyRead.Missing, Stamp = "" };
}

builder.Services.AddSingleton<IMasterKeyBroker, KmsMasterKeyBroker>(); // before AddEnvelopeCryptography
builder.Services.AddEnvelopeCryptography();                               // TryAdd keeps yours
```

## Associated-data (AAD) patterns

AAD is a consumer choice, passed per call and bound into the tag. Typical:

- Wrapped DEK → `"owner:{ownerId}"` (binds a DEK to its owner; can't be replayed under another).
- Value → `"{ownerId}/{recordKey}"` (binds ciphertext to its logical slot).

## Notes

- Random 96-bit nonce per `Encrypt` (never reused); 128-bit tag (max GCM strength).
- DEK plaintext is the caller's to clear (`CryptographicOperations.ZeroMemory`); the KEK is cleared on `SealService.Dispose`, and a key from `IMasterKeyBroker.Read()` is the caller's to clear.
- Byte parity with stored data: `Foundation.Tests/Security/EnvelopeParityVectorTests` pins payloads, a canary and fingerprints captured from the code these blocks were extracted from.
- BCL-only (`System.Security.Cryptography`); no third-party crypto.

## See also

- Folder lead: [`security.md`](./security.md)
- Underlying lib: [`System.Security.Cryptography.AesGcm`](https://learn.microsoft.com/dotnet/api/system.security.cryptography.aesgcm)
