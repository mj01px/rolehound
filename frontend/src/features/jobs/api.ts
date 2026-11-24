import { keepPreviousData, useInfiniteQuery, useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api } from '../../lib/api'
import type { ArchiveReason } from './labels'

export type JobView = 'all' | 'highlights' | 'archived'

export interface JobListItem {
  id: string
  title: string
  company: string
  location: string
  source: string
  workMode: string
  seniority: string
  score: number
  isHighlight: boolean
  isNew: boolean
  tags: string[]
  publishedAt: string | null
  firstSeenAt: string
  closedAtSource: boolean
  archiveReason: string | null
}

export interface JobCounts {
  all: number
  highlights: number
  archived: number
  new: number
}

export interface JobListResponse {
  items: JobListItem[]
  total: number
  offset: number
  limit: number
  counts: JobCounts
}

export interface JobDetail {
  job: JobListItem
  description: string
  url: string
  matchedGroups: string[]
  addedByUser: boolean
  applicationId: string | null
  closedAt: string | null
  archivedAt: string | null
  archiveNote: string | null
  /** The company behind the job, when people can follow it. */
  company: { targetId: string; name: string; following: boolean } | null
}

export interface JobFilters {
  view: JobView
  q: string
  source: string
  workMode: string
  seniority: string
}

export const PAGE_SIZE = 50

const jobsKey = ['jobs'] as const

export function useJobs(filters: JobFilters) {
  return useInfiniteQuery({
    queryKey: [...jobsKey, 'list', filters],
    initialPageParam: 0,
    queryFn: ({ pageParam, signal }) => {
      const params = new URLSearchParams({ view: filters.view, limit: String(PAGE_SIZE), offset: String(pageParam) })
      for (const key of ['q', 'source', 'workMode', 'seniority'] as const) {
        if (filters[key]) params.set(key, filters[key])
      }
      return api.get<JobListResponse>(`/jobs?${params.toString()}`, signal)
    },
    getNextPageParam: (last) => (last.offset + last.items.length < last.total ? last.offset + last.items.length : undefined),
    placeholderData: keepPreviousData,
  })
}

export function useJob(id: string) {
  return useQuery({
    queryKey: [...jobsKey, 'detail', id],
    queryFn: ({ signal }) => api.get<JobDetail>(`/jobs/${id}`, signal),
  })
}

export function useArchive(id: string) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (body: { reason: ArchiveReason; note: string }) => api.put(`/jobs/${id}/archive`, body),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: jobsKey }),
  })
}

export function useRestore(id: string) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: () => api.delete(`/jobs/${id}/archive`),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: jobsKey }),
  })
}

export interface AddLinkRequest {
  url: string
  title?: string
  company?: string
  location?: string
  description?: string
}

export function useAddLink() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (body: AddLinkRequest) => api.post<{ id: string; fetched: boolean }>('/jobs/links', body),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: jobsKey }),
  })
}
