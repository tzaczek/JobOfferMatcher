import { api } from './client.ts'
import type { ScheduleDto } from './types.ts'

export function getSchedule(signal?: AbortSignal): Promise<ScheduleDto> {
  return api.get<ScheduleDto>('/api/schedule', signal)
}

export function updateSchedule(body: {
  cron: string
  timeZone: string
  enabled: boolean
}): Promise<ScheduleDto> {
  return api.put<ScheduleDto>('/api/schedule', body)
}
