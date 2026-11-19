import { useMutation, useQueryClient } from '@tanstack/react-query'
import { useNavigate } from 'react-router'
import { api, errorMessage } from '../../lib/api'
import { Alert } from '../../ui/Alert'
import { Button } from '../../ui/Button'
import uiStyles from '../../ui/ui.module.css'
import { meKey } from '../auth/api'
import { onboardingKey } from './api'
import styles from './onboarding.module.css'

interface Result {
  jobsAnalyzed: number
  jobsMatching: number
  highlights: number
}

/** Scores the existing jobs with the new profile and opens the app (FR-ONB-04). */
export function FinishStep({ onBack }: { onBack?: (() => void) | undefined }) {
  const navigate = useNavigate()
  const queryClient = useQueryClient()
  const complete = useMutation({ mutationFn: () => api.post<Result>('/onboarding/complete') })

  const open = async () => {
    await queryClient.invalidateQueries({ queryKey: meKey })
    await queryClient.invalidateQueries({ queryKey: onboardingKey })
    navigate('/', { replace: true })
  }

  if (complete.data) {
    const r = complete.data
    return (
      <div className={styles.column}>
        <div className={styles.titles}>
          <h1 className={styles.title}>Tudo pronto</h1>
          <p className={styles.lead}>Pontuamos as vagas que já estavam no rolehound com o seu perfil. As novas entram a cada coleta.</p>
        </div>
        <div className={styles.summary}>
          <div className={styles.summaryRow}><span>Vagas analisadas</span><span className={styles.mono}>{r.jobsAnalyzed}</span></div>
          <div className={styles.summaryRow}><span>Batem com o seu perfil</span><span className={styles.mono}>{r.jobsMatching}</span></div>
          <div className={styles.summaryRow}>
            <span>Destaques</span>
            <span className={styles.mono}>
              <span className={styles.marker} aria-hidden="true" />
              {r.highlights}
            </span>
          </div>
        </div>
        <div className={styles.actions}>
          <Button size="large" block onClick={open}>
            Ver minhas vagas
          </Button>
          <p className={`${styles.hint} ${styles.center}`}>Avisos pelo Telegram podem ser ligados depois, em Configurações.</p>
        </div>
      </div>
    )
  }

  return (
    <div className={styles.column}>
      <div className={styles.titles}>
        <h1 className={styles.title}>Pontuar as vagas</h1>
        <p className={styles.lead}>Último passo: aplicamos seu perfil às vagas que já existem e liberamos o app.</p>
      </div>
      {complete.isError && <Alert>{errorMessage(complete.error)}</Alert>}
      <div className={styles.actions}>
        <Button size="large" block onClick={() => complete.mutate()} disabled={complete.isPending}>
          {complete.isPending ? 'Pontuando…' : 'Pontuar e concluir'}
        </Button>
        {onBack && (
          <button type="button" className={`${uiStyles.link} ${styles.center}`} onClick={onBack}>
            Voltar
          </button>
        )}
      </div>
    </div>
  )
}
