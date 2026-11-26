import type { QueryClient } from '@tanstack/react-query'

/**
 * Until when a collection someone just asked for (a company followed, a source read by hand) may
 * still be running. While it may, the bell checks every few seconds instead of every few minutes,
 * so the new jobs show up as soon as the read ends.
 */
export const readingKey = ['collection', 'reading-until'] as const

const WATCH_FOR = 10 * 60 * 1000

export function watchReading(queryClient: QueryClient) {
  queryClient.setQueryData(readingKey, Date.now() + WATCH_FOR)
  void queryClient.invalidateQueries({ queryKey: ['notifications'] })
}

export function isReading(queryClient: QueryClient) {
  return (queryClient.getQueryData<number>(readingKey) ?? 0) > Date.now()
}
