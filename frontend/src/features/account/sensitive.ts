import { createContext, use } from 'react'

export type Sensitive = <T>(action: () => Promise<T>) => Promise<T>

export const ReauthContext = createContext<Sensitive | null>(null)

/**
 * Runs a sensitive action. When the API answers "reauthentication_required",
 * asks for the password (and the second factor when enabled), then runs the
 * action again. Rejects with Error('cancelled') if the person cancels.
 */
export function useSensitive(): Sensitive {
  const context = use(ReauthContext)
  if (!context) {
    throw new Error('useSensitive must be used inside ReauthProvider')
  }
  return context
}
