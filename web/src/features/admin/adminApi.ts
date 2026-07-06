import { apiFetch } from '../../lib/apiClient'
import type {
  AdminActivity,
  AdminAuditLog,
  AdminDashboard,
  AdminUserDetail,
  AdminUserList,
} from './types'

export const ADMIN_PAGE_SIZE = 25

/** User-table filters, straight from form inputs. All optional, ANDed server-side. */
export interface AdminUserFilters {
  search?: string
  city?: string
  country?: string
  /** '' = all, 'true' = locked only, 'false' = unlocked only. */
  locked?: '' | 'true' | 'false'
}

export function getDashboard(): Promise<AdminDashboard> {
  return apiFetch<AdminDashboard>('/admin/dashboard')
}

export function listUsers(filters: AdminUserFilters, page = 1): Promise<AdminUserList> {
  const params = new URLSearchParams({
    page: String(page),
    pageSize: String(ADMIN_PAGE_SIZE),
  })
  if (filters.search?.trim()) params.set('search', filters.search.trim())
  if (filters.city?.trim()) params.set('city', filters.city.trim())
  if (filters.country?.trim()) params.set('country', filters.country.trim())
  if (filters.locked) params.set('locked', filters.locked)
  return apiFetch<AdminUserList>(`/admin/users?${params}`)
}

export function getUserDetail(userId: string): Promise<AdminUserDetail> {
  return apiFetch<AdminUserDetail>(`/admin/users/${encodeURIComponent(userId)}`)
}

/** Locks an account. The reason is mandatory — it lands in the audit log. */
export function lockUser(userId: string, reason: string): Promise<void> {
  return apiFetch<void>(`/admin/users/${encodeURIComponent(userId)}/lock`, {
    method: 'POST',
    body: { reason },
  })
}

export function unlockUser(userId: string, reason?: string): Promise<void> {
  return apiFetch<void>(`/admin/users/${encodeURIComponent(userId)}/unlock`, {
    method: 'POST',
    body: { reason: reason?.trim() ? reason.trim() : null },
  })
}

export function getAuditLog(page = 1): Promise<AdminAuditLog> {
  const params = new URLSearchParams({
    page: String(page),
    pageSize: String(ADMIN_PAGE_SIZE),
  })
  return apiFetch<AdminAuditLog>(`/admin/audit-log?${params}`)
}

export function getActivity(): Promise<AdminActivity> {
  return apiFetch<AdminActivity>('/admin/activity')
}
