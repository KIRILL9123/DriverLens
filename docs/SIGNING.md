# Driver Index Signing Specification & Instructions

To protect the client from tampered driver metadata, all index shards fetched from the remote GitHub repository must be signed with an ECDSA P-256 (SHA-256) private key. The client embeds the corresponding public key and verifies the signature before parsing the payload.

---

## Developer Keypair Generation

To generate a new development or release keypair, run the `IndexSigner` tool:

```bash
dotnet run --project tools/DriverLens.IndexSigner -- --generate-keypair <outputDir>
```

This will output:
1. `devkey.private.pem` — The private key used for signing shards. **NEVER COMMIT THIS FILE.**
2. `devkey.public.pem` — The public key corresponding to the private key.
3. A formatted C# byte array literal printed to stdout.

### Updating the Client Public Key
Copy the printed byte array literal from stdout and paste it into [SignedIndexVerifier.cs](file:///C:/Users/kyrylo/Documents/antigravity/brave-fermi/src/DriverLens.Core/SignedIndexVerifier.cs) to overwrite the `TrustedPublicKey` array:

```csharp
public static readonly byte[] TrustedPublicKey = { 0x30, 0x59, ... };
```

---

## Signing Shards

Whenever you modify the raw, human-readable index shard at `index/shards/net.json`, you must re-sign it to update `index/shards/net.json.signed.json` using:

```bash
dotnet run --project tools/DriverLens.IndexSigner -- --sign index/shards/net.json devkey.private.pem index/shards/net.json.signed.json
```

This generates the signed wrapper containing the raw payload and the base64-encoded signature.

### PR and Review Guidelines
- Both `index/shards/net.json` (unsigned, human-diffable) and `index/shards/net.json.signed.json` (the signed payload used by the client) must be committed to the repository.
- During pull requests, reviewers should inspect and diff `index/shards/net.json` for additions or modifications. Do not review the raw signed JSON blob, as it is unreadable and its integrity is verified by the client cryptographically.

---

## Threat Model

### What This Protects Against
- **Repository Compromise**: If a malicious actor gains direct commit access to the main branch or bypasses pull request controls to inject drivers, they cannot sign the shard without the private key. The client will reject the shard as the signature will be invalid.
- **Malicious Pull Requests**: Pull requests containing modified index shards will only update the unsigned `.json` source. The signed `.signed.json` will not verify against the production public key if signed with an unauthorized key, preventing installation of unreviewed driver links.

### What This Does NOT Protect Against
- **Private Key Leakage**: If `devkey.private.pem` is leaked or compromised, the trust boundary is broken. A malicious actor can sign arbitrary indexes.
  
> [!CAUTION]
> If a private key compromise is suspected:
> 1. Regenerate a new keypair immediately.
> 2. Re-sign all shards with the new private key.
> 3. Ship a client update replacing the public key in `SignedIndexVerifier.TrustedPublicKey`.
