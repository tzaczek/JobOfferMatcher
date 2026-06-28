import { api } from './client.ts'

export type RoleGroupOverride = 'same' | 'notSame'

/**
 * Persist a manual same/not-same decision for a cross-source role group (US4).
 * `'notSame'` splits a wrongly-merged group; `'same'` re-merges it.
 */
export function setRoleGroupOverride(
  roleGroupId: string,
  override: RoleGroupOverride,
): Promise<void> {
  return api.post<void>(`/api/role-groups/${roleGroupId}/override`, { override })
}
