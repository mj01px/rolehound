import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { QRCodeSVG } from 'qrcode.react'
import { useState, type FormEvent } from 'react'
import { useNavigate } from 'react-router'
import { ApiError, api, errorMessage } from '../../lib/api'
import { Alert, Notice } from '../../ui/Alert'
import { Button } from '../../ui/Button'
import { Field } from '../../ui/Field'
import { PASSWORD_HELP, usePasswordForm } from '../auth/passwordForm'
import { meKey, type TwoFactorMethod } from '../auth/api'
import { useSensitive } from './sensitive'
import styles from './account.module.css'

interface Security {
  email: string
  twoFactorMethod: TwoFactorMethod
  recoveryCodesLeft: number | null
}

const securityKey = ['account', 'security'] as const

const isCancel = (error: unknown) => error instanceof Error && error.message === 'cancelled'

export function SettingsPage() {
  const security = useQuery({ queryKey: securityKey, queryFn: ({ signal }) => api.get<Security>('/account/security', signal) })

  return (
    <div className={styles.page}>
      <h1 className={styles.title}>Configurações</h1>
      <section className={styles.section} aria-labelledby="security-title">
        <h2 id="security-title" className={styles.sectionTitle}>
          Segurança
        </h2>
        {security.data && (
          <>
            <EmailRow email={security.data.email} />
            <PasswordRow />
            <TwoFactorRow security={security.data} />
            <SessionsRow />
          </>
        )}
        <p className={styles.muted} style={{ paddingTop: 10 }}>
          Trocar e-mail, senha ou verificação pede sua senha de novo.
        </p>
      </section>
    </div>
  )
}

function EmailRow({ email }: { email: string }) {
  const sensitive = useSensitive()
  const [open, setOpen] = useState(false)
  const [newEmail, setNewEmail] = useState('')
  const change = useMutation({ mutationFn: () => sensitive(() => api.post('/account/email', { newEmail })) })

  const submit = (event: FormEvent) => {
    event.preventDefault()
    change.mutate()
  }

  return (
    <div className={styles.row}>
      <div className={styles.rowText}>
        <span className={styles.rowTitle}>E-mail de login</span>
        <span className={styles.muted}>{email}</span>
      </div>
      {!open && (
        <Button variant="secondary" onClick={() => setOpen(true)}>
          Trocar
        </Button>
      )}
      {open && (
        <form className={styles.panel} onSubmit={submit} noValidate>
          {change.isSuccess ? (
            <Notice>Enviamos um link para {newEmail}. A troca vale quando você confirmar por lá.</Notice>
          ) : (
            <>
              <Field label="Novo e-mail" type="email" autoComplete="email" value={newEmail} onChange={(e) => setNewEmail(e.target.value)} error={change.error instanceof ApiError ? change.error.field('newEmail') : undefined} />
              <div className={styles.actions}>
                <Button type="submit" disabled={change.isPending || !newEmail}>
                  Enviar link de confirmação
                </Button>
                <Button variant="ghost" onClick={() => setOpen(false)}>
                  Cancelar
                </Button>
              </div>
            </>
          )}
        </form>
      )}
    </div>
  )
}

function PasswordRow() {
  const sensitive = useSensitive()
  const [open, setOpen] = useState(false)
  const form = usePasswordForm()
  const change = useMutation({
    mutationFn: () => sensitive(() => api.post('/account/password', { newPassword: form.password })),
    onSuccess: () => setOpen(false),
  })

  const submit = (event: FormEvent) => {
    event.preventDefault()
    if (form.validate()) {
      change.mutate()
    }
  }

  const fieldError = change.error instanceof ApiError ? change.error.field('newPassword') : undefined

  return (
    <div className={styles.row}>
      <div className={styles.rowText}>
        <span className={styles.rowTitle}>Senha</span>
        <span className={styles.muted}>{change.isSuccess ? 'Senha alterada. As outras sessões foram encerradas.' : 'Trocar a senha encerra as outras sessões.'}</span>
      </div>
      {!open && (
        <Button variant="secondary" onClick={() => setOpen(true)}>
          Trocar
        </Button>
      )}
      {open && (
        <form className={styles.panel} onSubmit={submit} noValidate>
          <Field label="Nova senha" type="password" autoComplete="new-password" help={PASSWORD_HELP} error={fieldError} value={form.password} onChange={(e) => form.setPassword(e.target.value)} />
          <Field label="Confirmar senha" type="password" autoComplete="new-password" value={form.confirm} onChange={(e) => form.setConfirm(e.target.value)} error={form.mismatch ? 'As senhas não são iguais.' : undefined} />
          <div className={styles.actions}>
            <Button type="submit" disabled={change.isPending}>
              Salvar senha
            </Button>
            <Button variant="ghost" onClick={() => setOpen(false)}>
              Cancelar
            </Button>
          </div>
        </form>
      )}
    </div>
  )
}

type Setup = { kind: 'totp'; sharedKey: string; authenticatorUri: string } | { kind: 'email' }

function TwoFactorRow({ security }: { security: Security }) {
  const sensitive = useSensitive()
  const queryClient = useQueryClient()
  const [setup, setSetup] = useState<Setup | null>(null)
  const [code, setCode] = useState('')
  const [recoveryCodes, setRecoveryCodes] = useState<string[] | null>(null)

  const refresh = async () => {
    await queryClient.invalidateQueries({ queryKey: securityKey })
    await queryClient.invalidateQueries({ queryKey: meKey })
  }

  const start = useMutation({
    mutationFn: async (kind: 'totp' | 'email') =>
      kind === 'totp'
        ? { kind, ...(await sensitive(() => api.post<{ sharedKey: string; authenticatorUri: string }>('/account/two-factor/totp'))) }
        : (await sensitive(() => api.post('/account/two-factor/email')), { kind }),
    onSuccess: (result) => {
      setCode('')
      setSetup(result as Setup)
    },
  })

  const enable = useMutation({
    mutationFn: async () => {
      if (setup?.kind === 'totp') {
        const result = await sensitive(() => api.post<{ recoveryCodes: string[] }>('/account/two-factor/totp/enable', { code }))
        setRecoveryCodes(result.recoveryCodes)
      } else {
        await sensitive(() => api.post('/account/two-factor/email/enable', { code }))
      }
    },
    onSuccess: async () => {
      setSetup(null)
      await refresh()
    },
  })

  const disable = useMutation({ mutationFn: () => sensitive(() => api.post('/account/two-factor/disable')), onSuccess: refresh })
  const regenerate = useMutation({
    mutationFn: () => sensitive(() => api.post<{ recoveryCodes: string[] }>('/account/two-factor/recovery-codes')),
    onSuccess: async (result) => {
      setRecoveryCodes(result.recoveryCodes)
      await refresh()
    },
  })

  const error = [start.error, enable.error, disable.error, regenerate.error].find((e) => e && !isCancel(e))
  const method = security.twoFactorMethod
  const status =
    method === 'totp'
      ? `Ativa por app autenticador · ${security.recoveryCodesLeft ?? 0} de 10 códigos de recuperação`
      : method === 'email'
        ? 'Ativa por código no e-mail'
        : 'Desativada'

  const submitCode = (event: FormEvent) => {
    event.preventDefault()
    enable.mutate()
  }

  return (
    <div className={styles.row}>
      <div className={styles.rowText}>
        <span className={styles.rowTitle}>Verificação em duas etapas</span>
        <span className={styles.muted}>{status}</span>
      </div>
      {method === 'none' && !setup && (
        <div className={styles.actions}>
          <Button variant="secondary" onClick={() => start.mutate('totp')} disabled={start.isPending}>
            Ativar por app
          </Button>
          <Button variant="secondary" onClick={() => start.mutate('email')} disabled={start.isPending}>
            Ativar por e-mail
          </Button>
        </div>
      )}
      {method !== 'none' && (
        <div className={styles.actions}>
          {method === 'totp' && (
            <Button variant="secondary" onClick={() => regenerate.mutate()} disabled={regenerate.isPending}>
              Novos códigos de recuperação
            </Button>
          )}
          <Button variant="secondary" onClick={() => disable.mutate()} disabled={disable.isPending}>
            Desativar
          </Button>
        </div>
      )}

      {error && (
        <div className={styles.panel}>
          <Alert>{errorMessage(error)}</Alert>
        </div>
      )}

      {setup && (
        <form className={styles.panel} onSubmit={submitCode} noValidate>
          {setup.kind === 'totp' ? (
            <>
              <p className={styles.muted}>Aponte a câmera do app autenticador para o código. Sem câmera, digite a chave abaixo.</p>
              <div className={styles.qr}>
                {/* Fixed dark on light: scanners need that contrast, whatever the theme. */}
                <QRCodeSVG value={setup.authenticatorUri} size={168} marginSize={2} bgColor="#fafafa" fgColor="#09090b" title="QR code para o app autenticador" />
              </div>
              <p className={styles.key}>{setup.sharedKey}</p>
            </>
          ) : (
            <p className={styles.muted}>Enviamos um código de 6 dígitos para {security.email}.</p>
          )}
          <Field label="Código" inputMode="numeric" autoComplete="one-time-code" maxLength={6} value={code} onChange={(e) => setCode(e.target.value)} error={enable.error instanceof ApiError ? enable.error.field('code') : undefined} />
          <div className={styles.actions}>
            <Button type="submit" disabled={enable.isPending || code.length < 6}>
              Ativar
            </Button>
            <Button variant="ghost" onClick={() => setSetup(null)}>
              Cancelar
            </Button>
          </div>
        </form>
      )}

      {recoveryCodes && (
        <div className={styles.panel}>
          <Notice>Guarde estes códigos de recuperação. Cada um funciona uma vez, e eles não aparecem de novo.</Notice>
          <ul className={styles.codes}>
            {recoveryCodes.map((c) => (
              <li key={c}>{c}</li>
            ))}
          </ul>
          <div className={styles.actions}>
            <Button variant="secondary" onClick={() => setRecoveryCodes(null)}>
              Já guardei
            </Button>
          </div>
        </div>
      )}
    </div>
  )
}

function SessionsRow() {
  const navigate = useNavigate()
  const queryClient = useQueryClient()
  const revoke = useMutation({
    mutationFn: () => api.post('/account/sessions/revoke'),
    onSuccess: () => {
      queryClient.clear()
      navigate('/entrar', { replace: true, state: { notice: 'Você saiu de todas as sessões.' } })
    },
  })

  return (
    <div className={styles.row}>
      <div className={styles.rowText}>
        <span className={styles.rowTitle}>Sessões</span>
        <span className={styles.muted}>Encerra o acesso em todos os aparelhos, inclusive este.</span>
      </div>
      <Button variant="secondary" onClick={() => revoke.mutate()} disabled={revoke.isPending}>
        Sair de todas
      </Button>
    </div>
  )
}
