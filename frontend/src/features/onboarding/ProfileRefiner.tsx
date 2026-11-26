import { useMutation } from '@tanstack/react-query'
import { useId, useState } from 'react'
import { ApiError, api, errorMessage } from '../../lib/api'
import { Alert, Notice } from '../../ui/Alert'
import { Button } from '../../ui/Button'
import type { ProfileDocument } from './api'
import styles from './ProfileRefiner.module.css'

interface Impact {
  jobsAnalyzed: number
  matchingBefore: number
  matchingAfter: number
  highlightsBefore: number
  highlightsAfter: number
}

interface Refined {
  profile: ProfileDocument
  changes: string[]
  impact: Impact
}

const MAX_LENGTH = 500
const count = new Intl.NumberFormat('pt-BR')

interface ProfileRefinerProps {
  profile: ProfileDocument
  /** Keeps the revised profile: replaces the option during onboarding, saves it in settings. */
  onAccept: (profile: ProfileDocument) => void | Promise<void>
  acceptLabel: string
  /** Called with the revised profile while it waits for a decision, and with null once it is kept or dropped. */
  onPreview?: (profile: ProfileDocument | null) => void
}

/**
 * "Ask for a change": the person describes it in their own words, the AI
 * revises the profile, and the person sees each change and its effect on the
 * jobs before keeping it. Nothing is saved until they accept.
 */
export function ProfileRefiner({ profile, onAccept, acceptLabel, onPreview }: ProfileRefinerProps) {
  const id = useId()
  const [instruction, setInstruction] = useState('')
  const refine = useMutation({
    mutationFn: () => api.post<Refined>('/score-profile/refine', { profile, instruction }),
    onSuccess: (result) => onPreview?.(result.changes.length > 0 ? result.profile : null),
  })
  const discard = () => {
    refine.reset()
    onPreview?.(null)
  }
  const accept = useMutation({
    mutationFn: async (revised: ProfileDocument) => onAccept(revised),
    onSuccess: () => {
      setInstruction('')
      discard()
    },
  })

  const result = refine.data
  const fieldError = refine.error instanceof ApiError ? refine.error.field('instruction') : undefined
  const error = refine.error && !fieldError ? refine.error : accept.error

  return (
    <div className={styles.box}>
      <label htmlFor={id} className={styles.label}>
        Quer mudar algo neste perfil?
      </label>
      <textarea
        id={id}
        className={styles.textarea}
        rows={3}
        maxLength={MAX_LENGTH}
        value={instruction}
        placeholder="Ex.: não penalize vagas de C#, eu também trabalho com C#."
        aria-invalid={fieldError ? true : undefined}
        onChange={(event) => setInstruction(event.target.value)}
        disabled={refine.isPending || result !== undefined}
      />
      {fieldError && <span className={styles.fieldError}>{fieldError}</span>}
      {error && <Alert>{errorMessage(error)}</Alert>}

      {result === undefined ? (
        <div className={styles.actions}>
          <Button variant="secondary" onClick={() => refine.mutate()} disabled={refine.isPending || instruction.trim().length < 3}>
            {refine.isPending ? 'Ajustando…' : 'Pedir ajuste'}
          </Button>
          <span className={styles.hint}>A IA mostra o que mudaria. Nada muda até você aceitar.</span>
        </div>
      ) : result.changes.length === 0 ? (
        <>
          <Notice>A IA não mudou nada. Tente explicar de outro jeito.</Notice>
          <div className={styles.actions}>
            <Button variant="ghost" onClick={discard}>
              Voltar
            </Button>
          </div>
        </>
      ) : (
        <div className={styles.result}>
          <h3 className={styles.title}>O que mudaria</h3>
          <ul className={styles.changes}>
            {result.changes.map((change) => (
              <li key={change}>{change}</li>
            ))}
          </ul>
          <ImpactLine impact={result.impact} />
          <div className={styles.actions}>
            <Button onClick={() => accept.mutate(result.profile)} disabled={accept.isPending}>
              {acceptLabel}
            </Button>
            <Button variant="ghost" onClick={discard} disabled={accept.isPending}>
              Descartar
            </Button>
          </div>
        </div>
      )}
    </div>
  )
}

function ImpactLine({ impact }: { impact: Impact }) {
  if (impact.jobsAnalyzed === 0) {
    return <p className={styles.hint}>Ainda não há vagas abertas para medir o efeito.</p>
  }

  return (
    <p className={styles.impact}>
      Nas {count.format(impact.jobsAnalyzed)} vagas abertas: <Delta label="destaques" before={impact.highlightsBefore} after={impact.highlightsAfter} />
      {', '}
      <Delta label="vagas que combinam" before={impact.matchingBefore} after={impact.matchingAfter} />.
    </p>
  )
}

function Delta({ label, before, after }: { label: string; before: number; after: number }) {
  const tone = after > before ? styles.up : after < before ? styles.down : undefined
  return (
    <>
      {label} <span className={styles.number}>{count.format(before)}</span> → <span className={[styles.number, tone].filter(Boolean).join(' ')}>{count.format(after)}</span>
    </>
  )
}
