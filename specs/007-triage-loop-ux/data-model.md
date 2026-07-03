# Data Model: Triage-Loop UX (007)

## No new persistent data

This feature introduces **no new persistent entities, no columns, and no EF migration**. It is a display and
interaction change. Constitution Principle IX (append-only, recoverable) is satisfied trivially — nothing in
the schema changes, and no destructive data operation is performed.

The only writes it triggers are **existing** per-offer status transitions the product already supports; the
feature just invokes them from more places (the detail view, an optimistic dismiss, and open-⇒-viewed).

## Existing persisted data consumed (read-only shapes)

Reused as-is via the existing typed API-client layer; **no shape change**:

| DTO / field | Used by | Note |
|---|---|---|
| `OfferDto.fit` (state, score, matched, missing, rationale) | #7, #10 | Rendered by the shared `FitBreakdown`. |
| `OfferDto.affinity` (state, score, resembles, rationale) | #7, #10 | Rendered by the shared `AffinityBreakdown`; `insufficient` state drives cold-start copy. |
| `OfferDto.keySkills / requiredSkills / niceToHaveSkills` | #7 | Merged into one deduped skills row. |
| `OfferDto.normalizedSalary`, `userStatus`, `applied`, `appliedAt` | #7, #8 | Card/drawer badges. |
| `EnrichmentStatusDto.pendingTotal / failedTotal / lastResultAt` | #1 | Drives live refresh + the /enrich nudge. |
| `ScanRunSummaryDto.startedAt / finishedAt / outcome / counts / incompleteReason` | #5 | Freshness line + in-flight detection (`finishedAt === null`). |
| `ScanStatusDto.state` | #5 | Poll target for a resumed run. |
| `OfferDetailDto.offer` (a full `OfferDto`) | #8, #10 | Drawer badges + full breakdown, no extra fetch. |

## Status transitions used (all pre-existing and legal)

`Offer.ChangeUserStatus` already permits these; the domain rejects only `→ new`. No new transition is added.

- `new → viewed` — on detail-view open / prev-next navigation (finding #4) and via "Mark all reviewed".
- `active → dismissed` — Dismiss (optimistic, with undo).
- `dismissed → viewed` — Undo / Restore.
- `→ interested` — Interested.
- Apply / clear-applied — via the existing application endpoints.

## New transient client state (not persisted)

Lives only in the browser session; never stored, never sent off-machine:

- **URL search params**: `view`, `sort`, `source`, `q`, `workMode` (finding #2/#3).
- **`dismissStubs: Record<offerId, OfferDto>` + timers** — the ~6s undo window (finding #6).
- **`enrichmentRefresh` counter**, polled `EnrichmentStatusDto`, `lastScan` summary (findings #1/#5).
- **Drawer nav state**: derived current index over the visible `offerIds`; nested-modal open flags (finding
  #8/#9).

**Migration**: none. **Backup/restore/export scope**: unchanged (no new data to include).
