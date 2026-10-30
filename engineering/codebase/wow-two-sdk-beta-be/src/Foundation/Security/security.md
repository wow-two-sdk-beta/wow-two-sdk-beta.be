# Foundation.Security

Envelope cryptography — AES-256-GCM authenticated encryption with a two-tier key model. A long-lived
master key (KEK) wraps many short-lived data keys (DEKs); each DEK encrypts the actual values. The
master-key source is abstracted (env var or file now, KMS/HSM later). Application-agnostic — no domain vocabulary.

| Seam | Default | Role |
|---|---|---|
| `IValueCipher` | `ValueCipher` | Encrypt/decrypt a value under a supplied DEK + AAD (data plane); text and canary extensions |
| `ISealService` | `SealService` | Hold the KEK in memory from start; generate, wrap, unwrap DEKs (key plane) |
| `IMasterKeyBroker` | `EnvironmentMasterKeyBroker` · `FileMasterKeyBroker` | Where the KEK comes from, read anew per call with a content stamp; `ISealService` loads it once |

## Held key — load once

```csharp
builder.Services.AddEnvelopeCryptography(o => o.MasterKeyEnvironmentVariable = "MASTER_KEY");

// at startup, after the host is built:
app.Services.GetRequiredService<ISealService>().Unseal();   // loads the KEK into memory

// encrypt a value (per-record DEK, wrapped under the KEK):
var dek     = seal.GenerateDataKey();
var wrapped = seal.WrapDataKey(dek, $"owner:{ownerId}");              // store wrapped beside the data
var payload = cipher.Encrypt(plaintext, dek, $"{ownerId}/{recordKey}"); // store payload
CryptographicOperations.ZeroMemory(dek);                               // clear the plaintext DEK

// decrypt:
var dek2      = seal.UnwrapDataKey(wrapped, $"owner:{ownerId}");
var recovered = cipher.Decrypt(payload, dek2, $"{ownerId}/{recordKey}");
CryptographicOperations.ZeroMemory(dek2);
```

## Re-read key — read per use, prove once per content

For a key file the owner places, rotates or removes while the host runs: nothing holds the key between uses.

```csharp
builder.Services.AddEnvelopeCryptography();
builder.Services.AddFileMasterKeyBroker(o => o.Path = "/srv/app/vault/root.key");   // EnvelopeCryptography:KeyFile

public sealed class Vault(MasterKeyUnsealService unsealer, IValueCipher cipher, IVaultStore store)
{
    public async Task<string?> ReadAsync(Guid owner, string key, CancellationToken ct)
    {
        var read = await unsealer.UnsealAsync(ProveAsync, ct);
        if (read.Key is not { } root)
            return null;                                              // read.Problem: missing / invalid / mismatch
        try
        {
            var dek = cipher.Decrypt(await store.WrappedKeyAsync(owner, ct), root, $"app/owner-key/{owner}");
            try { return cipher.DecryptText(await store.ValueAsync(owner, key, ct), dek, $"app/{owner}/{key}"); }
            finally { CryptographicOperations.ZeroMemory(dek); }
        }
        finally { CryptographicOperations.ZeroMemory(root); }
    }

    // A canary made on the first write proves later keys; an empty store accepts any key.
    private async Task<bool> ProveAsync(byte[] root, CancellationToken ct) =>
        await store.CanaryAsync(ct) is not { } canary || cipher.VerifyCanary(canary, root, "app/canary-key", "app/canary");
}
```

- `FileMasterKeyBroker` — one base64 line of 256 bits (`openssl rand -base64 32`); the stamp is write time and size;
  `CheckAccess()` names a `missing`, `unreadable` or `exposed` file (group or others may open it). `ISealService` loads
  the same file once.
- `EnvironmentMasterKeyBroker` reads per call too; its stamp is a short labelled hash of the variable's value.
- `MasterKeyUnsealService` — reads per use; a content proven once (`MasterKeyProofTracker`) skips the proof until the
  source changes; a key that fails its proof is cleared and reads `mismatch`.
- `KeyCanary` — `cipher.CreateCanary(root, keyContext, valueContext)` on the first write, `VerifyCanary` later.
- `new KeyFingerprintHasher(label).Hash(key)` — `aeb1 80f7 9014 1a39 9680`: 80 bits of SHA-256 over label, zero byte,
  key, to compare an offline copy by eye.
- `new ValueFingerprintHasher(purpose).Hash(value, dek)` — 16 hex of HMAC-SHA256 under an HKDF key from the DEK: equal
  values match without a testable guess.

## Notes

- **AAD is yours to choose.** The module takes associated-data as a parameter and binds it into the GCM
  tag (authenticated, not encrypted). A common pattern: `"owner:{id}"` for the wrapped DEK and
  `"{owner}/{key}"` for the value — so a blob can't be replayed under a different owner or key.
- **Contexts and labels are data formats.** Ciphertext, canaries and fingerprints written under one AAD, label or
  purpose open only under the same string — never change one for stored data.
- **Random 96-bit nonce per encryption**, never reused under one key; 128-bit tag. Decrypt throws
  `AuthenticationTagMismatchException` on any tamper / wrong key / wrong AAD — never returns corrupt bytes.
- **Sealed until unsealed.** `ISealService` with no key stays sealed (wrap/unwrap throw). A malformed key throws
  `MasterKeyFormatException` (key-free message, safe to log). Restart → sealed again.
- **Swap the key source** by registering your own `IMasterKeyBroker` before `AddEnvelopeCryptography`
  (it uses `TryAddSingleton`); `AddFileMasterKeyBroker` replaces the broker in either order. A broker implements
  `Location`, `Read()` and `CheckAccess()`; `TryLoadKey()` defaults to `Read()`.
- Parity: `EnvelopeParityVectorTests` pins payloads, a canary and fingerprints written before these blocks moved here.

## See also

- Spec: [`Security.spec.md`](./Security.spec.md)
- Underlying primitive: [`System.Security.Cryptography.AesGcm`](https://learn.microsoft.com/dotnet/api/system.security.cryptography.aesgcm)
