import { api } from './client.ts'
import type { EnrichmentSettingsDto, NormalizationDto, WeightsDto } from './types.ts'

export function getWeights(signal?: AbortSignal): Promise<WeightsDto> {
  return api.get<WeightsDto>('/api/settings/weights', signal)
}

export function updateWeights(body: {
  skills: number
  seniority: number
  workMode: number
  employment: number
  salary: number
}): Promise<WeightsDto> {
  return api.put<WeightsDto>('/api/settings/weights', body)
}

export function getNormalization(signal?: AbortSignal): Promise<NormalizationDto> {
  return api.get<NormalizationDto>('/api/settings/normalization', signal)
}

export function getEnrichmentSettings(signal?: AbortSignal): Promise<EnrichmentSettingsDto> {
  return api.get<EnrichmentSettingsDto>('/api/settings/enrichment', signal)
}

export function updateEnrichmentSettings(body: EnrichmentSettingsDto): Promise<EnrichmentSettingsDto> {
  return api.put<EnrichmentSettingsDto>('/api/settings/enrichment', body)
}

export function updateNormalization(body: {
  baseCurrency: string
  fxToBase: Record<string, number>
  assumedMonthlyHours: number
  assumedMonthlyWorkingDays: number
  b2bToPermanentFactor: number
  rangeStrategy: string
  fxSource: string
}): Promise<NormalizationDto> {
  return api.put<NormalizationDto>('/api/settings/normalization', body)
}
