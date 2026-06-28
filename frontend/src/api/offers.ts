import { api } from './client.ts'
import type { OffersQuery, OffersResponse, UserStatus } from './types.ts'

export function listOffers(query: OffersQuery = {}, signal?: AbortSignal): Promise<OffersResponse> {
  const params = new URLSearchParams()
  if (query.status) params.set('status', query.status)
  if (query.source) params.set('source', query.source)
  if (query.workMode) params.set('workMode', query.workMode)
  if (query.sort) params.set('sort', query.sort)
  if (query.availability) params.set('availability', query.availability)
  if (query.q) params.set('q', query.q)
  const qs = params.toString()
  return api.get<OffersResponse>(`/api/offers${qs ? `?${qs}` : ''}`, signal)
}

export function setOfferStatus(offerId: string, status: Exclude<UserStatus, 'new'>): Promise<void> {
  return api.post<void>(`/api/offers/${offerId}/status`, { status })
}
