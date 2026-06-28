# 0002 — Source access via the justjoin.it public JSON API (accepted risk)

**Status:** Accepted (ADR-2 in `plan.md`; corrected by adversarial review — `research.md` §1).
Load-bearing.

## Context

Offers are collected from justjoin.it. It exposes a **public, unauthenticated JSON API** on
`api.justjoin.it` (list: `GET /v2/user-panel/offers/by-cursor`; detail: `GET /v1/offers/{slug}`),
which gives stable ids, structured salary split by employment basis, and dates directly — far
more reliable than HTML/RSC scraping, and no login is required.

However, *reachable ≠ permitted*. The adversarial review found that
`api.justjoin.it/robots.txt` contains a `User-agent: *` group ending in `Disallow: /` with a
small allowlist (`/sitemap`, `/pricing`, `/login`, `/register`) that does **not** include the
offer endpoints; the `/v2/user-panel/…` path signals an internal endpoint; and the
Regulamin/ToS may restrict automation. The endpoints return HTTP 200 unauthenticated, but that
does not by itself mean automated access is sanctioned.

## Decision

Collect via the public JSON API using a server-side `HttpClient`, behind a pluggable
**`IJobSource`** port (`JustJoinItSource`), and **accept this as a deliberate, mitigated risk**
appropriate for a single-user, low-volume, **local**, personal tool. Mitigations:

- **Polite pacing** ~1 request/second, sequential, with Polly backoff on 429/5xx
  (Cloudflare-cache-friendly).
- **Detail fetched only for new/changed offers** — list payloads already carry most fields — to
  minimise request volume.
- A **generic, non-PII `User-Agent`** (no name or email — Principle IV).
- **No redistribution** of collected data; it stays on the machine.
- A **built-in escalation switch** to the deferred manual-login browser path on a 403/challenge
  (FR-040); the source adapter is swappable behind `IJobSource` if access terms change.

## Consequences

- The primary collection path is lightweight (no browser, no credentials) and the design degrades
  gracefully to a manual-login flow rather than scraping harder.
- **Action for the user:** review the justjoin.it Regulamin/ToS; this access is an accepted risk,
  not a verified permission.
- If the source begins blocking direct access, the escalation trigger is already built; the heavy
  Playwright adapter is deferred (Principle X) until a source actually requires it.
