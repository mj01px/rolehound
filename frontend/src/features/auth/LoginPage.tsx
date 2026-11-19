import { useMutation, useQueryClient } from '@tanstack/react-query'
import { useState, type FormEvent } from 'react'
import { Link, useLocation, useNavigate } from 'react-router'
import { api, errorMessage } from '../../lib/api'
import { Alert, Notice } from '../../ui/Alert'
import { Button } from '../../ui/Button'
import { Field } from '../../ui/Field'
import uiStyles from '../../ui/ui.module.css'
import { meKey, type LoginResponse } from './api'
import { AuthLayout } from './AuthLayout'
import styles from './auth.module.css'

export function LoginPage() {
  const navigate = useNavigate()
  const location = useLocation()
  const queryClient = useQueryClient()
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const notice = (location.state as { notice?: string } | null)?.notice

  const login = useMutation({
    mutationFn: () => api.post<LoginResponse>('/auth/login', { email, password }),
    onSuccess: async (result) => {
      if (result.status === 'twoFactorRequired') {
        navigate('/entrar/verificacao', { state: { method: result.method } })
        return
      }
      await queryClient.invalidateQueries({ queryKey: meKey })
      navigate('/', { replace: true })
    },
  })

  const submit = (event: FormEvent) => {
    event.preventDefault()
    login.mutate()
  }

  return (
    <AuthLayout title="Entrar no rolehound">
      <form className={styles.form} onSubmit={submit} noValidate>
        {notice && <Notice>{notice}</Notice>}
        {login.isError && <Alert>{errorMessage(login.error)}</Alert>}
        <div className={styles.fields}>
          <Field label="E-mail" type="email" autoComplete="email" placeholder="voce@exemplo.com" value={email} onChange={(e) => setEmail(e.target.value)} required />
          <Field
            label="Senha"
            type="password"
            autoComplete="current-password"
            value={password}
            onChange={(e) => setPassword(e.target.value)}
            aside={
              <Link to="/esqueci-senha" className={uiStyles.link}>
                Esqueci a senha
              </Link>
            }
            required
          />
        </div>
        <Button type="submit" size="large" block disabled={login.isPending || !email || !password}>
          {login.isPending ? 'Entrando…' : 'Entrar'}
        </Button>
      </form>
    </AuthLayout>
  )
}
