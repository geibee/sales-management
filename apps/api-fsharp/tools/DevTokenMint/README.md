# DevTokenMint

Local developer / integration-test JWT minting CLI. Signs tokens with the HS256
shared secret in `Authentication.SigningKey` so the API (with auth enabled) can be
exercised without standing up Keycloak.

## Usage

```
dotnet run --project tools/DevTokenMint -- --role operator --user u1
# → eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9....
```

Options:

- `--role <viewer|operator|admin>` (default `viewer`)
- `--user <username>` (default `dev-user`)
- `--ttl <duration>` — `30s`, `5m`, `1h`, `7d` (default `1h`)
- `--signing-key <key>` (also via env `Authentication__SigningKey`)
- `--audience <aud>` (default from `appsettings.json` or `sales-api`)
- `--issuer <iss>` (default `dev-token-mint`)

The signing key is resolved from (highest priority first):

1. `--signing-key`
2. env var `Authentication__SigningKey`
3. `src/SalesManagement/appsettings.json` / `appsettings.Development.json`

## Security

- **Do not use a production signing key.** The default key in
  `appsettings.Development.json` is for local development only.
- Tokens contain no real authorisation context; treat the output as sensitive
  enough to keep out of git (`.env.local`, etc.) but trivial to reissue.
