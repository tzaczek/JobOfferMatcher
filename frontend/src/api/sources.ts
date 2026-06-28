import { api } from './client.ts'
import type { SearchCriteriaDto, SourceDto } from './types.ts'

export interface CreateSourceBody {
  name: string
  kind: string
  searchCriteria: SearchCriteriaDto
  requiresLogin: boolean
}

export interface UpdateSourceBody {
  name: string
  searchCriteria: SearchCriteriaDto
  requiresLogin: boolean
}

export function listSources(signal?: AbortSignal): Promise<{ data: SourceDto[] }> {
  return api.get<{ data: SourceDto[] }>('/api/sources', signal)
}

export function createSource(body: CreateSourceBody): Promise<SourceDto> {
  return api.post<SourceDto>('/api/sources', body)
}

export function updateSource(id: string, body: UpdateSourceBody): Promise<SourceDto> {
  return api.put<SourceDto>(`/api/sources/${id}`, body)
}

export function enableSource(id: string): Promise<void> {
  return api.post<void>(`/api/sources/${id}/enable`)
}

export function disableSource(id: string): Promise<void> {
  return api.post<void>(`/api/sources/${id}/disable`)
}
