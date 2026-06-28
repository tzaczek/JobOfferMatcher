// TypeScript mirror of contracts/rest-api.md. Kept in sync with the backend DTOs by hand
// (single-user app — no codegen). All times are ISO-8601 UTC strings.

export type UserStatus = 'new' | 'viewed' | 'interested' | 'dismissed'
export type WorkMode = 'office' | 'remote' | 'hybrid' | string
export type Availability = 'available' | 'no_longer_available'
export type NormalizationQuality = 'Reported' | 'Estimated' | 'RoughEstimate'

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
  score: number
  matched: string[]
  missing: string[]
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
  fit: FitDto | null
  canonicalUrl: string
  isNew: boolean
  isUpdated: boolean
  availability: Availability
  firstSeenAt: string
  firstSuggestedAt: string | null
  lastSeenAt: string
  userStatus: UserStatus
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
  noReadableCv: boolean
}

export interface OffersResponse {
  data: OfferDto[]
  meta: OffersMeta
}

export type SortKey = 'rank' | 'salary' | 'fit' | 'recency'
export type StatusFilter = 'new' | 'all' | 'interested' | 'dismissed' | 'viewed'

export interface OffersQuery {
  status?: StatusFilter
  source?: string
  workMode?: string
  sort?: SortKey
  availability?: 'available' | 'all'
  q?: string
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
  skills: string[]
  seniority: string | null
}

export interface ProfileDto {
  skills: string[]
  seniority: string | null
  salaryFloor: number | null
  salaryTarget: number | null
  preferredWorkModes: string[]
  preferredEmployment: string[]
  hasReadableCv: boolean
}
