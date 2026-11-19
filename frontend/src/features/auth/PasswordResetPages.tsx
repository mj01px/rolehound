import { useMutation, useQuery } from '@tanstack/react-query'
import { useState, type FormEvent } from 'react'
import { Link, useLocation, useNavigate } from 'react-router'
import { ApiError, api, errorMessage } from '../../lib/api'
import { Alert, Notice } from '../../ui/Alert'
import { Button } from '../../ui/Button'
import { Field } from '../../ui/Field'
import uiStyles from '../../ui/ui.module.css'
import { takeHashToken } from './api'
import { AuthLayout } from './AuthLayout'
import styles from './auth.module.css'
import { PASSWORD_HELP, usePasswordForm } from './passwordForm'

export function ForgotPasswordPage() {
  const navigate = useNavigate()
  const [email, setEmail] = useState('')
  const forgot = useMutation({
    mutationFn: () => api.post('/auth/password/forgot', { email }),
    onSuccess: () => navigate('/esqueci-senha/enviado', { state: { email } }),
  })

  const submit = (event: FormEvent) => {
    event.preventDefault()
    forgot.mutate()
  }

  return (
    <AuthLayout title="Redefinir senha" subtitle="Enviaremos um link para o e-mail da sua conta.">
      <form className={styles.form} onSubmit={submit} noValidate>
        {forgot.isError && <Alert>{errorMessage(forgot.error)}</Alert>}
        <Field label="E-mail" type="email" autoComplete="email" placeholder="voce@exemplo.com" value={email} onChange={(e) => setEmail(e.target.value)} autoFocus />
        <Button type="submit" size="large" block disabled={forgot.isPending || !email}>
          Enviar link
        </Button>
        <Link to="/entrar" className={`${uiStyles.link} ${styles.center}`}>
          Voltar para o login
        </Link>
      </form>
    </AuthLayout>
  )
}

export function ForgotPasswordSentPage() {
  const email = (useLocation().state as { email?: string } | null)?.email ?? ''
  const resend = useMutation({ mutationFn: () => api.post('/auth/password/forgot', { email }) })

  return (
    <AuthLayout
      title="Confira seu e-mail"
      subtitle={
        <>
          Se existir uma conta com <strong>{email || 'esse e-mail'}</strong>, você vai receber um link para criar uma senha nova. Ele vale por 1 hora e funciona uma vez.
        </>
      }
    >
      {resend.isSuccess && <Notice>Enviamos outro link. Só o mais recente funciona.</Notice>}
      <div className={styles.links}>
        <button type="button" className={uiStyles.link} onClick={() => resend.mutate()} disabled={!email || resend.isPending}>
          Reenviar link
        </button>
        <Link to="/entrar" className={uiStyles.link}>
          Voltar para o login
        </Link>
      </div>
    </AuthLayout>
  )
}

export function ResetPasswordPage() {
  const navigate = useNavigate()
  const [token] = useState(takeHashToken)
  const form = usePasswordForm()

  const link = useQuery({
    queryKey: ['auth', 'reset-link', token],
    queryFn: () => api.post<{ email: string }>('/auth/password/reset/inspect', { token }),
    enabled: token !== null,
    retry: false,
    staleTime: Infinity,
  })

  const reset = useMutation({
    mutationFn: () => api.post('/auth/password/reset', { token, password: form.password }),
    onSuccess: () => navigate('/entrar', { replace: true, state: { notice: 'Senha alterada. Entre com a senha nova.' } }),
  })

  if (token === null || link.isError || (reset.error instanceof ApiError && reset.error.code === 'link_invalid')) {
    return <LinkExpired again="/esqueci-senha" message="O link vale por 1 hora e só pode ser usado uma vez. Se você pediu outro depois, só o mais recente funciona." />
  }

  const submit = (event: FormEvent) => {
    event.preventDefault()
    if (form.validate()) {
      reset.mutate()
    }
  }

  const passwordError = reset.error instanceof ApiError ? reset.error.field('password') : undefined

  return (
    <AuthLayout title="Nova senha" subtitle={link.data ? `Para ${link.data.email}` : ' '}>
      <form className={styles.form} onSubmit={submit} noValidate>
        {reset.isError && !passwordError && <Alert>{errorMessage(reset.error)}</Alert>}
        <div className={styles.fields}>
          <Field label="Nova senha" type="password" autoComplete="new-password" help={PASSWORD_HELP} value={form.password} onChange={(e) => form.setPassword(e.target.value)} error={passwordError} />
          <Field label="Confirmar senha" type="password" autoComplete="new-password" value={form.confirm} onChange={(e) => form.setConfirm(e.target.value)} error={form.mismatch ? 'As senhas não são iguais.' : undefined} />
        </div>
        <Button type="submit" size="large" block disabled={reset.isPending || !link.isSuccess}>
          Salvar senha
        </Button>
      </form>
    </AuthLayout>
  )
}

export function LinkExpired({ again, message }: { again?: string; message: string }) {
  return (
    <AuthLayout title="Link expirado" subtitle={message}>
      {again && (
        <Link to={again} className={uiStyles.button + ' ' + uiStyles.primary + ' ' + uiStyles.large + ' ' + uiStyles.block}>
          Pedir um link novo
        </Link>
      )}
      <Link to="/entrar" className={`${uiStyles.link} ${styles.center}`}>
        Voltar para o login
      </Link>
    </AuthLayout>
  )
}
