import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api } from '../../../lib/api'

export type SourceKind = 'inhire' | 'gupy' | 'github' | 'greenhouse' | 'lever'

export interface SourceTarget {
  id: string
  source: string
  key: string
  label: string | null
  enabled: boolean
  lastRunAt: string | null
  lastJobCount: number | null
  lastError: string | null
  activeJobs: number
  /** Who added it by following a company link; null when the admin added it. */
  addedBy: string | null
}

/** People tried to follow companies on a platform Rolehound does not read yet. */
export interface ConnectorRequestSummary {
  platform: string
  requests: number
  people: number
  lastAt: string
}

export interface CollectionRun {
  id: string
  source: string
  /** The target's label, or its key when it has none. */
  key: string
  startedAt: string
  finishedAt: string
  listed: number
  added: number
  reopened: number
  closed: number
  error: string | null
}

/** One collection: every source read in the same run, with only the reads that changed something or failed. */
export interface CollectionBatch {
  id: string
  startedAt: string
  finishedAt: string
  targets: number
  listed: number
  added: number
  reopened: number
  closed: number
  failed: number
  changes: CollectionRun[]
  unchanged: number
}

export interface SourcesStatus {
  nextScheduledAt: string
  /** When a manual run is allowed again; null when it is allowed now. */
  manualAvailableAt: string | null
}

export interface CollectionSummary {
  targets: number
  failed: number
  added: number
  reopened: number
  closed: number
}

const sourcesKey = ['admin', 'sources'] as const

export function useTargets() {
  return useQuery({ queryKey: [...sourcesKey, 'targets'], queryFn: ({ signal }) => api.get<SourceTarget[]>('/admin/sources', signal) })
}

export function useCollections(limit = 10) {
  return useQuery({
    queryKey: [...sourcesKey, 'collections', limit],
    queryFn: ({ signal }) => api.get<CollectionBatch[]>(`/admin/sources/collections?limit=${limit}`, signal),
  })
}

export function useConnectorRequests() {
  return useQuery({
    queryKey: [...sourcesKey, 'requests'],
    queryFn: ({ signal }) => api.get<ConnectorRequestSummary[]>('/admin/sources/requests', signal),
  })
}

export function useSourcesStatus() {
  // The manual run unlocks with time, so the answer is refreshed every minute.
  return useQuery({
    queryKey: [...sourcesKey, 'status'],
    queryFn: ({ signal }) => api.get<SourcesStatus>('/admin/sources/status', signal),
    refetchInterval: 60_000,
  })
}

export function useRunNow() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: () => api.post<CollectionSummary>('/admin/sources/run'),
    onSettled: () => queryClient.invalidateQueries({ queryKey: sourcesKey }),
  })
}

export function useAddSource() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (body: { source: SourceKind; key: string; label: string }) => api.post<SourceTarget>('/admin/sources', body),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: sourcesKey }),
  })
}

export function useToggleSource(target: SourceTarget) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: () => api.put(`/admin/sources/${target.id}`, { enabled: !target.enabled, label: target.label }),
    onSettled: () => queryClient.invalidateQueries({ queryKey: sourcesKey }),
  })
}

export function useRemoveSource(target: SourceTarget) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: () => api.delete(`/admin/sources/${target.id}`),
    onSettled: () => queryClient.invalidateQueries({ queryKey: sourcesKey }),
  })
}

export const kindLabels: Record<SourceKind, string> = {
  inhire: 'InHire',
  gupy: 'Gupy',
  github: 'GitHub',
  greenhouse: 'Greenhouse',
  lever: 'Lever',
}

export const kinds: SourceKind[] = ['inhire', 'gupy', 'github', 'greenhouse', 'lever']

const time = (date: Date) => date.toLocaleTimeString('pt-BR', { hour: '2-digit', minute: '2-digit' })

/** "hoje, 12:00", "ontem, 18:00" or "20 nov, 18:00". */
export function when(value: string | null) {
  if (!value) return 'nunca'
  const date = new Date(value)
  const today = new Date()
  const yesterday = new Date(today)
  yesterday.setDate(today.getDate() - 1)
  if (date.toDateString() === today.toDateString()) return `hoje, ${time(date)}`
  if (date.toDateString() === yesterday.toDateString()) return `ontem, ${time(date)}`
  const day = date.toLocaleDateString('pt-BR', { day: 'numeric', month: 'short' }).replace(/\./g, '').replace(/ de /g, ' ')
  return `${day}, ${time(date)}`
}

export const plural = (count: number, one: string, many: string) => `${count} ${count === 1 ? one : many}`
