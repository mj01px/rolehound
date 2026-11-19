import { useMutation } from '@tanstack/react-query'
import { useState, type FormEvent } from 'react'
import { ApiError, api, errorMessage } from '../../lib/api'
import { Alert } from '../../ui/Alert'
import { Button } from '../../ui/Button'
import { Field } from '../../ui/Field'
import uiStyles from '../../ui/ui.module.css'
import { useSensitive } from '../account/sensitive'
import styles from './onboarding.module.css'

export function KeyStep({ onDone }: { onDone: () => Promise<void> }) {
  const sensitive = useSensitive()
  const [key, setKey] = useState('')
  // Replacing a saved key (coming back to this step) asks for the password again.
  const save = useMutation({ mutationFn: () => sensitive(() => api.put('/ai/key', { key })), onSuccess: onDone })

  const submit = (event: FormEvent) => {
    event.preventDefault()
    save.mutate()
  }

  const fieldError = save.error instanceof ApiError ? save.error.field('key') : undefined

  return (
    <form className={styles.column} onSubmit={submit} noValidate>
      <div className={styles.titles}>
        <h1 className={styles.title}>Conecte sua chave do OpenRouter</h1>
        <p className={styles.lead}>As funções de IA usam a sua própria chave. Ela fica criptografada no servidor e nunca volta para o navegador.</p>
      </div>
      {save.isError && !fieldError && save.error.message !== 'cancelled' && <Alert>{errorMessage(save.error)}</Alert>}
      <Field
        label="Chave da API"
        type="password"
        autoComplete="off"
        placeholder="sk-or-v1-…"
        className={styles.mono}
        value={key}
        onChange={(e) => setKey(e.target.value)}
        help="Testamos a chave antes de salvar."
        error={fieldError}
        aside={
          <a href="https://openrouter.ai/keys" target="_blank" rel="noreferrer" className={uiStyles.link}>
            Onde pegar a chave
          </a>
        }
        autoFocus
      />
      <Button type="submit" size="large" block disabled={save.isPending || !key}>
        {save.isPending ? 'Testando…' : 'Testar e continuar'}
      </Button>
    </form>
  )
}
