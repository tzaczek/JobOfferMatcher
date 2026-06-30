// TypeScript mirror of contracts/rest-api.md. Kept in sync with the backend DTOs by hand
// (single-user app — no codegen). All times are ISO-8601 UTC strings.

export type UserStatus = 'new' | 'viewed' | 'interested' | 'dismissed'
export type WorkMode = 'office' | 'remote' | 'hybrid' | string
export type Availability = 'available' | 'no_longer_available'
export type NormalizationQuality = 'Reported' | 'Estimated' | 'RoughEstimate'

/** Lifecycle of an AI-derived output (offer summary / fit). Never a non-AI fallback (FR-005). */
export type EnrichmentState = 'pending' | 'produced' | 'failed'
/** CV profile lifecycle — adds `unreadable` (a content verdict, not a retry-exhausted failure). */
export type CvProfileState = 'pending' | 'produced' | 'unreadable' | 'failed'

export interface SalaryBandDto {
  min: number | null
  max: number | null
  currency: string | null
  period: string | null
  basis: string
  tax: string
}

export interface NormalizedSalaryDto {
  comparableMonthly: { amount: number; currency: string }
  quality: NormalizationQuality
  assumptions: string[]
}

export interface FitDto {
  /** Lifecycle. A numeric `score` is present ONLY under `produced` (FR-005). */
  state: EnrichmentState
  score?: number
  matched?: string[]
  missing?: string[]
  rationale?: string | null
}

export interface OfferDto {
  offerId: string
  roleGroupId: string | null
  title: string
  company: string
  location: string | null
  workMode: WorkMode | null
  employmentType: string | null
  seniority: string | null
  requiredSkills: string[]
  niceToHaveSkills: string[]
  salaryBands: SalaryBandDto[]
  normalizedSalary: NormalizedSalaryDto | null
  /** AI offer summary (≤ configured words); null until produced (FR-006). */
  summary?: string | null
  /** AI key skills (≤ configured count). */
  keySkills?: string[]
  /** Offer summary/skills lifecycle. */
  enrichmentState?: EnrichmentState
  /** Fit is null when there is NO current produced CV profile (no CV, or profile not produced). */
  fit: FitDto | null
  /** Fit lifecycle mirror (matches `fit.state` when a produced profile exists). */
  fitState?: EnrichmentState
  canonicalUrl: string
  isNew: boolean
  isUpdated: boolean
  availability: Availability
  firstSeenAt: string
  firstSuggestedAt: string | null
  lastSeenAt: string
  /** Source-reported publish date (ISO-8601), when the source provides one. */
  publishedAt?: string | null
  userStatus: UserStatus
  /** The user marked that they applied to this role (orthogonal to userStatus). */
  applied: boolean
  /** When the user applied, if a date was recorded (ISO-8601); null otherwise. */
  appliedAt?: string | null
  /** Free-text note about the application, if one was added. */
  applicationNote?: string | null
  /** Other offers grouped under the same role across sources (US4). */
  groupMembers?: OfferGroupMemberDto[]
}

export interface OfferGroupMemberDto {
  offerId: string
  sourceName: string
  canonicalUrl: string
}

export interface OffersMeta {
  total: number
  new: number
  /** A current produced CV profile exists (replaces `noReadableCv`; tracks the AI profile, not the gauge). */
  hasProducedProfile: boolean
  /** Offers whose summary/skills are pending (eligibility-gated). */
  pendingEnrichment: number
  /** Offers whose summary/skills failed (retries exhausted). */
  failedEnrichment: number
}

export interface OffersResponse {
  data: OfferDto[]
  meta: OffersMeta
}

export type SortKey = 'rank' | 'salary' | 'fit' | 'recency' | 'published'
export type StatusFilter = 'new' | 'all' | 'active' | 'interested' | 'dismissed' | 'viewed'

export interface OffersQuery {
  status?: StatusFilter
  source?: string
  workMode?: string
  sort?: SortKey
  availability?: 'available' | 'all'
  q?: string
  /** Keep only offers the user has (true) / has not (false) applied to. */
  applied?: boolean
}

/** Payload for marking an offer applied — both fields optional (`appliedAt` ISO-8601). */
export interface ApplicationInput {
  appliedAt?: string | null
  note?: string | null
}

export type ScanState =
  | 'running'
  | 'waiting_for_login'
  | 'challenge_detected'
  | 'completed'
  | 'incomplete'

export type ScanOutcome = 'complete' | 'partial' | 'failed' | null

export interface ScanCounts {
  collected: number
  new: number
  updated: number
  unavailable: number
  failed: number
}

export interface ScanStatusDto {
  state: ScanState
  outcome: ScanOutcome
  counts: ScanCounts
  incompleteReason: string | null
}

export interface ScanRunSummaryDto {
  scanRunId: string
  startedAt: string
  finishedAt: string | null
  trigger: string
  outcome: ScanOutcome
  counts: ScanCounts
  incompleteReason: string | null
}

export interface ScheduleDto {
  cron: string
  timeZone: string
  enabled: boolean
  lastRunUtc: string | null
}

export interface WeightsDto {
  skills: number
  seniority: number
  workMode: number
  employment: number
  salary: number
  total: number
}

export interface NormalizationDto {
  baseCurrency: string
  fxToBase: Record<string, number>
  assumedMonthlyHours: number
  assumedMonthlyWorkingDays: number
  b2bToPermanentFactor: number
  rangeStrategy: string
  fxAsOf: string
  fxSource: string
}

export interface SourceDto {
  id: string
  name: string
  kind: string
  requiresLogin: boolean
  enabled: boolean
  searchCriteria: SearchCriteriaDto
}

export interface SearchCriteriaDto {
  categories: string[]
  experienceLevels: string[]
  employmentTypes: string[]
  workingTimes: string[]
  withSalary: boolean
  sortBy: string | null
  orderBy: string | null
  workplaceKeep: string[]
}

export interface CvDto {
  id: string
  fileName: string
  isReadable: boolean
  extractedAt: string | null
  /** AI profile lifecycle (pending until the worker reads the PDF). */
  state: CvProfileState
  /** AI candidate summary; null until produced. */
  summary?: string | null
  skills: string[]
  seniority: string | null
  /** Failed-attempt count (drives the retry/error badge). */
  attemptCount?: number
}

/** `GET /api/enrichment/status` — drives the pending/failed indicators (FR-010/SC-007). */
export interface EnrichmentStatusDto {
  pendingTotal: number
  pendingProfiles: number
  pendingSummaries: number
  pendingFits: number
  failedTotal: number
  hasProducedProfile: boolean
  lastResultAt: string | null
}

/** `GET`/`PUT /api/settings/enrichment` (FR-018). All caps soft; retryLimit drives Pending→Failed. */
export interface EnrichmentSettingsDto {
  offerSummaryMaxWords: number
  cvSummaryMaxWords: number
  maxKeySkills: number
  fitRationaleMaxWords: number
  retryLimit: number
}

export type RerunScope = 'failed' | 'all'

export interface ProfileDto {
  skills: string[]
  seniority: string | null
  /** AI candidate summary from the produced CV profile; null until produced. */
  summary?: string | null
  salaryFloor: number | null
  salaryTarget: number | null
  preferredWorkModes: string[]
  preferredEmployment: string[]
  /** A current produced CV profile exists (replaces `hasReadableCv`; tracks the AI profile, not the gauge). */
  hasProducedProfile: boolean
}

// ---- Backup & Restore (003) -------------------------------------------------
// Mirrors contracts/backup-api.md. The archive bundles the whole DB + CV files.

/** How a backup's source schema relates to this build (FR-017). */
export type BackupCompatibility = 'Same' | 'Older' | 'Newer'

/** One table in the archive manifest: its name, explicit column list, and row count. */
export interface BackupManifestTableDto {
  name: string
  columns: string[]
  rowCount: number
}

/** `manifest.json` — the self-describing header of a backup archive. */
export interface BackupManifestDto {
  backupFormatVersion: number
  createdAtUtc: string
  appProductVersion: string
  migrationTip: string
  tables: BackupManifestTableDto[]
  cvFiles: { name: string; size: number; sha256: string }[]
  cvFileCount: number
}

/** `POST /api/backup/inspect` — verify a backup without restoring (US3, FR-005). */
export interface BackupInspectionDto {
  valid: boolean
  createdAtUtc: string
  appProductVersion: string
  migrationTip: string
  compatibility: BackupCompatibility
  tableCounts: Record<string, number>
  cvFileCount: number
  totalCvBytes: number
  warnings: string[]
}

/** `POST /api/backup/restore` success body (US2, FR-013). */
export interface RestoreReportDto {
  restoredAtUtc: string
  compatibility: BackupCompatibility
  tableCounts: Record<string, number>
  cvFileCount: number
  safetyBackupPath: string
  backfillApplied: boolean
}
