import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api } from '../../../lib/api'

export type RequestStatus = 'open' | 'done' | 'declined'

/** One platform's requests in one state. */
export interface ConnectorPlatform {
  platform: string
  status: RequestStatus
  requests: number
  people: number
  /** The sites people tried to follow, most asked first (at most five). */
  hosts: string[]
  firstAt: string
  lastAt: string
  resolvedAt: string | null
  /** Why it was declined; admin only. */
  reason: string | null
}

export interface Resolution {
  resolved: number
  notified: number
}

const key = ['admin', 'connector-requests']

export function useConnectorRequests() {
  return useQuery({ queryKey: key, queryFn: ({ signal }) => api.get<ConnectorPlatform[]>('/admin/connector-requests', signal) })
}

/** Closes every open request of a platform: done tells the people who asked, declined tells nobody. */
export function useResolve(platform: string) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (body: { status: 'done' | 'declined'; reason?: string }) => api.post<Resolution>(`/admin/connector-requests/${encodeURIComponent(platform)}/resolution`, body),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: key }),
  })
}
