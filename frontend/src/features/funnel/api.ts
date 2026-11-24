import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api } from '../../lib/api'

export const statuses = [
  { value: 'interest', label: 'Tenho interesse', color: 'var(--status-interest)' },
  { value: 'applied', label: 'Aplicada', color: 'var(--status-applied)' },
  { value: 'screening', label: 'Em triagem', color: 'var(--status-screening)' },
  { value: 'challenge', label: 'Teste ou desafio', color: 'var(--status-challenge)' },
  { value: 'interview', label: 'Entrevista', color: 'var(--status-interview)' },
  { value: 'offer', label: 'Proposta', color: 'var(--status-offer)' },
  { value: 'rejected', label: 'Rejeitada', color: 'var(--status-rejected)' },
  { value: 'withdrawn', label: 'Desisti', color: 'var(--status-withdrawn)' },
] as const

export type ApplicationStatus = (typeof statuses)[number]['value']

export const statusInfo = (value: string) => statuses.find((status) => status.value === value) ?? statuses[0]

export interface ApplicationCard {
  id: string
  jobId: string
  title: string
  company: string
  status: ApplicationStatus
  priority: number
  appliedOn: string | null
  nextStep: string
  nextStepOn: string | null
  isOverdue: boolean
  jobClosedAtSource: boolean
  updatedAt: string
}

export interface Board {
  columns: { status: ApplicationStatus; items: ApplicationCard[] }[]
  overdue: ApplicationCard[]
  closed: ApplicationCard[]
}

export interface Interaction {
  id: string
  date: string
  title: string
  detail: string
  isSystem: boolean
  createdAt: string
}

export interface ApplicationDetail {
  application: ApplicationCard
  contact: string
  hasReferral: boolean
  notes: string
  jobUrl: string
  interactions: Interaction[]
}

export interface ApplicationForm {
  status: ApplicationStatus
  priority: number
  appliedOn: string | null
  nextStep: string
  nextStepOn: string | null
  contact: string
  hasReferral: boolean
  notes: string
}

export interface InteractionForm {
  date: string
  title: string
  detail: string
}

const funnelKey = ['funnel'] as const

/** Funnel changes also change the job views (a job in the funnel leaves them). */
function useInvalidate() {
  const queryClient = useQueryClient()
  return () => Promise.all([queryClient.invalidateQueries({ queryKey: funnelKey }), queryClient.invalidateQueries({ queryKey: ['jobs'] })])
}

export function useBoard() {
  return useQuery({ queryKey: [...funnelKey, 'board'], queryFn: ({ signal }) => api.get<Board>('/applications', signal) })
}

const closedStatuses: ApplicationStatus[] = ['rejected', 'withdrawn']

/**
 * Moves a card to another stage (dragging on the board). The board changes at once;
 * if the API refuses, it goes back as it was.
 */
export function useMoveApplication() {
  const queryClient = useQueryClient()
  const invalidate = useInvalidate()
  const boardKey = [...funnelKey, 'board']
  return useMutation({
    mutationFn: ({ id, status }: { id: string; status: ApplicationStatus }) => api.put<ApplicationCard>(`/applications/${id}/status`, { status }),
    onMutate: async ({ id, status }) => {
      await queryClient.cancelQueries({ queryKey: boardKey })
      const before = queryClient.getQueryData<Board>(boardKey)
      if (before) {
        const card = [...before.columns.flatMap((column) => column.items), ...before.closed].find((item) => item.id === id)
        if (card) {
          const moved = { ...card, status }
          const without = (items: ApplicationCard[]) => items.filter((item) => item.id !== id)
          queryClient.setQueryData<Board>(boardKey, {
            columns: before.columns.map((column) => ({
              ...column,
              items: column.status === status ? [moved, ...without(column.items)] : without(column.items),
            })),
            overdue: before.overdue.map((item) => (item.id === id ? moved : item)),
            closed: closedStatuses.includes(status) ? [moved, ...without(before.closed)] : without(before.closed),
          })
        }
      }
      return { before }
    },
    onError: (_error, _move, context) => {
      if (context?.before) queryClient.setQueryData(boardKey, context.before)
    },
    onSettled: invalidate,
  })
}

export function useApplication(id: string) {
  return useQuery({ queryKey: [...funnelKey, 'detail', id], queryFn: ({ signal }) => api.get<ApplicationDetail>(`/applications/${id}`, signal) })
}

export function useEnterFunnel(jobId: string) {
  const invalidate = useInvalidate()
  return useMutation({
    mutationFn: (status: ApplicationStatus) => api.post<ApplicationCard>(`/jobs/${jobId}/application`, { status }),
    onSuccess: invalidate,
  })
}

export function useSaveApplication(id: string) {
  const invalidate = useInvalidate()
  return useMutation({
    mutationFn: (form: ApplicationForm) => api.put<ApplicationCard>(`/applications/${id}`, form),
    onSuccess: invalidate,
  })
}

/**
 * Leaves the funnel. The deleted application's query is dropped, so nothing asks for it
 * again, and the board and job lists refresh in the background instead of being awaited.
 */
export function useLeaveFunnel(id: string) {
  const queryClient = useQueryClient()
  const invalidate = useInvalidate()
  return useMutation({
    mutationFn: () => api.delete(`/applications/${id}`),
    onSuccess: () => {
      queryClient.removeQueries({ queryKey: [...funnelKey, 'detail', id] })
      void invalidate()
    },
  })
}

export function useSaveInteraction(applicationId: string) {
  const invalidate = useInvalidate()
  return useMutation({
    mutationFn: ({ id, form }: { id?: string; form: InteractionForm }) =>
      id ? api.put(`/applications/${applicationId}/interactions/${id}`, form) : api.post(`/applications/${applicationId}/interactions`, form),
    onSuccess: invalidate,
  })
}

export function useDeleteInteraction(applicationId: string) {
  const invalidate = useInvalidate()
  return useMutation({ mutationFn: (id: string) => api.delete(`/applications/${applicationId}/interactions/${id}`), onSuccess: invalidate })
}

/** "21/11/2026" from "2026-11-21", without timezone shifts. */
export function formatDay(value: string | null): string {
  if (!value) return ''
  const [year, month, day] = value.split('-')
  return `${day}/${month}/${year}`
}

/** Today in Brazil as "yyyy-mm-dd" (funnel dates are local days). */
export function todayInBrazil(): string {
  return new Intl.DateTimeFormat('en-CA', { timeZone: 'America/Sao_Paulo' }).format(new Date())
}
