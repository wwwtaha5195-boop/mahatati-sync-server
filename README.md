# Mahatati Sync Server

This small HTTPS API is a central transport for the same version-2 envelopes used by offline synchronization. The desktop remains the financial authority. The server never joins records by local database IDs.

Set `MAHATATI_SYNC_API_KEY` to a secret with at least 16 characters, optionally set `MAHATATI_SYNC_DATA`, and publish behind a valid HTTPS endpoint. Both applications must use the same URL and key.

Results remain available on the server and may be downloaded repeatedly. Desktop uniqueness constraints on `SyncId` make repeated downloads idempotent.

## Render deployment

Deploy this directory as a Docker Web Service. Configure:

- Health check path: `/health`
- `MAHATATI_SYNC_API_KEY`: a private secret with at least 16 characters
- `MAHATATI_SYNC_DATA`: `/var/data`
- Persistent disk mount path: `/var/data` (1 GB is sufficient initially)

Do not use an ephemeral filesystem for production synchronization data.

For local Visual Studio testing, the included launch profile uses
`mahatati-local-development-key` and opens `http://localhost:5089/health`.
This local-only key must never be used for the public Render service.
