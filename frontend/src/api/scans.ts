import { api } from './client.ts'
import type { ScanRunSummaryDto, ScanStatusDto } from './types.ts'

export function runScan(sourceIds: string[] | null = null): Promise<{ scanRunId: string }> {
  return api.post<{ scanRunId: string }>('/api/scans/run', { sourceIds })
}

export function listScans(signal?: AbortSignal): Promise<{ data: ScanRunSummaryDto[] }> {
  return api.get<{ data: ScanRunSummaryDto[] }>('/api/scans', signal)
}

export function getScanStatus(scanRunId: string, signal?: AbortSignal): Promise<ScanStatusDto> {
  return api.get<ScanStatusDto>(`/api/scans/${scanRunId}/status`, signal)
}

export const TERMINAL_SCAN_STATES = ['completed', 'incomplete'] as const

export function isTerminalScanState(state: ScanStatusDto['state']): boolean {
  return (TERMINAL_SCAN_STATES as readonly string[]).includes(state)
}
