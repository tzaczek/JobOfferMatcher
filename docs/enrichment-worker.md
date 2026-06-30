# Running AI enrichment — the `/enrich` worker

How to (re-)generate offer summaries, the CV profile, and fit scores. These are **AI outputs**, but the
backend makes **no** external AI call — *you*, running Claude Code under your Max plan, are the worker.
Un-produced items show **"pending"**, never a fabricated value.

## TL;DR

1. `./start.ps1` — app on `http://localhost:5180` (host-process mode).
2. In Claude Code, in this repo, run `/enrich`.
3. Done when `GET /api/enrichment/status` → `"pendingTotal": 0`.

Safe to re-run anytime — it's idempotent (staleness is keyed by an input hash, so only changed inputs
are reprocessed).

## Why host-process mode, not the container (ADR-4)

Enrichment **must** run against the **host process** (`./start.ps1`, port **5180**), not the full
`docker compose` `app` container (port 8080):

- `/api/enrichment/*` is **fail-closed loopback-only**. A request from a host Claude session to the
  container's published port arrives with a non-loopback source IP → **403**.
- The worker reads the CV **PDF by local path**; the container's CVs live inside the `jobs_cvdata`
  Docker volume, which the host worker can't read.

The container on `:8080` is fine for **viewing** the feed — it shares the same Postgres, so results the
host worker writes show up there on refresh.

## Steps

```powershell
# 1. Start the host app (Postgres in Docker + dotnet run on :5180).
./start.ps1
```

```text
# 2. In Claude Code (this repo), run the worker slash command:
/enrich
```

It loops: `GET /api/enrichment/pending` → produces each output **in-session** (reading the CV PDF
directly for the profile) → `POST /api/enrichment/results`, until the queue is drained.

```powershell
# 3. Verify it drained.
Invoke-RestMethod http://localhost:5180/api/enrichment/status   # → pendingTotal: 0, failedTotal: 0
```

You can also watch the feed (`localhost:5180` or the container's `localhost:8080`) — `pending` chips
flip to produced summaries, key-skill chips, and fit scores with matched/missing + a rationale.

## When work goes "pending" again (just re-run `/enrich`)

| Trigger | What goes pending |
|---|---|
| A scan collects new offers | their summaries + fits |
| An offer's description/company/location changes | that offer's summary + fit |
| A CV is uploaded or replaced | that CV's profile; **all** fits |
| Fit weights or preferences change (Settings) | **all** fits |

The feed's **Re-run** button (`POST /api/enrichment/rerun`, `scope: failed|all`) re-arms work in-app;
it does **not** call AI — you still run `/enrich` to produce the results.

## Gotcha: CV uploaded through the container

If you uploaded the CV via the container UI (`:8080`), its PDF is in the `jobs_cvdata` volume, not on
the host. Either **re-upload it through the host app** (`:5180`) so it lands on disk, or copy it out
and point the host app at it:

```powershell
mkdir -Force C:\Users\tomas\Repo\Job\cv-data
docker cp jobs-app:/app/cv-data/. C:\Users\tomas\Repo\Job\cv-data
$env:Cv__StoragePath = "C:\Users\tomas\Repo\Job\cv-data"
dotnet run --project backend/src/Web    # serves :5180 with the copied CVs readable
```

## Draining a large backlog faster (optional)

A single `/enrich` pass is sequential. For a big first-time backlog (e.g. ~180 offers ⇒ summaries +
fits) you can run **several worker shards in parallel** — each fetches a page and processes only the
items at positions where `position % N == shardIndex`. The server's write-back is **idempotent by
input hash**, so any overlap is harmless. (In Claude Code this is what a small `Workflow` fan-out of
`/enrich`-style worker agents does.)

## Privacy (FR-012 / SC-005)

The backend imports no AI SDK and makes no outbound AI call. The CV **binary** never leaves disk (read
by path); CV/offer **text** crosses only the **loopback** enrichment channel to your own Claude
session. Nothing is transmitted to an external service.

See also: [`README.md` → AI enrichment](../README.md), and `specs/002-llm-enrichment-matching/`
(`contracts/worker-protocol.md`, `contracts/enrichment-api.md`, ADR-4 in `plan.md`).
