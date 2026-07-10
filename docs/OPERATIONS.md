# Operations guide (Phase 3)

## Architecture

```
Internet ──HTTPS──► Caddy (auto-TLS + HSTS) ──► API container ──► PostgreSQL
                                                   │
                                                   ├─ /app/Storage volume (DP keys, receipts if disk mode)
                                                   └─ S3-compatible bucket (receipts, recommended)
```

## Health & observability

- **`/health/live`** — process is up (point container healthchecks here).
- **`/health/ready`** — process + database reachable (point uptime monitoring
  here, e.g. UptimeRobot free tier at 5-minute intervals).
- **Logs:** Serilog structured console output; collect with `docker logs`,
  or the platform's log stream. Framework noise is filtered to warnings.
- **Errors:** set `Sentry__Dsn` (free tier) and unhandled exceptions land in
  Sentry with request context; 10% performance tracing.

## Hosting (UK region — a GDPR-friendly default for client books)

| Option | Fit |
|---|---|
| DigitalOcean LON1 droplet + Managed PostgreSQL | Simplest ops; managed PG has daily backups + 7-day PITR |
| Azure UK South (App Service or VM) + Azure Database for PostgreSQL Flexible | Most "enterprise" answer; PITR 7–35 days |
| AWS eu-west-2 (Lightsail/EC2) + RDS PostgreSQL | Fine if already AWS-familiar |

Start small: one VM running `docker-compose.prod.yml` with **managed** PostgreSQL
(swap the `db` service for the managed connection string). The bundled db
service is for the very first deployment only.

## Deployment

1. Server: any Ubuntu VM with Docker. Copy `docker-compose.prod.yml` and
   `deploy/Caddyfile`; create `.env` beside them (see the variable list in
   the compose file — never commit it).
2. DNS: point `api.yourdomain` at the server. Caddy obtains and renews TLS
   automatically and sends HSTS.
3. `docker compose -f docker-compose.prod.yml pull && docker compose -f docker-compose.prod.yml up -d`
4. CI publishes `ghcr.io/thematthewwalker/kestrel-books:latest` on every push
   to main — redeploy is step 3 again. (Staging = the same compose file on a
   second cheap VM or the same VM with a second .env/domain.)
5. **Mobile app:** set `API_BASE` to `https://api.yourdomain` — no port.

## Backups & the restore test (do this before real data, then quarterly)

Managed PG: enable PITR, then prove it:
1. Restore yesterday's point-in-time to a **new** instance.
2. Point a scratch API container at it (`ConnectionStrings__Default`).
3. Smoke test: log in, open a client, run a trial balance.
4. Note how long it took — that's your real recovery time. Delete the scratch.

Self-hosted PG fallback: nightly `pg_dump -Fc` to object storage +
`docker compose exec db pg_dump ...` in cron; test restores the same way.

Also back up: the `apistorage` volume (Data Protection keys — losing them
invalidates sessions and encrypted secrets) and the receipts bucket.

## Receipts storage

Set the `S3` config section to move receipt images to object storage
(Cloudflare R2 or Backblaze B2 are the cheap options; both are S3-compatible
and work with the built-in client). Disk mode is fine for a single server if
the volume is backed up.

## Incident runbook (one page)

1. **Down?** Check `/health/ready`. Container up (`docker ps`)? Logs
   (`docker logs api --tail 200`)? Database reachable?
2. **Bad deploy?** `docker compose -f docker-compose.prod.yml up -d` pinned
   to the previous image tag `ghcr.io/...:<old-sha>` — every push is tagged.
3. **Data problem?** Stop writes (scale api to 0), restore per the tested
   procedure above, investigate before reopening.
4. **Security event?** Rotate `Jwt__Key` (invalidates all sessions), revoke
   HMRC connections from the app, review `AuthEvents`, rotate any exposed
   credential. A pushed/pasted secret is always burned.
5. Tell affected users what happened and when it's fixed — a simple public
   status page (UptimeRobot's free one, or Instatus) beats silence.
