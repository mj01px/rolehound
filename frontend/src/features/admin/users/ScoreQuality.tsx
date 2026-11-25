import { useQuery } from '@tanstack/react-query'
import { api, errorMessage } from '../../../lib/api'
import { Alert } from '../../../ui/Alert'
import { percent, type Metrics, type Rate } from '../../accuracy/api'
import styles from './users.module.css'

/** Below 20 labels the rate means little, so the count shows instead. */
const shown = (rate: Rate) => percent(rate) ?? `${rate.hits}/${rate.total}`

/** One person's score quality, numbers only: the admin never sees their jobs (FR-ACU-04). */
export function ScoreQuality({ userId }: { userId: string }) {
  const metrics = useQuery({ queryKey: ['admin', 'users', userId, 'accuracy'], queryFn: ({ signal }) => api.get<Metrics>(`/admin/users/${userId}/accuracy`, signal) })
  const data = metrics.data

  return (
    <section aria-labelledby="quality-title" className={styles.panel}>
      <div className={styles.panelHeadRow}>
        <h2 id="quality-title" className={styles.panelTitle}>
          Qualidade do score
        </h2>
        <span className={styles.panelHint}>{data?.inFirstThirtyDays ? 'Primeiros 30 dias de uso · ' : ''}Só números, sem as vagas. Abaixo de 20 rótulos aparece a contagem.</span>
      </div>
      {metrics.isError && <Alert>{errorMessage(metrics.error)}</Alert>}
      {data && (
        <dl className={styles.stats}>
          <Stat label="Precisão dos destaques" value={shown(data.precision)} hint="Destaques que a pessoa marcou como relevantes" />
          <Stat label="Modelo de trabalho" value={shown(data.workMode)} hint="Remoto, híbrido ou presencial certo" />
          <Stat label="Destaques perdidos" value={shown(data.missedHighlights)} hint="Candidaturas em vagas que não eram destaque" />
          <Stat label="Sinais" value={`${data.signalsRelevant}/${data.signalsNotRelevant}`} hint="Vagas levadas ao funil / arquivadas" />
        </dl>
      )}
    </section>
  )
}

function Stat({ label, value, hint }: { label: string; value: string; hint: string }) {
  return (
    <div className={styles.stat}>
      <dt>{label}</dt>
      <dd className={styles.statValue}>{value}</dd>
      <dd className={styles.statHint}>{hint}</dd>
    </div>
  )
}
