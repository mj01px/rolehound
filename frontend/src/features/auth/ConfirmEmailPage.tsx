import { useMutation } from '@tanstack/react-query'
import { useEffect, useRef, useState } from 'react'
import { Link } from 'react-router'
import { api } from '../../lib/api'
import uiStyles from '../../ui/ui.module.css'
import { takeHashToken } from './api'
import { AuthLayout } from './AuthLayout'
import { LinkExpired } from './PasswordResetPages'

/** Confirms a new login email. Works without a session; every session ends afterwards. */
export function ConfirmEmailPage() {
  const [token] = useState(takeHashToken)
  const confirm = useMutation({ mutationFn: () => api.post('/auth/email/confirm', { token }) })
  const { mutate } = confirm
  const sent = useRef(false)

  // Links are single use: send once, even when React runs effects twice in development.
  useEffect(() => {
    if (token && !sent.current) {
      sent.current = true
      mutate()
    }
  }, [token, mutate])

  if (token === null || confirm.isError) {
    return <LinkExpired message="Este link de confirmação não vale mais. Peça a troca de e-mail de novo nas configurações." />
  }

  if (!confirm.isSuccess) {
    return <AuthLayout title="Confirmando…">{null}</AuthLayout>
  }

  return (
    <AuthLayout title="E-mail trocado" subtitle="Entre de novo usando o e-mail novo.">
      <Link to="/entrar" className={`${uiStyles.button} ${uiStyles.primary} ${uiStyles.large} ${uiStyles.block}`}>
        Entrar
      </Link>
    </AuthLayout>
  )
}
