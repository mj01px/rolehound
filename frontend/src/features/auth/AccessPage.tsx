import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useState, type FormEvent } from 'react'
import { useNavigate } from 'react-router'
import { ApiError, api, errorMessage } from '../../lib/api'
import { Alert } from '../../ui/Alert'
import { Button } from '../../ui/Button'
import { Field } from '../../ui/Field'
import uiStyles from '../../ui/ui.module.css'
import { meKey, takeHashToken } from './api'
import { AuthLayout } from './AuthLayout'
import styles from './auth.module.css'
import { PASSWORD_HELP, usePasswordForm } from './passwordForm'
import { LinkExpired } from './PasswordResetPages'

interface AccessLink {
  email: string
  username: string
  grantedBy: string | null
}

/** First access: the admin created the account; the person only sets a password. */
export function AccessPage() {
  const navigate = useNavigate()
  const queryClient = useQueryClient()
  const [token] = useState(takeHashToken)
  const form = usePasswordForm()

  const link = useQuery({
    queryKey: ['auth', 'access-link', token],
    queryFn: () => api.post<AccessLink>('/auth/access/inspect', { token }),
    enabled: token !== null,
    retry: false,
    staleTime: Infinity,
  })

  const accept = useMutation({
    mutationFn: () => api.post('/auth/access/accept', { token, password: form.password }),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: meKey })
      navigate('/configuracao-inicial', { replace: true })
    },
  })

  if (token === null || link.isError || (accept.error instanceof ApiError && accept.error.code === 'link_invalid')) {
    return <LinkExpired message="Este link de acesso não vale mais. Peça um novo ao administrador." />
  }

  if (!link.data) {
    return <AuthLayout title="rolehound">{null}</AuthLayout>
  }

  const submit = (event: FormEvent) => {
    event.preventDefault()
    if (form.validate()) {
      accept.mutate()
    }
  }

  const passwordError = accept.error instanceof ApiError ? accept.error.field('password') : undefined

  return (
    <AuthLayout
      title={`Olá, ${link.data.username}`}
      subtitle={`${link.data.grantedBy ? `${link.data.grantedBy} liberou` : 'Foi liberado'} seu acesso ao rolehound. Defina sua senha para entrar.`}
    >
      <form className={styles.form} onSubmit={submit} noValidate>
        {accept.isError && !passwordError && <Alert>{errorMessage(accept.error)}</Alert>}
        <div className={styles.fields}>
          <Field label="E-mail" type="email" value={link.data.email} readOnly className={uiStyles.input} />
          <Field label="Senha" type="password" autoComplete="new-password" help={PASSWORD_HELP} error={passwordError} value={form.password} onChange={(e) => form.setPassword(e.target.value)} autoFocus />
          <Field label="Confirmar senha" type="password" autoComplete="new-password" value={form.confirm} onChange={(e) => form.setConfirm(e.target.value)} error={form.mismatch ? 'As senhas não são iguais.' : undefined} />
        </div>
        <Button type="submit" size="large" block disabled={accept.isPending}>
          Definir senha
        </Button>
        <p className={uiStyles.help} style={{ margin: 0, textAlign: 'center' }}>
          Este link vale por 72 horas e só pode ser usado uma vez.
        </p>
      </form>
    </AuthLayout>
  )
}
