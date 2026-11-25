import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import { Navigate, useNavigate } from 'react-router'
import { api } from '../../lib/api'
import { Logo } from '../../ui/Logo'
import { ReauthProvider } from '../account/ReauthProvider'
import uiStyles from '../../ui/ui.module.css'
import { onboardingKey, STEPS, type OnboardingStep } from './api'
import { DossierStep } from './DossierStep'
import { FinishStep } from './FinishStep'
import { TermsAccept } from '../legal/TermsAccept'
import { KeyStep } from './KeyStep'
import { ModelsStep } from './ModelsStep'
import styles from './onboarding.module.css'
import { ProfileStep } from './ProfileStep'

/**
 * One screen per step (FR-ONB-02). The server says where the person stopped
 * (FR-ONB-03); "Voltar" lets them revisit an earlier step locally.
 */
export function OnboardingPage() {
  const navigate = useNavigate()
  const queryClient = useQueryClient()
  const status = useQuery({ queryKey: onboardingKey, queryFn: ({ signal }) => api.get<{ step: OnboardingStep }>('/onboarding', signal) })
  const [revisit, setRevisit] = useState<OnboardingStep | null>(null)

  const logout = useMutation({
    mutationFn: () => api.post('/auth/logout'),
    onSuccess: () => {
      queryClient.clear()
      navigate('/entrar', { replace: true })
    },
  })

  if (!status.data) {
    return null
  }

  if (status.data.step === 'done') {
    return <Navigate to="/" replace />
  }

  const step = revisit ?? status.data.step
  const index = STEPS.indexOf(step as (typeof STEPS)[number])

  const next = async () => {
    setRevisit(null)
    await queryClient.invalidateQueries({ queryKey: onboardingKey })
  }
  const back = index > 0 ? () => setRevisit(STEPS[index - 1] ?? null) : undefined

  return (
    <ReauthProvider>
    <div className={styles.page}>
      <header className={styles.header}>
        <Logo size={24} />
        <div className={styles.progress}>
          <span>
            Etapa {index + 1} de {STEPS.length}
          </span>
          <div role="progressbar" aria-valuenow={index + 1} aria-valuemin={1} aria-valuemax={STEPS.length} aria-label="Progresso" className={styles.segments}>
            {STEPS.map((s, i) => (
              <span key={s} className={i <= index ? `${styles.segment} ${styles.segmentOn}` : styles.segment} />
            ))}
          </div>
        </div>
        <button type="button" className={`${uiStyles.link} ${styles.exit}`} onClick={() => logout.mutate()}>
          Sair
        </button>
      </header>
      <main className={styles.main}>
        {step === 'terms' && (
          <div className={styles.column}>
            <div className={styles.titles}>
              <h1 className={styles.title}>Termos e privacidade</h1>
            </div>
            <TermsAccept onDone={next} />
          </div>
        )}
        {step === 'key' && <KeyStep onDone={next} />}
        {step === 'models' && <ModelsStep onDone={next} onBack={back} />}
        {step === 'dossier' && <DossierStep onDone={next} onBack={back} />}
        {step === 'profile' && <ProfileStep onDone={next} onBack={back} />}
        {step === 'scoring' && <FinishStep onBack={back} />}
      </main>
    </div>
    </ReauthProvider>
  )
}
