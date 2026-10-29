# Identifiers

`IIdGenerator` produces random text identifiers; `CryptographicIdGenerator` uses unbiased cryptographic selection.
Register `services.AddSingleton<IIdGenerator, CryptographicIdGenerator>()`; choose the product's length at the call site.
The default alphabet is `IdAlphabetConstants.Alphanumeric`; `HexLower` (4 bits a character) and `Base64Url` (6 bits)
suit generated secrets — `Generate(64, IdAlphabetConstants.HexLower)` carries 256 bits. Custom alphabets are distinct
URL-safe ASCII characters.
Length is 1..4096. Invalid length/alphabet is a configuration error. Storage must enforce uniqueness and bound collision retries.
