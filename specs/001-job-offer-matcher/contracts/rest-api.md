# Contract: REST API (localhost)

The ASP.NET Core host exposes a JSON API under `/api` on `localhost`, consumed by the React
SPA (typed client in `frontend/src/api`). Single-user, local — no auth layer (the surface is
bound to localhost). All bodies are JSON; times are ISO-8601 UTC. Commands return a result
envelope; expected failures map from Domain `Result<T>` to HTTP 4xx with a `problem` body, not
exceptions.

## Conventions

- Success: `200`/`201` with the resource or `{ "data": … }`.
- Expected failure (validation, bad cron, not-readable CV): `400`/`409` +
  `{ "error": { "code": "…", "message": "…" } }` (RFC-7807-ish).
- IDs in transport are string GUIDs; the backend maps to wrapped IDs at the boundary.

## Offers

### `GET /api/offers`
Query the current feed. Query params (all optional):
`status` (`new|all|active|interested|dismissed|viewed`; `active` = every status except
dismissed — the default feed), `source`, `workMode`, `sort`
(`rank|salary|fit|recency`, default `rank`), `availability` (`available|all`),
`q` (text), `applied` (`true|false` — keep only offers the user has / has not applied to).
Returns the **role-grouped** feed (one entry per `RoleGroup`, with members).

```jsonc
{
  "data": [{
    "offerId": "…", "roleGroupId": "…|null",
    "title": "Senior .NET Engineer", "company": "Acme",
    "location": "Kraków", "workMode": "remote", "employmentType": "b2b",
    "seniority": "senior",
    "salaryBands": [                         // RAW, verbatim (FR-008/010)
      { "min": 18000, "max": 22000, "currency": "PLN", "period": "monthly", "basis": "b2b", "tax": "net" }
    ],
    "normalizedSalary": {                    // DERIVED, secondary (research §7)
      "comparableMonthly": { "amount": 16600, "currency": "PLN" },
      "quality": "Estimated",
      "assumptions": ["midpoint 18000–22000 = 20000", "B2B→Permanent-gross ×0.85"]
    },
    "fit": {                                 // null if no readable CV (FR-026)
      "score": 99,
      "matched": ["C#", ".NET", "Azure", "seniority meets", "remote", "B2B", "salary in band"],
      "missing": []
    },
    "canonicalUrl": "https://justjoin.it/job-offers/…",
    "isNew": true, "availability": "available",
    "firstSeenAt": "…", "firstSuggestedAt": "…", "lastSeenAt": "…",
    "userStatus": "new",
    "applied": false,                        // user marked "I applied" (orthogonal to userStatus)
    "appliedAt": null,                       // optional ISO-8601 date; null if not recorded
    "applicationNote": null                  // optional free-text note (≤ 2000 chars)
  }],
  "meta": { "total": 177, "new": 12, "noReadableCv": false }
}
```

### `GET /api/offers/{id}`
Full offer incl. sanitized `descriptionHtml`, version history, and event timeline.

### `POST /api/offers/{id}/status`
Body `{ "status": "interested|dismissed|viewed" }`. Appends a `StatusChanged` event;
`dismissed` persists across scans and never re-appears as new (FR-031/SC-002).

### `PUT /api/offers/{id}/application`
Mark the offer as applied-to (or edit the recorded date/note); idempotent on the flag.
Body `{ "appliedAt": "2026-06-20"|null, "note": "…"|null }` — both optional (`appliedAt`
ISO-8601; an invalid date → `400 InvalidDate`; a note over 2000 chars → `400
ApplicationNoteTooLong`). Appends an `Applied` event. Orthogonal to `userStatus` and
persists across scans. → `204`.

### `DELETE /api/offers/{id}/application`
Clear the applied flag and its date/note (un-apply). Appends an `ApplicationCleared`
event. Idempotent. → `204`.

### `POST /api/role-groups/{id}/override`
Body `{ "override": "same|notSame" }` — user correction to cross-source grouping (FR-016).

## Sources (FR-002/003)

- `GET /api/sources` — list configured sources.
- `POST /api/sources` / `PUT /api/sources/{id}` — create/edit. Body includes editable
  `searchCriteria` (filter params) + `requiresLogin` + `enabled`. **No code change to add a
  source's search** (FR-002).
- `POST /api/sources/{id}/enable` · `/disable`.

## Scans (FR-017/018/020)

### `POST /api/scans/run`
Trigger an on-demand scan. Body `{ "sourceIds": ["…"] | null }` (null = all enabled).
Returns `{ "scanRunId": "…" }`. Calls the **same** `IScanRunner` as the scheduler.
If a scan is already running, returns `409 { error.code: "ScanInProgress" }` (single-flight).

### `GET /api/scans` / `GET /api/scans/{id}`
Run history with counts + outcome + `incompleteReason` (FR-020/036).

### `GET /api/scans/{id}/status`  *(polling — default UI transport)*
```jsonc
{ "state": "running|waiting_for_login|challenge_detected|completed|incomplete",
  "outcome": "complete|partial|failed|null",
  "counts": { "collected": 177, "new": 12, "updated": 3, "unavailable": 1, "failed": 0 },
  "incompleteReason": null }
```
The SPA polls this (Principle X); an optional SignalR hub may push the same transitions later.

### Manual-login handshake (deferred adapter; contract defined now)
- `POST /api/scans/{id}/continue` — user clicks **Done** after logging in (the **authoritative**
  login-complete signal; research §2). Resumes the scan.
- `POST /api/scans/{id}/cancel` — user cancels → run recorded `Incomplete(LoginNotCompleted)`.

## Schedule (FR-019)

- `GET /api/schedule` → `{ "cron": "0 6,13,20 * * *", "timeZone": "Europe/Warsaw", "enabled": true, "lastRunUtc": "…" }`.
- `PUT /api/schedule` — body `{ cron, timeZone, enabled }`. **Cron validated** at the boundary
  → `400 { error.code: "InvalidCron" }` on parse failure. Picked up by the next poll-tick.

## CV (FR-021/022/026)

- `GET /api/cv` — list CVs + readability + derived profile summary.
- `POST /api/cv` (multipart) — upload a PDF (stored locally, gitignored). Triggers extraction;
  if below the readable threshold → `200` with `{ "isReadable": false }` (graceful degradation).
- `DELETE /api/cv/{id}`.
- `GET /api/profile` / `PUT /api/profile` — view/edit derived profile + user-set salary
  expectation (`floor`,`target`) and work-mode/employment preferences (not in the PDF).

## Settings

- `GET /api/settings/normalization` / `PUT …` — FX table, `fxAsOf`, B2B↔Permanent factor,
  range strategy, assumed hours/days (research §7). Editing **re-ranks** (derived recompute).
- `GET /api/settings/weights` / `PUT …` — scoring weights.

## Export (FR-037)

### `GET /api/export?format=json|csv`
Streams all collected offers + statuses + history in a portable, human-readable file
(Content-Disposition attachment). Read outside the app (FR-037, Principle IX).
