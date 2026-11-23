import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useState, type FormEvent } from 'react'
import { ApiError, api, errorMessage } from '../../lib/api'
import { Alert, Notice } from '../../ui/Alert'
import { Button } from '../../ui/Button'
import { Field } from '../../ui/Field'
import type { Models } from '../onboarding/api'
import styles from './admin.module.css'

/** The models suggested to each person during onboarding; each person can still pick others. */
export function AdminModelsPage() {
  const current = useQuery({ queryKey: ['admin', 'suggested'], queryFn: ({ signal }) => api.get<Models>('/admin/ai/suggested', signal) })

  return (
    <div className={styles.page}>
      <header className={styles.header}>
        <h1 className={styles.title}>Modelos sugeridos</h1>
        <p className={styles.muted}>O que aparece pré-selecionado para cada função de IA. Cada pessoa testa e pode trocar o seu ao salvar.</p>
      </header>
      {current.isError && <Alert>{errorMessage(current.error)}</Alert>}
      {current.data && <ModelsForm key={JSON.stringify(current.data)} initial={current.data} />}
    </div>
  )
}

function ModelsForm({ initial }: { initial: Models }) {
  const queryClient = useQueryClient()
  const [models, setModels] = useState(initial)
  const save = useMutation({
    mutationFn: () => api.put('/admin/ai/suggested', models),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['admin', 'suggested'] }),
  })
  const error = save.error instanceof ApiError ? save.error : null

  const submit = (event: FormEvent) => {
    event.preventDefault()
    save.mutate()
  }

  const field = (key: keyof Models, label: string) => (
    <Field
      label={label}
      value={models[key]}
      onChange={(event) => setModels((current) => ({ ...current, [key]: event.target.value }))}
      error={error?.field(key)}
      spellCheck={false}
      autoComplete="off"
    />
  )

  return (
    <form onSubmit={submit} noValidate style={{ display: 'flex', flexDirection: 'column', gap: 16, maxWidth: 520 }}>
      {field('pitchAndAts', 'Pitch e análise ATS')}
      {field('resume', 'Currículo adaptado')}
      {field('profileAndFix', 'Perfil de score e correção do Dossier')}
      {save.isSuccess && <Notice>Sugestões salvas.</Notice>}
      {save.isError && !error?.problem.errors && <Alert>{errorMessage(save.error)}</Alert>}
      <p className={styles.muted}>Use o identificador do OpenRouter, como deepseek/deepseek-chat. A lista completa está em openrouter.ai/models.</p>
      <div>
        <Button type="submit" disabled={save.isPending}>
          Salvar
        </Button>
      </div>
    </form>
  )
}
