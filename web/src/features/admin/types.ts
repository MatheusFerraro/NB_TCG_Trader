/** Mirrors the API's admin hub contracts (Features/Admin/AdminContracts.cs). */

export interface AdminDashboard {
  totalUsers: number
  newUsersLast7Days: number
  newUsersLast30Days: number
  lockedUsers: number
  activeListings: number
  failedImportsLast7Days: number
}

/**
 * One admin user-table row. Contact channels are present/absent flags only —
 * the API never sends the handles themselves to the admin hub.
 */
export interface AdminUserSummary {
  id: string
  displayName: string
  email: string | null
  city: string | null
  country: string | null
  hasContactEmail: boolean
  hasDiscordHandle: boolean
  hasInstagramHandle: boolean
  isLockedOut: boolean
  createdAt: string
  lastLoginAt: string | null
  lastSeenAt: string | null
}

export interface AdminUserList {
  items: AdminUserSummary[]
  page: number
  pageSize: number
  totalCount: number
}

export interface AdminImportJobCounts {
  total: number
  failed: number
  needsReview: number
  completed: number
}

export interface AdminUserDetail {
  id: string
  displayName: string
  email: string | null
  city: string | null
  country: string | null
  hasContactEmail: boolean
  hasDiscordHandle: boolean
  hasInstagramHandle: boolean
  roles: string[]
  isLockedOut: boolean
  lockoutEnd: string | null
  accessFailedCount: number
  createdAt: string
  lastLoginAt: string | null
  lastSeenAt: string | null
  collectionItemCount: number
  activeListingCount: number
  importJobs: AdminImportJobCounts
  recentAuditEntries: AdminAuditEntry[]
}

export type AdminActionType =
  | 'UserLocked'
  | 'UserUnlocked'
  | 'UserDetailViewed'
  | 'RoleChanged'

export interface AdminAuditEntry {
  id: number
  adminUserId: string
  adminDisplayName: string | null
  targetUserId: string | null
  targetDisplayName: string | null
  action: AdminActionType
  reason: string | null
  correlationId: string | null
  createdAt: string
}

export interface AdminAuditLog {
  items: AdminAuditEntry[]
  page: number
  pageSize: number
  totalCount: number
}

export interface AdminActivityUser {
  id: string
  displayName: string
  timestamp: string | null
}

export interface AdminFailedSignIn {
  id: string
  displayName: string
  accessFailedCount: number
  isLockedOut: boolean
}

export interface AdminFailedImport {
  id: number
  userId: string
  userDisplayName: string
  fileName: string
  createdAt: string
}

export interface AdminActivity {
  recentSignIns: AdminActivityUser[]
  recentRegistrations: AdminActivityUser[]
  failedSignIns: AdminFailedSignIn[]
  recentFailedImports: AdminFailedImport[]
}
