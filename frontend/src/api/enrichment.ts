import { api } from './client.ts'
import type { EnrichmentStatusDto, RerunScope } from './types.ts'

/** Pending/failed enrichment counts for the global indicator (FR-010/FR-016/SC-007). */
export function getEnrichmentStatus(signal?: AbortSignal): Promise<EnrichmentStatusDto> {
  return api.get<EnrichmentStatusDto>('/api/enrichment/status', signal)
}

/**
 * In-app re-run trigger (FR-009). Does NOT call AI — it only re-arms app state; the user then runs
 * the `/enrich` worker. `failed` re-arms failed-but-current items; `all` forces a full re-run.
 * Returns the new counts (same shape as `/status`).
 */
export function triggerRerun(scope: RerunScope = 'failed'): Promise<EnrichmentStatusDto> {
  return api.post<EnrichmentStatusDto>('/api/enrichment/rerun', { scope })
}
