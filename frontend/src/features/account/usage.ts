import { api } from '../../lib/api'

export interface UsageRow {
  feature: string
  model: string
  calls: number
  failed: number
  promptTokens: number
  completionTokens: number
}

/** Shared by the usage table and the summary on top of Configurações, so the page asks once. */
export const usageQuery = {
  queryKey: ['account', 'ai-usage'],
  queryFn: ({ signal }: { signal: AbortSignal }) => api.get<{ last30Days: UsageRow[]; allTime: UsageRow[] }>('/account/ai-usage', signal),
}
