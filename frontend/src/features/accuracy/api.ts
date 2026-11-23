import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api } from '../../lib/api'

export interface SampleItem {
  id: string
  jobId: string | null
  title: string
  company: string
  score: number
  shownWorkMode: string
  status: 'pending' | 'labelled' | 'skipped'
  relevant: boolean | null
  workModeRight: boolean | null
}

export interface Sample {
  month: string
  pending: number
  labelled: number
  skipped: number
  items: SampleItem[]
}

export interface Rate {
  value: number | null
  hits: number
  total: number
}

export interface Metrics {
  precision: Rate
  workMode: Rate
  missedHighlights: Rate
  signalsRelevant: number
  signalsNotRelevant: number
  workModeArchives: number
  inFirstThirtyDays: boolean
  months: { month: string; precision: Rate; workMode: Rate }[]
}

const key = ['accuracy'] as const

export const useSample = () => useQuery({ queryKey: [...key, 'sample'], queryFn: ({ signal }) => api.get<Sample>('/accuracy/sample', signal) })

export const useMetrics = () => useQuery({ queryKey: [...key, 'metrics'], queryFn: ({ signal }) => api.get<Metrics>('/accuracy', signal) })

export function useLabel() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: ({ id, relevant, workModeRight }: { id: string; relevant: boolean; workModeRight: boolean | null }) =>
      api.put(`/accuracy/sample/${id}`, { relevant, workModeRight }),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: key }),
  })
}

export function useSkipSample() {
  const queryClient = useQueryClient()
  return useMutation({ mutationFn: () => api.post('/accuracy/sample/skip'), onSuccess: () => queryClient.invalidateQueries({ queryKey: key }) })
}

/** "novembro de 2026" from "2026-11-01". */
export function monthName(value: string): string {
  const [year, month] = value.split('-').map(Number)
  return new Date(year ?? 2026, (month ?? 1) - 1, 1).toLocaleDateString('pt-BR', { month: 'long', year: 'numeric' })
}

export const percent = (rate: Rate) => (rate.value === null ? null : `${Math.round(rate.value * 100)}%`)
