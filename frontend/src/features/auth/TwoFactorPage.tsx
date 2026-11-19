import { useMutation, useQueryClient } from '@tanstack/react-query'
import { useState, type FormEvent } from 'react'
import { Link, Navigate, useLocation, useNavigate } from 'react-router'
import { ApiError, api, errorMessage } from '../../lib/api'
import { Alert, Notice } from '../../ui/Alert'
import { Button } from '../../ui/Button'
import { Field } from '../../ui/Field'
import uiStyles from '../../ui/ui.module.css'
import { meKey, type LoginResponse, type TwoFactorMethod } from './api'
import { AuthLayout } from './AuthLayout'
import styles from './auth.module.css'

export function TwoFactorPage() {
  const navigate = useNavigate()
  const queryClient = useQueryClient()
  const method = (useLocation().state as { method?: TwoFactorMethod } | null)?.method
  const [code, setCode] = useState('')
  const [useRecovery, setUseRecovery] = useState(false)

  const verify = useMutation({
    mutationFn: () => api.post<LoginResponse>('/auth/login/two-factor', { code, useRecoveryCode: useRecovery }),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: meKey })
      navigate('/', { replace: true })
    },
    onError: (error) => {
      if (error instanceof ApiError && error.code === 'two_factor_expired') {
        navigate('/entrar', { replace: true, state: { notice: error.problem.detail } })
      }
    },
  })

  const resend = useMutation({ mutationFn: () => api.post('/auth/login/two-factor/code') })

  if (!method) {
    return <Navigate to="/entrar" replace />
  }

  const submit = (event: FormEvent) => {
    event.preventDefault()
    verify.mutate()
  }

  const subtitle = useRecovery
    ? 'Digite um dos códigos de recuperação que você guardou.'
    : method === 'email'
      ? 'Enviamos um código de 6 dígitos para o seu e-mail.'
      : 'Digite o código de 6 dígitos do seu app autenticador.'

  return (
    <AuthLayout title="Verificação em duas etapas" subtitle={subtitle}>
      <form className={styles.form} onSubmit={submit} noValidate>
        {verify.isError && <Alert>{errorMessage(verify.error)}</Alert>}
        {resend.isSuccess && <Notice>Enviamos um código novo.</Notice>}
        <Field
          label={useRecovery ? 'Código de recuperação' : 'Código'}
          className={useRecovery ? undefined : styles.code}
          inputMode={useRecovery ? 'text' : 'numeric'}
          autoComplete="one-time-code"
          maxLength={useRecovery ? 11 : 6}
          value={code}
          onChange={(e) => setCode(e.target.value)}
          autoFocus
        />
        <Button type="submit" size="large" block disabled={verify.isPending || !code}>
          {verify.isPending ? 'Verificando…' : 'Verificar'}
        </Button>
        <div className={styles.links}>
          {method === 'totp' && (
            <button type="button" className={uiStyles.link} onClick={() => { setUseRecovery(!useRecovery); setCode('') }}>
              {useRecovery ? 'Usar o app autenticador' : 'Usar código de recuperação'}
            </button>
          )}
          {method === 'email' && (
            <button type="button" className={uiStyles.link} onClick={() => resend.mutate()} disabled={resend.isPending}>
              Reenviar código
            </button>
          )}
          <Link to="/entrar" className={uiStyles.link}>
            Voltar
          </Link>
        </div>
      </form>
    </AuthLayout>
  )
}
