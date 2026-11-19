import { useMutation } from '@tanstack/react-query'
import { useCallback, useEffect, useRef, useState, type FormEvent, type ReactNode } from 'react'
import { ApiError, api } from '../../lib/api'
import { Alert, Notice } from '../../ui/Alert'
import { Button } from '../../ui/Button'
import { Field } from '../../ui/Field'
import uiStyles from '../../ui/ui.module.css'
import { useMe } from '../auth/api'
import styles from './account.module.css'
import { ReauthContext, type Sensitive } from './sensitive'

interface Pending {
  resolve: () => void
  reject: (reason: Error) => void
}

export function ReauthProvider({ children }: { children: ReactNode }) {
  const [pending, setPending] = useState<Pending | null>(null)

  const ask = useCallback(() => new Promise<void>((resolve, reject) => setPending({ resolve, reject })), [])

  const sensitive = useCallback<Sensitive>(
    async (action) => {
      try {
        return await action()
      } catch (error) {
        if (error instanceof ApiError && error.code === 'reauthentication_required') {
          await ask()
          return action()
        }
        throw error
      }
    },
    [ask],
  )

  return (
    <ReauthContext value={sensitive}>
      {children}
      {pending && (
        <ReauthDialog
          onDone={() => {
            pending.resolve()
            setPending(null)
          }}
          onCancel={() => {
            pending.reject(new Error('cancelled'))
            setPending(null)
          }}
        />
      )}
    </ReauthContext>
  )
}

function ReauthDialog({ onDone, onCancel }: { onDone: () => void; onCancel: () => void }) {
  const dialog = useRef<HTMLDialogElement>(null)
  const method = useMe().data?.twoFactorMethod ?? 'none'
  const [password, setPassword] = useState('')
  const [code, setCode] = useState('')

  useEffect(() => {
    dialog.current?.showModal()
  }, [])

  const check = useMutation({
    mutationFn: () => api.post('/account/reauthenticate', { password, code: method === 'none' ? null : code }),
    onSuccess: onDone,
  })
  const sendCode = useMutation({ mutationFn: () => api.post('/account/reauthenticate/code') })

  const submit = (event: FormEvent) => {
    event.preventDefault()
    check.mutate()
  }

  const error = check.error instanceof ApiError ? check.error : null

  return (
    <dialog ref={dialog} className={styles.dialog} onCancel={onCancel} aria-labelledby="reauth-title">
      <form className={styles.dialogForm} onSubmit={submit} noValidate>
        <div className={styles.dialogHead}>
          <h2 id="reauth-title" className={styles.dialogTitle}>
            Confirme que é você
          </h2>
          <p className={styles.muted}>Ações sensíveis pedem sua senha de novo. Vale por 10 minutos.</p>
        </div>
        {error && !error.field('password') && !error.field('code') && <Alert>{error.problem.detail}</Alert>}
        {sendCode.isSuccess && <Notice>Enviamos um código para o seu e-mail.</Notice>}
        <Field label="Senha" type="password" autoComplete="current-password" value={password} onChange={(e) => setPassword(e.target.value)} error={error?.field('password')} autoFocus />
        {method !== 'none' && (
          <Field
            label={method === 'email' ? 'Código do e-mail' : 'Código do app autenticador'}
            inputMode="numeric"
            autoComplete="one-time-code"
            maxLength={6}
            value={code}
            onChange={(e) => setCode(e.target.value)}
            error={error?.field('code')}
            aside={
              method === 'email' ? (
                <button type="button" className={uiStyles.link} onClick={() => sendCode.mutate()} disabled={sendCode.isPending}>
                  Enviar código
                </button>
              ) : undefined
            }
          />
        )}
        <div className={styles.dialogActions}>
          <Button variant="ghost" onClick={onCancel}>
            Cancelar
          </Button>
          <Button type="submit" disabled={check.isPending || !password}>
            Confirmar
          </Button>
        </div>
      </form>
    </dialog>
  )
}
