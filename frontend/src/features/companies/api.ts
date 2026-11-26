import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api } from '../../lib/api'
import { watchReading } from '../notifications/reading'

/** A company Rolehound reads jobs from, as the signed-in person sees it. */
export interface Company {
  id: string
  name: string
  source: string
  key: string
  openJobs: number
  /** Open jobs from it that match the person's profile. */
  forYou: number
  following: boolean
  /** The admin paused its collection: no new jobs arrive from it. */
  paused: boolean
}

export interface FollowLinkResult {
  company: Company
  /** The company was new to Rolehound and now is one of its sources. */
  added: boolean
  alreadyFollowing: boolean
  /** Open jobs it has now. */
  listed: number
  /** A new company is being read in the background; a notification comes when it ends. */
  reading: boolean
}

const companiesKey = ['companies'] as const

export function useCompanies() {
  return useQuery({ queryKey: companiesKey, queryFn: ({ signal }) => api.get<Company[]>('/companies', signal) })
}

/** Following changes the companies list, the open job and the bell. */
function useRefresh() {
  const queryClient = useQueryClient()
  return () =>
    Promise.all([
      queryClient.invalidateQueries({ queryKey: companiesKey }),
      queryClient.invalidateQueries({ queryKey: ['jobs', 'detail'] }),
      queryClient.invalidateQueries({ queryKey: ['notifications'] }),
    ])
}

export function useFollow(targetId: string) {
  const refresh = useRefresh()
  return useMutation({
    mutationFn: (on: boolean) => (on ? api.put(`/companies/${targetId}/alert`) : api.delete(`/companies/${targetId}/alert`)),
    onSettled: refresh,
  })
}

export function useFollowLink() {
  const refresh = useRefresh()
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (url: string) => api.post<FollowLinkResult>('/companies/follow-link', { url }),
    onSuccess: async (result) => {
      if (result.reading) watchReading(queryClient)
      await refresh()
    },
  })
}

export const sourceNames: Record<string, string> = { inhire: 'InHire', gupy: 'Gupy', greenhouse: 'Greenhouse', lever: 'Lever', workday: 'Workday', careersite: 'Site de carreiras' }

/** What happened after following by link, in one sentence. */
export function followMessage(result: FollowLinkResult) {
  const { company, added, alreadyFollowing, listed } = result
  if (alreadyFollowing) return `Você já segue a ${company.name}.`
  const paused = company.paused ? ' A coleta dela está pausada pelo admin, então nenhuma vaga nova chega até ele reativar.' : ''
  if (!added) return `Você está seguindo a ${company.name}.${paused}`
  if (listed === 0) return `Você está seguindo a ${company.name}. Ela não tem vagas abertas agora; você é avisado quando abrir.`
  const reading = result.reading ? ' Já estamos lendo as vagas dela: em um ou dois minutos elas entram em Vagas sozinhas, e você recebe uma notificação.' : ''
  return `Você está seguindo a ${company.name}, que tem ${listed === 1 ? '1 vaga aberta' : `${listed} vagas abertas`}.${reading}`
}

/** One open job of a company page; `score` only when it matches the person's profile. */
export interface CompanyJob {
  id: string
  title: string
  location: string
  workMode: string
  openedAt: string
  score: number | null
  highlight: boolean
  forYou: boolean
  inFunnel: boolean
}

export interface CompanyJobsPage {
  items: CompanyJob[]
  total: number
  page: number
  perPage: number
}

export function useCompany(id: string) {
  return useQuery({ queryKey: ['companies', id], queryFn: ({ signal }) => api.get<Company>(`/companies/${id}`, signal) })
}

/** A place where a company has open jobs, as the source writes it, and how many. */
export interface CompanyCity {
  name: string
  jobs: number
}

export function useCompanyCities(id: string) {
  return useQuery({ queryKey: ['companies', id, 'cities'], queryFn: ({ signal }) => api.get<CompanyCity[]>(`/companies/${id}/cities`, signal) })
}

export function useCompanyJobs(id: string, search: string, city: string, page: number) {
  return useQuery({
    queryKey: ['companies', id, 'jobs', search, city, page],
    queryFn: ({ signal }) => api.get<CompanyJobsPage>(`/companies/${id}/jobs?${new URLSearchParams({ search, city, page: String(page) })}`, signal),
    placeholderData: keepPreviousData,
  })
}
