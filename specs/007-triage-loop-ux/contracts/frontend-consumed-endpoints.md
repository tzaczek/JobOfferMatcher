# Contracts: Triage-Loop UX (007)

## No new or changed contract

This feature adds **no endpoint, no request/response shape change, and no new query parameter**. It is
frontend-only and **consumes existing backend contracts**. This file exists to make the "no backend change"
guarantee (NFR-1 / SC-008) explicit and reviewable.

All endpoints below already exist in `backend/src/Web/Endpoints/` and are loopback-only.

| Endpoint | Method | Used by | Existing capability relied on |
|---|---|---|---|
| `/api/offers` | GET | #2, #3 | Already accepts `view`/`status`/`availability`/`source`/`sort` **and `q`, `workMode`** — the feature simply starts sending `q` (ILike on Title+Company) and `workMode` (case-insensitive enum). No new param. |
| `/api/offers/{id}/detail` | GET | #8, #9, #10, #14-adjacent | Returns `OfferDetailDto` (offer = full `OfferDto` + versions/events) for any offer id, independent of the feed. |
| `/api/offers/{id}/status` | POST | #4, #6 | Sets `userStatus`; `new→viewed`, `→dismissed`, restore already legal. |
| `/api/offers/{id}/application` (+ clear) | POST/DELETE | #8 | Existing mark-applied / edit / unmark used by `ApplyModal`. |
| `/api/enrichment/status` | GET | #1 | Returns `pendingTotal`/`failedTotal`/`lastResultAt`. |
| `/api/enrichment/rerun` | POST | #1 | Existing re-run; the feature only adds an error `catch` on the client. |
| `/api/scans` | GET | #5 | Returns `ScanRunSummaryDto[]` newest-first (freshness + in-flight detection). |
| `/api/scans/{id}` | GET | #5 | `ScanStatusDto` poll target for a resumed run. |
| `/api/scans/run` | POST | #5 | Existing run trigger (unchanged; no per-source use added here). |
| `/api/tailored-cv/*` | — | #8 | Existing tailor flow reused via `TailorCvModal`. |

**Client wiring only**: the changes are in `frontend/src/api/*` callers and components — the API-client
functions (`listOffers`, `getOfferDetail`, `setOfferStatus`, `getEnrichmentStatus`, `triggerRerun`,
`listScans`, `getScanStatus`, `runScan`) already exist and already serialize the parameters used.

**No OpenAPI/schema regeneration, no versioning change, no new DTO.**
