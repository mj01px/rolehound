import { useQuery } from '@tanstack/react-query'
import { ApiError, api } from '../../lib/api'

export type TwoFactorMethod = 'none' | 'totp' | 'email'

export interface Me {
  id: string
  email: string
  username: string
  role: 'user' | 'admin'
  twoFactorMethod: TwoFactorMethod
  onboarded: boolean
  /** Accepted the current terms of use and privacy policy. */
  termsAccepted: boolean
  /** The current version, sent back when accepting. */
  termsVersion: string
}

export interface LoginResponse {
  status: 'signedIn' | 'twoFactorRequired'
  method: TwoFactorMethod | null
}

export const meKey = ['auth', 'me'] as const

/** The signed-in person, or null when there is no session. */
export function useMe() {
  return useQuery({
    queryKey: meKey,
    queryFn: async ({ signal }) => {
      try {
        return await api.get<Me>('/auth/me', signal)
      } catch (error) {
        if (error instanceof ApiError && error.status === 401) {
          return null
        }
        throw error
      }
    },
    retry: false,
    staleTime: 60_000,
  })
}

let lastTaken: { path: string; token: string } | null = null

/**
 * Links in emails carry their token in the URL fragment, so it never reaches
 * servers or Referer headers. Read it once and wipe it from the address bar.
 * Remembered per page, so a second read (React development mode) gets the same value.
 */
export function takeHashToken(): string | null {
  const path = window.location.pathname
  const token = new URLSearchParams(window.location.hash.slice(1)).get('token')
  if (token) {
    lastTaken = { path, token }
    window.history.replaceState(null, '', path + window.location.search)
    return token
  }
  return lastTaken?.path === path ? lastTaken.token : null
}
