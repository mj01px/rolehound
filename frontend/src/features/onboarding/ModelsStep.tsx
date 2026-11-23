import { useMutation, useQuery } from '@tanstack/react-query'
import { useState, type FormEvent } from 'react'
import { ApiError, api, errorMessage } from '../../lib/api'
import { Alert, Notice } from '../../ui/Alert'
import { Button } from '../../ui/Button'
import { ModelPicker } from '../../ui/ModelPicker'
import { MODEL_USAGE } from '../../ui/modelUsage'
import uiStyles from '../../ui/ui.module.css'
import type { AiSettings, Models } from './api'
import styles from './onboarding.module.css'

const SLOTS: { key: keyof Models; label: string }[] = [
  { key: 'pitchAndAts', label: 'Pitch e análise ATS' },
  { key: 'resume', label: 'Currículo sob medida' },
  { key: 'profileAndFix', label: 'Perfil de score e correção do Dossier' },
]

export function ModelsStep({ onDone, onBack }: { onDone: () => Promise<void>; onBack?: (() => void) | undefined }) {
  const settings = useQuery({ queryKey: ['ai', 'settings'], queryFn: ({ signal }) => api.get<AiSettings>('/ai/settings', signal) })

  if (!settings.data) {
    return null
  }

  return <ModelsForm initial={settings.data.models ?? settings.data.suggested} suggested={settings.data.suggested} onDone={onDone} onBack={onBack} />
}

function ModelsForm({ initial, suggested, onDone, onBack }: { initial: Models; suggested: Models; onDone: () => Promise<void>; onBack?: (() => void) | undefined }) {
  const [models, setModels] = useState<Models>(initial)
  const save = useMutation({ mutationFn: () => api.put('/ai/models', models), onSuccess: onDone })

  const submit = (event: FormEvent) => {
    event.preventDefault()
    save.mutate()
  }

  const error = save.error instanceof ApiError ? save.error : null
  const usesFree = Object.values(models).some((m) => m.endsWith(':free'))

  return (
    <form className={styles.column} onSubmit={submit} noValidate>
      <div className={styles.titles}>
        <h1 className={styles.title}>Escolha os modelos</h1>
        <p className={styles.lead}>Já deixamos um modelo sugerido para cada função. Se quiser, troque. Testamos cada um ao salvar.</p>
      </div>
      {error && !SLOTS.some((s) => error.field(s.key)) && <Alert>{errorMessage(save.error)}</Alert>}
      <div className={styles.stack}>
        {SLOTS.map((slot) => (
          <ModelPicker
            key={slot.key}
            label={slot.label}
            value={models[slot.key]}
            usage={MODEL_USAGE[slot.key]}
            suggested={suggested[slot.key]}
            onChange={(id) => setModels((current) => ({ ...current, [slot.key]: id }))}
            error={error?.field(slot.key)}
            aside={models[slot.key] === suggested[slot.key] ? <span className={styles.tag}>Sugerido</span> : undefined}
          />
        ))}
      </div>
      {usesFree ? (
        <Notice>
          Modelos gratuitos não pedem cartão no OpenRouter, mas podem usar o que você envia para treino, e o OpenRouter limita a quantidade de pedidos por dia. Se preferir mais privacidade, escolha um modelo pago: ele precisa de créditos comprados no OpenRouter.
        </Notice>
      ) : (
        <p className={styles.hint}>Modelos pagos usam créditos comprados no OpenRouter, com cartão ou cripto.</p>
      )}
      <div className={styles.actions}>
        <Button type="submit" size="large" block disabled={save.isPending}>
          {save.isPending ? 'Testando os modelos…' : 'Testar e continuar'}
        </Button>
        {onBack && (
          <button type="button" className={`${uiStyles.link} ${styles.center}`} onClick={onBack}>
            Voltar
          </button>
        )}
      </div>
    </form>
  )
}
