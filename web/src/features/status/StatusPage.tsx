import { useQuery } from '@tanstack/react-query'
import { apiGet } from '../../lib/api'
import styles from './StatusPage.module.css'

// Temporary landing page until the sign-in slice exists: proves the web app
// reaches the API.
export function StatusPage() {
  const health = useQuery({
    queryKey: ['health', 'live'],
    queryFn: ({ signal }) => apiGet<string>('/health/live', signal),
    retry: false,
  })

  const label = health.isPending ? 'verificando' : health.isSuccess ? 'API respondendo' : 'API fora do ar'

  return (
    <main className={styles.page}>
      <svg width="48" height="48" viewBox="0 0 32 32" role="img" aria-label="rolehound">
        <path d="M9 25V14.5a5 5 0 0 1 5-5h3" fill="none" stroke="currentColor" strokeWidth="4.5" />
        <rect x="19.5" y="20" width="5.5" height="5" fill="var(--signal)" />
      </svg>
      <h1 className={styles.title}>rolehound</h1>
      <p className={styles.status} role="status">
        <span className={health.isSuccess ? styles.dotOk : styles.dot} aria-hidden="true" />
        {label}
      </p>
    </main>
  )
}
