import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api } from '../../../lib/api'
import { watchReading } from '../../notifications/reading'

export type SourceKind = 'inhire' | 'gupy' | 'github' | 'greenhouse' | 'lever' | 'workday' | 'careersite'

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
  /** Open health incident: 'failing' (last reads failed) or 'emptied' (went from many jobs to none). */
  healthIssue: 'failing' | 'emptied' | null
  /** What the platform says the target is: a general search or one company's careers page. */
  kind: 'search' | 'company'
  /** The app keeps it itself (Gupy search terms); it cannot be paused or removed here. */
  automatic: boolean
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
  /** The read failed and changed nothing. */
  error: string | null
  /** The read counted but was partial. */
  warning: string | null
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
  /** One source read on its own, right after it was added. */
  partial: boolean
}

export interface CollectionRequest {
  id: string
  kind: 'full' | 'target'
  status: 'queued' | 'running'
  requestedAt: string
  startedAt: string | null
}

export interface SourcesStatus {
  nextScheduledAt: string
  /** When a manual run is allowed again; null when it is allowed now. */
  manualAvailableAt: string | null
  /** A full collection someone asked for, waiting or running. */
  running: CollectionRequest | null
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

export function useSourcesStatus() {
  // Every few seconds while a collection runs; otherwise every minute (the manual run unlocks with time).
  return useQuery({
    queryKey: [...sourcesKey, 'status'],
    queryFn: ({ signal }) => api.get<SourcesStatus>('/admin/sources/status', signal),
    refetchInterval: (query) => (query.state.data?.running ? 5_000 : 60_000),
  })
}

export function useRunNow() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: () => api.post<CollectionRequest>('/admin/sources/run'),
    onSuccess: () => watchReading(queryClient),
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

/** Reads one source now, on its own; the person hears about it by notification when it ends. */
export function useRunSource(target: SourceTarget) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: () => api.post<CollectionRequest>(`/admin/sources/${target.id}/run`),
    onSuccess: () => watchReading(queryClient),
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
  workday: 'Workday',
  careersite: 'Sites de carreira',
}

export const kinds: SourceKind[] = ['inhire', 'gupy', 'github', 'greenhouse', 'lever', 'workday', 'careersite']

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
