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

export function useLeaveFunnel(id: string) {
  const invalidate = useInvalidate()
  return useMutation({ mutationFn: () => api.delete(`/applications/${id}`), onSuccess: invalidate })
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
