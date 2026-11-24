import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api } from '../../../lib/api'
import { useSensitive } from '../../account/sensitive'

export type Role = 'user' | 'admin'
export type UserStatus = 'active' | 'pending' | 'deactivated'

export interface AdminUser {
  id: string
  name: string
  /** Masked by the API ("a***a@exemplo.com"): admins never see full addresses. */
  email: string
  username: string
  role: Role
  status: UserStatus
  createdAt: string
  lastLoginAt: string | null
  accessLinkExpiresAt: string | null
  onboardingCompleted: boolean
  twoFactorMethod: 'none' | 'totp' | 'email'
  /** The address waiting for the person's confirmation after an admin edit, masked too. */
  pendingEmail: string | null
}

export interface UserDraft {
  name: string
  email: string
  username: string
}

const usersKey = ['admin', 'users'] as const

export function useUsers() {
  return useQuery({
    queryKey: usersKey,
    queryFn: ({ signal }) => api.get<AdminUser[]>('/admin/users', signal),
  })
}

export function useUser(id: string) {
  return useQuery({
    queryKey: [...usersKey, id],
    queryFn: ({ signal }) => api.get<AdminUser>(`/admin/users/${id}`, signal),
  })
}

export function useCreateUser() {
  const sensitive = useSensitive()
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (draft: UserDraft & { role: Role }) => sensitive(() => api.post<AdminUser>('/admin/users', draft)),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: usersKey }),
  })
}

export function useUpdateUser(id: string) {
  const sensitive = useSensitive()
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (draft: UserDraft) => sensitive(() => api.put<AdminUser>(`/admin/users/${id}`, draft)),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: usersKey }),
  })
}

export type UserAction = 'resend' | 'role' | 'deactivate' | 'reactivate'

export function useUserAction(user: AdminUser) {
  const sensitive = useSensitive()
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (action: UserAction) => {
      switch (action) {
        case 'resend':
          return api.post(`/admin/users/${user.id}/access-link`)
        case 'deactivate':
          return api.post(`/admin/users/${user.id}/deactivate`)
        case 'reactivate':
          return api.post(`/admin/users/${user.id}/reactivate`)
        case 'role':
          return sensitive(() =>
            api.put(`/admin/users/${user.id}/role`, {
              role: user.role === 'admin' ? 'user' : 'admin',
            }),
          )
      }
    },
    onSettled: () => queryClient.invalidateQueries({ queryKey: usersKey }),
  })
}

export const roleLabel = (role: Role) => (role === 'admin' ? 'Admin' : 'Usuário')

export function statusLabel(user: AdminUser) {
  if (user.status === 'deactivated') return 'Desativado'
  if (user.status === 'active') return 'Ativo'
  return user.accessLinkExpiresAt ? 'Convite pendente' : 'Convite vencido'
}

export const initial = (user: AdminUser) => (user.name || user.username).trim().charAt(0).toUpperCase()

const sameDay = (a: Date, b: Date) => a.toDateString() === b.toDateString()

/** "hoje, 11:42", "21 nov 2026" or "nunca entrou". */
export function lastAccess(value: string | null) {
  if (!value) return 'nunca entrou'
  const date = new Date(value)
  if (sameDay(date, new Date())) return `hoje, ${date.toLocaleTimeString('pt-BR', { hour: '2-digit', minute: '2-digit' })}`
  return longDate(value)
}

export const longDate = (value: string) =>
  new Date(value)
    .toLocaleDateString('pt-BR', {
      day: 'numeric',
      month: 'short',
      year: 'numeric',
    })
    .replace(/\./g, '')
    .replace(/ de /g, ' ')

export const dateTime = (value: string) => `${longDate(value)}, ${new Date(value).toLocaleTimeString('pt-BR', { hour: '2-digit', minute: '2-digit' })}`
