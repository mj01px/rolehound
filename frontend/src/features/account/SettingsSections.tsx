import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useState, type FormEvent } from 'react'
import { Link, useNavigate } from 'react-router'
import { ApiError, api, apiUrl, errorMessage } from '../../lib/api'
import { Alert } from '../../ui/Alert'
import { Button } from '../../ui/Button'
import { Field } from '../../ui/Field'
import { HelpGuide } from '../../ui/HelpGuide'
import { Modal, ModalHead } from '../../ui/Modal'
import { StepModal } from './StepModal'
import { managementKeyGuide } from './keyGuides'
import { KeyStep } from '../onboarding/KeyStep'
import { ModelsStep } from '../onboarding/ModelsStep'
import type { AiSettings } from '../onboarding/api'
import { useSensitive } from './sensitive'
import { usageQuery } from './usage'
import overlayStyles from '../../ui/overlay.module.css'
import styles from './account.module.css'

const isCancel = (error: unknown) => error instanceof Error && error.message === 'cancelled'

/** OpenRouter key and models, after onboarding (FR-AI-01, FR-AI-02). */
export function AiSection() {
  const queryClient = useQueryClient()
  const settings = useQuery({ queryKey: ['ai', 'settings'], queryFn: ({ signal }) => api.get<AiSettings>('/ai/settings', signal) })
  const [editing, setEditing] = useState<'key' | 'models' | null>(null)
  const done = async () => {
    setEditing(null)
    await queryClient.invalidateQueries({ queryKey: ['ai'] })
  }

  return (
    <section className={styles.section} aria-labelledby="ai-title">
      <h2 id="ai-title" className={styles.sectionTitle}>
        Inteligência artificial
      </h2>
      {settings.isError && <Alert>{errorMessage(settings.error)}</Alert>}
      {editing === 'key' && (
        <StepModal label="Trocar a chave do OpenRouter" onClose={() => setEditing(null)}>
          <KeyStep onDone={done} />
          <Button variant="ghost" onClick={() => setEditing(null)}>
            Cancelar
          </Button>
        </StepModal>
      )}
      {editing === 'models' && (
        <StepModal label="Mudar os modelos" onClose={() => setEditing(null)}>
          <ModelsStep onDone={done} onBack={() => setEditing(null)} />
        </StepModal>
      )}
      {settings.data && (
        <>
          <div className={styles.row}>
            <div className={styles.rowText}>
              <span className={styles.rowTitle}>Chave do OpenRouter</span>
              <span className={styles.muted}>{settings.data.hasKey ? `Cadastrada (${settings.data.keyHint ?? 'final oculto'})` : 'Nenhuma chave cadastrada'}</span>
            </div>
            <Button variant="secondary" onClick={() => setEditing('key')}>
              Trocar
            </Button>
          </div>
          <div className={styles.row}>
            <div className={styles.rowText}>
              <span className={styles.rowTitle}>Modelos</span>
              {settings.data.models ? (
                <dl className={styles.models}>
                  <dt>Pitch e ATS:</dt>
                  <dd>{settings.data.models.pitchAndAts}</dd>
                  <dt>Currículo:</dt>
                  <dd>{settings.data.models.resume}</dd>
                  <dt>Perfil:</dt>
                  <dd>{settings.data.models.profileAndFix}</dd>
                </dl>
              ) : (
                <span className={styles.muted}>Ainda não escolhidos</span>
              )}
            </div>
            <Button variant="secondary" onClick={() => setEditing('models')}>
              Mudar
            </Button>
          </div>
          <ManagementKeyRow hint={settings.data.managementKeyHint} />
        </>
      )}
    </section>
  )
}

/**
 * The optional management key, only to show the account's credit balance. It can create and
 * delete OpenRouter keys, so saving it asks for the password again and the risk is spelled out.
 */
function ManagementKeyRow({ hint }: { hint: string | null }) {
  const queryClient = useQueryClient()
  const sensitive = useSensitive()
  const [open, setOpen] = useState(false)
  const [key, setKey] = useState('')
  const refresh = () => queryClient.invalidateQueries({ queryKey: ['ai'] })
  const save = useMutation({
    mutationFn: () => sensitive(() => api.put('/ai/management-key', { key: key.trim() })),
    onSuccess: async () => {
      setOpen(false)
      setKey('')
      await refresh()
    },
  })
  const remove = useMutation({ mutationFn: () => api.delete('/ai/management-key'), onSuccess: refresh })
  const error = save.error instanceof ApiError ? save.error : null
  const cancelled = save.error instanceof Error && save.error.message === 'cancelled'

  const submit = (event: FormEvent) => {
    event.preventDefault()
    save.mutate()
  }

  return (
    <div className={styles.row}>
      <div className={styles.rowText}>
        <span className={styles.rowTitle}>Chave de gerenciamento (opcional)</span>
        <span className={styles.muted}>{hint ? `Cadastrada (${hint}). Usada só para mostrar o saldo da conta.` : 'Só para mostrar o saldo da sua conta no resumo acima.'}</span>
      </div>
      <div className={styles.actions}>
        {hint && (
          <Button variant="ghost" onClick={() => remove.mutate()} disabled={remove.isPending}>
            Remover
          </Button>
        )}
        <Button variant="secondary" onClick={() => setOpen(true)}>
          {hint ? 'Trocar' : 'Cadastrar'}
        </Button>
      </div>
      {open && (
        <Modal titleId="management-key-title" onClose={() => setOpen(false)}>
          <form className={overlayStyles.form} onSubmit={submit} noValidate>
            <ModalHead
              id="management-key-title"
              title="Chave de gerenciamento"
              text="Opcional. Serve só para mostrar o saldo da sua conta do OpenRouter."
              onClose={() => setOpen(false)}
            />
            <Field
              label="Chave de gerenciamento"
              aside={<HelpGuide guide={managementKeyGuide} label="Como pegar a chave de gerenciamento" />}
              type="password"
              autoComplete="off"
              value={key}
              onChange={(event) => setKey(event.target.value)}
              error={error?.field('key')}
            />
            {error && !error.field('key') && <Alert>{errorMessage(error)}</Alert>}
            {save.isError && !error && !cancelled && <Alert>{errorMessage(save.error)}</Alert>}
            <div className={overlayStyles.actions}>
              <Button type="submit" disabled={save.isPending || !key.trim()}>
                {save.isPending ? 'Conferindo…' : 'Salvar'}
              </Button>
              <Button variant="ghost" onClick={() => setOpen(false)}>
                Cancelar
              </Button>
            </div>
          </form>
        </Modal>
      )}
      {remove.isError && <Alert>{errorMessage(remove.error)}</Alert>}
    </div>
  )
}

interface DossierInfo {
  number: number
  createdAt: string
}

/** The Dossier and the score profile: each has its own page, where it is read and changed (FR-DOS-07, FR-SCO-02). */
export function DossierSection() {
  const dossier = useQuery({ queryKey: ['dossier'], queryFn: ({ signal }) => api.get<DossierInfo>('/dossier', signal) })

  return (
    <section className={styles.section} aria-labelledby="dossier-title">
      <h2 id="dossier-title" className={styles.sectionTitle}>
        Dossier e perfil de score
      </h2>
      <div className={styles.row}>
        <div className={styles.rowText}>
          <span className={styles.rowTitle}>Dossier</span>
          <span className={styles.muted}>{dossier.data ? `Versão ${dossier.data.number}, de ${new Date(dossier.data.createdAt).toLocaleDateString('pt-BR')}` : 'Carregando'}</span>
        </div>
        {dossier.data && (
          <Link to="/configuracoes/dossier" className={styles.buttonLink}>
            Ver e importar
          </Link>
        )}
      </div>
      <div className={styles.row}>
        <div className={styles.rowText}>
          <span className={styles.rowTitle}>Perfil de score</span>
          <span className={styles.muted}>Os grupos de termos e pesos que dão a nota das vagas.</span>
        </div>
        <Link to="/configuracoes/perfil" className={styles.buttonLink}>
          Ver e ajustar
        </Link>
      </div>
    </section>
  )
}

const featureLabel: Record<string, string> = {
  modeltest: 'Teste de modelo',
  dossierfix: 'Correção do Dossier',
  scoreprofile: 'Perfil de score',
  pitch: 'Pitch',
  ats: 'Análise ATS',
  resume: 'Currículo adaptado',
}

/** Tokens spent on the person's own key, by feature and model (FR-AI-13). */
export function UsageSection() {
  const usage = useQuery(usageQuery)
  const rows = usage.data?.last30Days ?? []

  return (
    <section id="uso" className={styles.section} aria-labelledby="usage-title">
      <h2 id="usage-title" className={styles.sectionTitle}>
        Consumo de IA nos últimos 30 dias
      </h2>
      {usage.isError && <Alert>{errorMessage(usage.error)}</Alert>}
      {usage.data && rows.length === 0 && <p className={styles.muted}>Nenhum uso de IA nos últimos 30 dias.</p>}
      {rows.length > 0 && (
        <div className={styles.tableWrap}>
          <table className={styles.table}>
            <thead>
              <tr>
                <th>Função</th>
                <th>Modelo</th>
                <th>Pedidos</th>
                <th>Tokens enviados</th>
                <th>Tokens recebidos</th>
              </tr>
            </thead>
            <tbody>
              {rows.map((row) => (
                <tr key={`${row.feature}-${row.model}`}>
                  <td>{featureLabel[row.feature] ?? row.feature}</td>
                  <td className={styles.mono}>{row.model}</td>
                  <td className={styles.mono}>
                    {row.calls}
                    {row.failed > 0 && <span className={styles.muted}> ({row.failed} com erro)</span>}
                  </td>
                  <td className={styles.mono}>{row.promptTokens.toLocaleString('pt-BR')}</td>
                  <td className={styles.mono}>{row.completionTokens.toLocaleString('pt-BR')}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
      <p className={styles.muted}>Tokens contados pelo Rolehound. O gasto em dinheiro desta chave está no resumo acima.</p>
    </section>
  )
}

/** Export everything and delete the account (FR-DATA-01, FR-DATA-02). */
export function DataSection() {
  const sensitive = useSensitive()
  const navigate = useNavigate()
  const queryClient = useQueryClient()
  const [confirm, setConfirm] = useState('')
  const remove = useMutation({
    mutationFn: () => sensitive(() => api.delete('/account')),
    onSuccess: () => {
      queryClient.clear()
      navigate('/entrar', { replace: true, state: { notice: 'Sua conta e seus dados foram apagados.' } })
    },
  })

  return (
    <section className={styles.section} aria-labelledby="data-title">
      <h2 id="data-title" className={styles.sectionTitle}>
        Seus dados
      </h2>
      <div className={styles.row}>
        <div className={styles.rowText}>
          <span className={styles.rowTitle}>Exportar</span>
          <span className={styles.muted}>Tudo o que o Rolehound guarda sobre você, num arquivo JSON. A chave do OpenRouter não vai junto.</span>
        </div>
        <a className={styles.buttonLink} href={apiUrl('/account/export')} download>
          Baixar
        </a>
      </div>
      <div className={styles.row}>
        <div className={styles.rowText}>
          <span className={styles.rowTitle}>Termos e privacidade</span>
          <span className={styles.muted}>
            <Link to="/termos" target="_blank" rel="noreferrer" className={styles.textLink}>
              Termos de uso
            </Link>
            {' · '}
            <Link to="/privacidade" target="_blank" rel="noreferrer" className={styles.textLink}>
              Política de privacidade
            </Link>
          </span>
        </div>
      </div>
      <div className={styles.row} style={{ flexDirection: 'column', alignItems: 'stretch' }}>
        <div className={styles.rowText}>
          <span className={styles.rowTitle}>Apagar conta</span>
          <span className={styles.muted}>
            Apaga para sempre sua conta, Dossier, perfil, funil, textos gerados e currículo. As vagas continuam, porque são compartilhadas. Não dá para desfazer.
          </span>
        </div>
        <div className={styles.actions}>
          <input
            className={styles.input}
            aria-label='Digite "apagar" para confirmar'
            placeholder='Digite "apagar" para confirmar'
            value={confirm}
            onChange={(e) => setConfirm(e.target.value)}
          />
          <Button variant="danger" onClick={() => remove.mutate()} disabled={confirm.trim().toLowerCase() !== 'apagar' || remove.isPending}>
            Apagar minha conta
          </Button>
        </div>
        {remove.isError && !isCancel(remove.error) && <Alert>{errorMessage(remove.error)}</Alert>}
      </div>
    </section>
  )
}

interface TelegramStatus {
  available: boolean
  connected: boolean
  linkedAt: string | null
}

/** Optional Telegram notifications: new highlights and overdue follow-ups (PRD 11). */
export function TelegramSection() {
  const queryClient = useQueryClient()
  const [link, setLink] = useState<{ url: string; expiresAt: string } | null>(null)
  const status = useQuery({
    queryKey: ['telegram'],
    queryFn: ({ signal }) => api.get<TelegramStatus>('/telegram', signal),
    // While a link is open, check every few seconds whether the chat was connected.
    refetchInterval: (query) => (link && !query.state.data?.connected ? 3000 : false),
  })
  const start = useMutation({ mutationFn: () => api.post<{ url: string; expiresAt: string }>('/telegram/link'), onSuccess: setLink })
  const stop = useMutation({
    mutationFn: () => api.delete('/telegram/link'),
    onSuccess: () => {
      setLink(null)
      return queryClient.invalidateQueries({ queryKey: ['telegram'] })
    },
  })
  const data = status.data

  return (
    <section className={styles.section} aria-labelledby="telegram-title">
      <h2 id="telegram-title" className={styles.sectionTitle}>
        Telegram
      </h2>
      {status.isError && <Alert>{errorMessage(status.error)}</Alert>}
      {data && !data.available && <p className={styles.muted}>Os avisos pelo Telegram ainda não foram ligados neste servidor.</p>}
      {data?.available && (
        <div className={styles.row} style={{ flexDirection: 'column', alignItems: 'stretch' }}>
          <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', gap: 12 }}>
            <div className={styles.rowText}>
              <span className={styles.rowTitle}>Avisos no Telegram</span>
              <span className={styles.muted}>
                {data.connected
                  ? `Conectado desde ${new Date(data.linkedAt!).toLocaleDateString('pt-BR')}. Destaques novos depois de cada coleta e follow-ups atrasados às 9h.`
                  : 'Destaques novos depois de cada coleta e follow-ups atrasados às 9h. As mensagens não levam dados do seu Dossier.'}
              </span>
            </div>
            {data.connected ? (
              <Button variant="secondary" onClick={() => stop.mutate()} disabled={stop.isPending}>
                Desconectar
              </Button>
            ) : (
              <Button variant="secondary" onClick={() => start.mutate()} disabled={start.isPending}>
                Conectar
              </Button>
            )}
          </div>
          {link && !data.connected && (
            <div className={styles.actions}>
              <a className={styles.buttonLink} href={link.url} target="_blank" rel="noopener noreferrer">
                Abrir no Telegram
              </a>
              <span className={styles.muted}>Toque em Começar no bot. O link vale até {new Date(link.expiresAt).toLocaleTimeString('pt-BR', { timeStyle: 'short' })}.</span>
            </div>
          )}
          {(start.isError || stop.isError) && <Alert>{errorMessage(start.error ?? stop.error)}</Alert>}
        </div>
      )}
    </section>
  )
}
