import { useMutation, useQueryClient } from '@tanstack/react-query'
import { useState, type FormEvent } from 'react'
import { Link } from 'react-router'
import { api, errorMessage } from '../../lib/api'
import { Alert } from '../../ui/Alert'
import { Button } from '../../ui/Button'
import { meKey, useMe } from '../auth/api'
import styles from './legal.module.css'

/** The acceptance itself: both texts linked, one checkbox, one button. Used in onboarding and when the texts change. */
export function TermsAccept({ onDone, changed = false }: { onDone: () => Promise<void> | void; changed?: boolean }) {
  const me = useMe().data
  const queryClient = useQueryClient()
  const [agreed, setAgreed] = useState(false)
  const accept = useMutation({
    mutationFn: () => api.post('/account/terms', { version: me?.termsVersion }),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: meKey })
      await onDone()
    },
  })

  const submit = (event: FormEvent) => {
    event.preventDefault()
    accept.mutate()
  }

  return (
    <form className={styles.accept} onSubmit={submit} noValidate>
      <p className={styles.acceptText}>
        {changed
          ? 'Os termos de uso e a política de privacidade mudaram. Leia a nova versão para continuar.'
          : 'Antes de começar, leia como o Rolehound funciona e o que ele faz com os seus dados.'}
      </p>
      <ul className={styles.acceptLinks}>
        <li>
          <Link to="/termos" target="_blank" rel="noreferrer">
            Termos de uso
          </Link>
        </li>
        <li>
          <Link to="/privacidade" target="_blank" rel="noreferrer">
            Política de privacidade
          </Link>
        </li>
      </ul>
      <label className={styles.check}>
        <input type="checkbox" checked={agreed} onChange={(event) => setAgreed(event.target.checked)} />
        <span>Li e aceito os termos de uso e a política de privacidade.</span>
      </label>
      {accept.isError && <Alert>{errorMessage(accept.error)}</Alert>}
      <Button type="submit" size="large" block disabled={!agreed || accept.isPending || !me}>
        {accept.isPending ? 'Salvando…' : 'Aceitar e continuar'}
      </Button>
    </form>
  )
}
