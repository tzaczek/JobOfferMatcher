import { api } from './client.ts'
import type { CvDto, ProfileDto } from './types.ts'

export function listCvs(signal?: AbortSignal): Promise<{ data: CvDto[] }> {
  return api.get<{ data: CvDto[] }>('/api/cv', signal)
}

export function uploadCv(file: File): Promise<CvDto> {
  const form = new FormData()
  form.append('file', file)
  return api.upload<CvDto>('/api/cv', form)
}

export function deleteCv(id: string): Promise<void> {
  return api.del<void>(`/api/cv/${id}`)
}

export function getProfile(signal?: AbortSignal): Promise<ProfileDto> {
  return api.get<ProfileDto>('/api/profile', signal)
}

export function updateProfile(body: {
  salaryFloor: number | null
  salaryTarget: number | null
  preferredWorkModes: string[]
  preferredEmployment: string[]
}): Promise<ProfileDto> {
  return api.put<ProfileDto>('/api/profile', body)
}
