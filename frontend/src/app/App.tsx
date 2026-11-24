import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { RouterProvider } from 'react-router/dom'
import { ApiError } from '../lib/api'
import { router } from './router'

// A 4xx answer will not change on its own (gone, forbidden, invalid), so only network and server errors are retried.
const retry = (failures: number, error: unknown) => !(error instanceof ApiError && error.status >= 400 && error.status < 500) && failures < 2

const queryClient = new QueryClient({
  defaultOptions: { queries: { staleTime: 30_000, refetchOnWindowFocus: false, retry } },
})

export function App() {
  return (
    <QueryClientProvider client={queryClient}>
      <RouterProvider router={router} />
    </QueryClientProvider>
  )
}
