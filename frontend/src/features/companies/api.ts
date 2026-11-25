import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api } from '../../lib/api'

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
  return useMutation({
    mutationFn: (url: string) => api.post<FollowLinkResult>('/companies/follow-link', { url }),
    onSuccess: refresh,
  })
}

export const sourceNames: Record<string, string> = { inhire: 'InHire', greenhouse: 'Greenhouse', lever: 'Lever', workday: 'Workday', careersite: 'Site de carreiras' }

/** What happened after following by link, in one sentence. */
export function followMessage(result: FollowLinkResult) {
  const { company, added, alreadyFollowing, listed } = result
  if (alreadyFollowing) return `Você já segue a ${company.name}.`
  const paused = company.paused ? ' A coleta dela está pausada pelo admin, então nenhuma vaga nova chega até ele reativar.' : ''
  if (!added) return `Você está seguindo a ${company.name}.${paused}`
  if (listed === 0) return `Você está seguindo a ${company.name}. Ela não tem vagas abertas agora; você é avisado quando abrir.`
  return `Você está seguindo a ${company.name}. ${listed === 1 ? '1 vaga aberta entra' : `${listed} vagas abertas entram`} na próxima coleta, e as que combinarem com você chegam nas suas notificações.`
}
