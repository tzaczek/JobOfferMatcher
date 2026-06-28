# Contract: justjoin.it source payload (external, unofficial)

Live-verified 2026-06-28. **Unofficial/undocumented** — pin behind the `JustJoinItSource`
adapter + a thin mapping layer + contract tests, so an upstream change touches one class
(research §1; accepted-risk **ADR-2** in plan.md). See [ijobsource-port.md](./ijobsource-port.md)
for the abstraction this maps into.

## Endpoints

| Purpose | Request | Notes |
|---------|---------|-------|
| LIST | `GET https://api.justjoin.it/v2/user-panel/offers/by-cursor` + filter params + `from={offset}` | Page size fixed **20**. |
| DETAIL | `GET https://api.justjoin.it/v1/offers/{slug}` | **v1** (v2 path 404s). Fetch **only for new/changed offers**. |

No auth/cookies/headers required (HTTP 200 without `User-Agent`/`Version`). Send a **generic,
non-PII** `User-Agent` anyway (politeness; no name/email — Principle IV). Pace ~1 req/s with
Polly backoff on 429/5xx.

## Filter params (the user's saved search → server)

```
categories[]=7                       # .NET (category key "net")
experienceLevels[]=mid&experienceLevels[]=senior
employmentTypes[]=b2b&employmentTypes[]=permanent
withSalary=true                      # with-salary=yes
workingTimes[]=full_time             # working-hours=full-time
orderBy=DESC&sortBy=salary
# workplace=hybrid,remote is NOT honored server-side → filter client-side (see below)
```

Keep all params + the category id↔key map in **editable config** (FR-002); contract-test
`7 ↔ "net"`.

## LIST response shape

```jsonc
{
  "data": [{
    "guid": "uuid",                  // STABLE identity (FR-011) → ExternalRef.NativeKey
    "slug": "senior-dotnet-acme-krakow",   // per-scan; canonical link; may change on repost
    "title": "…", "companyName": "…",
    "workplaceType": "office|remote|hybrid",   // CLIENT-FILTER here (keep remote+hybrid)
    "experienceLevel": "mid|senior|…",
    "requiredSkills": ["C#", ".NET", …],       // present in LIST — no detail needed for skills
    "niceToHaveSkills": [ … ],
    "employmentTypes": [                        // ARRAY — can hold b2b AND permanent
      { "type": "b2b", "from": 18000, "to": 22000, "currency": "pln", "unit": "month",
        "gross": false, "fromPln": 18000, "toPln": 22000, "fromEur": 4186, "toEur": 5116 /* … */ }
    ],
    "multilocation": [ { "city": "…", "slug": "…" } ],   // share one guid/offerParent.slug
    "publishedAt": "…", "lastPublishedAt": "…", "expiredAt": "…",
    "isSuperOffer": false,                       // promoted → pinned out of salary order
    "remote": true, "countryCode": "PL"
  }],
  "meta": { "from": 0, "next": { "cursor": 20, "itemsCount": 20 },
            "prev": { … }, "totalItems": 179 }
}
```

## DETAIL response (only the extras)

Adds: `body` (description HTML), `applyUrl`, `companyUrl`, `languages[]`, `companySize`,
`category{ id, name, key }`, `isOfferActive`. Everything else is already in LIST.

## Mapping rules → `CollectedOffer`

| Target | Source | Rule |
|--------|--------|------|
| `ExternalRef.NativeKey` | `guid` | wrap → `SourceOfferId`; **never** the slug (slug is volatile). |
| `CanonicalUrl` | `slug` (or `offerParent.slug`) | `https://justjoin.it/job-offers/{slug}`. |
| `SalaryBands[]` | each `employmentTypes[]` entry | `{from,to,currency,unit,gross}` → `SalaryBand` (basis from `type`, `tax` from `gross`). **Store raw**; ignore `*Pln/*Eur` for storage (normalize in Domain). |
| `WorkMode` | `workplaceType` | keep `remote`+`hybrid`; **UNKNOWN value → keep + flag**, never silently drop. |
| `RequiredSkills`/`NiceToHaveSkills` | same arrays | empty allowed → unknown (FR-010). |
| dates | `publishedAt`/`lastPublishedAt`/`expiredAt` | **update detection + recency only** (not new-vs-seen). |
| `DescriptionHtml` | DETAIL `body` | sanitize before display; Minor tier. |
| availability inputs | `isOfferActive`/`expiredAt` | feed disappearance/availability. |

## Pagination algorithm (FR-007 + correctness)

```
from = 0; seen = {}; pages = 0; cap = ceil(meta.totalItems/20) + 1
loop:
  page = GET …&from={from}
  newGuids = page.data where guid ∉ seen
  if newGuids is empty: STOP                    # authoritative termination
  add newGuids → seen; from = meta.next.cursor  # NB: advance via `from`, value is next.cursor
  if meta.next.cursor is null: STOP
  if ++pages >= cap: STOP (log truncation)      # hard safety cap
dedup all by guid
```

## Known quirks (encode as tests)

- Advance param is **`from`** (offset), not `cursor`/`page`/`offset` (others silently re-return
  page 1).
- `sortBy=salary&orderBy=DESC` is **not** monotonic (`isSuperOffer` pinned) — **re-sort locally**
  by normalized salary for ranking (FR-024).
- Multilocation offers share one `guid` — dedup on `guid` to avoid double-counting.
- Skills can be genuinely empty even on DETAIL — treat as unknown (FR-010), don't infer.
- On `403`/Cloudflare challenge → `Partial/ChallengeDetected` + escalation signal (FR-040).
