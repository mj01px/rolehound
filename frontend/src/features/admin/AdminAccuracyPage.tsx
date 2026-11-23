import { useQuery } from '@tanstack/react-query'
import { api, errorMessage } from '../../lib/api'
import { Alert } from '../../ui/Alert'
import { percent, type Metrics, type Rate } from '../accuracy/api'
import styles from './admin.module.css'

interface UserAccuracy {
  username: string
  metrics: Metrics
}

const cell = (rate: Rate) => percent(rate) ?? `${rate.hits}/${rate.total}`

/** Every person's score quality, numbers only: the admin never sees their jobs (FR-ACU-04). */
export function AdminAccuracyPage() {
  const people = useQuery({ queryKey: ['admin', 'accuracy'], queryFn: ({ signal }) => api.get<UserAccuracy[]>('/admin/accuracy', signal) })

  return (
    <div className={styles.page}>
      <header className={styles.header}>
        <h1 className={styles.title}>Qualidade do score</h1>
        <p className={styles.muted}>Números de cada pessoa, sem as vagas. Abaixo de 20 rótulos aparece a contagem em vez da taxa.</p>
      </header>
      {people.isError && <Alert>{errorMessage(people.error)}</Alert>}
      <div className={styles.tableWrap}>
        <table className={styles.table}>
          <thead>
            <tr>
              <th>Pessoa</th>
              <th>Precisão</th>
              <th>Modelo de trabalho</th>
              <th>Destaques perdidos</th>
              <th>Sinais (funil/arquivo)</th>
            </tr>
          </thead>
          <tbody>
            {people.data?.map((person) => (
              <tr key={person.username}>
                <td>
                  <span className={styles.name}>{person.username}</span>
                  {person.metrics.inFirstThirtyDays && <span className={styles.dim}> · primeiros 30 dias</span>}
                </td>
                <td className={styles.mono}>{cell(person.metrics.precision)}</td>
                <td className={styles.mono}>{cell(person.metrics.workMode)}</td>
                <td className={styles.mono}>{cell(person.metrics.missedHighlights)}</td>
                <td className={styles.mono}>
                  {person.metrics.signalsRelevant}/{person.metrics.signalsNotRelevant}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </div>
  )
}
