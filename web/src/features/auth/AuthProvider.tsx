import { useCallback, useEffect, useMemo, useState } from 'react'
import type { ReactNode } from 'react'
import { onAuthFailure, refreshSession } from '../../lib/apiClient'
import * as authApi from './authApi'
import { AuthContext } from './authContext'
import type { AuthStatus } from './authContext'
import type { LoginRequest, RegisterRequest, UpdateProfileRequest, User } from './types'

interface AuthState {
  status: AuthStatus
  user: User | null
}

/**
 * Owns the session. On mount it trades the persisted refresh token for a new
 * token pair so the user stays logged in across a page refresh (BACKLOG #19).
 * refreshSession() is single-flight, so StrictMode's double effect run cannot
 * consume the one-time refresh token twice.
 */
export function AuthProvider({ children }: { children: ReactNode }) {
  const [state, setState] = useState<AuthState>({ status: 'loading', user: null })

  useEffect(() => {
    let cancelled = false
    const unsubscribe = onAuthFailure(() => {
      setState({ status: 'anonymous', user: null })
    })

    refreshSession()
      .then((auth) => {
        if (!cancelled) {
          setState(auth ? { status: 'authenticated', user: auth.user } : { status: 'anonymous', user: null })
        }
      })
      .catch(() => {
        if (!cancelled) {
          setState({ status: 'anonymous', user: null })
        }
      })
    return () => {
      cancelled = true
      unsubscribe()
    }
  }, [])

  const login = useCallback(async (request: LoginRequest) => {
    const user = await authApi.login(request)
    setState({ status: 'authenticated', user })
  }, [])

  const register = useCallback(async (request: RegisterRequest) => {
    const user = await authApi.register(request)
    setState({ status: 'authenticated', user })
  }, [])

  const updateProfile = useCallback(async (request: UpdateProfileRequest) => {
    const user = await authApi.updateProfile(request)
    // Status is already 'authenticated' (the endpoint requires a token); only
    // the user snapshot changes.
    setState((prev) => ({ ...prev, user }))
  }, [])

  const logout = useCallback(() => {
    authApi.logout()
    setState({ status: 'anonymous', user: null })
  }, [])

  const value = useMemo(
    () => ({ status: state.status, user: state.user, login, register, updateProfile, logout }),
    [state, login, register, updateProfile, logout],
  )

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>
}
