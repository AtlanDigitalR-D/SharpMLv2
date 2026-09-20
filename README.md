# SharpML 2.0

SharpML 2.0 is a defensive rewrite of the original SharpML proof of concept. The original project mines readable file shares, passes collected text through a bundled ML model, and can then attempt Active Directory authentication with candidate username/password pairs. This version deliberately removes the authentication stage and turns the project into a read-only secret-exposure triage scanner.

Read the companion blog post (here)[https://www.atlan.digital/lab/sharpmlv2-jev-ai-soc]

## What changed

The pipeline is now:

```text
Readable directory / UNC share
        |
        v
Bounded text-file scanner
        |
        v
Deterministic candidate detectors
(password assignments, API keys, tokens, private-key headers, etc.)
        |
        v
Immediate redaction + local metadata
(length, entropy, placeholder signal, ephemeral fingerprint)
        |
        +--------------------------+
        |                          |
        v                          v
Local heuristic score       Optional Jev decisions
        |                          |
        +------------+-------------+
                     v
              Hybrid triage score
                     |
                     v
               JSONL findings
```

There is **no credential authentication, AD user harvesting, or password spraying** in this project.

## Why Jev belongs here

Jev is not asked whether an opaque hash or identifier is malicious. It is given evidence that actually contains classification signal: detector type, file type, value length/entropy, placeholder indicators, selected path hints, and redacted surrounding context.

One request asks three typed questions:

* `secret_type`: password / API key / token / connection string / private key / non-secret / uncertain
* `likely_real`: probability that the candidate represents real deployed authentication material rather than sample/test/documentation data
* `priority`: 0-4 analyst-review priority

The result is blended with deterministic local heuristics. Jev is therefore a semantic triage feature, not ground truth.

## Build

Requires .NET 8 SDK.

```powershell
dotnet build -c Release
```

The project has no third-party NuGet dependencies.

## Run locally

```powershell
dotnet run -- -r C:\Shares\Finance -o findings.jsonl
```

UNC shares work through the normal Windows filesystem APIs, assuming the current identity already has read access:

```powershell
dotnet run -- -r \\fileserver\deploy -o findings.jsonl
```

Useful limits:

```powershell
dotnet run -- -r \\fileserver\deploy `
  --max-file-mb 2 `
  --context-lines 2 `
  --max-candidates 5000 `
  --min-likelihood 0.35
```

## Jev mode

The public Jev playground documentation describes a `model + state + questions` typed-decision payload with `choice`, `score`, and `noul` answers. The Jev API hub currently also says production server calls/SDK access are coming soon, so SharpML 2.0 does **not** hard-code a production hostname. Supply the endpoint explicitly once you have an endpoint that accepts that typed-question payload.

Prefer environment variables for credentials:

```powershell
$env:JEV_ENDPOINT = "https://your-jev-endpoint.example/v1/decision"
$env:JEV_API_KEY = "..."
$env:JEV_MODEL = "typesafe/jev-1.13"

dotnet run -- -r \\fileserver\deploy --use-jev
```

You can also pass `--jev-endpoint`, `--jev-api-key`, and `--jev-model`. Avoid putting API keys directly into shell history when an environment variable is available.

### Data sent to Jev

Raw secret values are never included in the request. The external state contains only:

* coarse filename hints (not the actual filename)
* extension and file size
* line number and detector name
* key name such as `password`
* `<REDACTED:length=N>`
* value length and Shannon entropy
* placeholder heuristic
* coarse path hints (`prod`, `test`, `docs`, etc.)
* sanitized nearby text
* last-write timestamp

## Output

Each report line is one JSON object. Example shape:

```json
{
  "candidate": {
    "file_path": "C:\\Shares\\Finance\\prod\\database.ini",
    "extension": ".ini",
    "line_number": 4,
    "detector": "password_assignment",
    "key_name": "password",
    "masked_value": "<REDACTED:length=38>",
    "value_fingerprint": "ephemeral-per-process",
    "value_length": 38,
    "entropy": 4.1,
    "looks_placeholder": false,
    "context": ["username=svc_demo", "password=<REDACTED>" ]
  },
  "classification": {
    "secret_type": "password",
    "likely_real": 0.82,
    "priority_score": 3.2,
    "confidence": 0.76,
    "source": "jev+heuristic"
  }
}
```

The fingerprint is an HMAC with a random process-local key. It supports deduplication during one run but is not stable across runs and does not create a reusable unsalted password-hash corpus.

## Detection scope

Current deterministic detectors cover common password/passphrase assignments, API/client secrets, token assignments, connection-string passwords, AWS access-key IDs, GitHub token forms, and private-key headers. Candidate generation is intentionally conservative and should be extended with organization-specific formats rather than relying on Jev to inspect every line.

Supported text-oriented extensions include common shell/script languages, config formats, `.env`, JSON/YAML/XML, Terraform, SQL and source files. Files larger than the configured limit are skipped.


