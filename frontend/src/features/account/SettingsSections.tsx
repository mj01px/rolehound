import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useState, type FormEvent } from 'react'
import { useNavigate } from 'react-router'
import { ApiError, api, apiUrl, errorMessage } from '../../lib/api'
import { Alert, Notice } from '../../ui/Alert'
import { Button } from '../../ui/Button'
import { DossierStep } from '../onboarding/DossierStep'
import { KeyStep } from '../onboarding/KeyStep'
import { ModelsStep } from '../onboarding/ModelsStep'
import { ProfileStep } from '../onboarding/ProfileStep'
import type { AiSettings, ProfileDocument, ProfileGroup } from '../onboarding/api'
import { useSensitive } from './sensitive'
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
        <div className={styles.panel}>
          <KeyStep onDone={done} />
          <Button variant="ghost" onClick={() => setEditing(null)}>
            Cancelar
          </Button>
        </div>
      )}
      {editing === 'models' && (
        <div className={styles.panel}>
          <ModelsStep onDone={done} onBack={() => setEditing(null)} />
        </div>
      )}
      {settings.data && editing === null && (
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
              <span className={styles.muted}>
                {settings.data.models
                  ? `Pitch e ATS: ${settings.data.models.pitchAndAts} · Currículo: ${settings.data.models.resume} · Perfil: ${settings.data.models.profileAndFix}`
                  : 'Ainda não escolhidos'}
              </span>
            </div>
            <Button variant="secondary" onClick={() => setEditing('models')}>
              Mudar
            </Button>
          </div>
        </>
      )}
    </section>
  )
}

interface DossierInfo {
  number: number
  createdAt: string
}

/** Re-import the Dossier and, if the person wants, regenerate the score profile from it (FR-DOS-07). */
export function DossierSection() {
  const queryClient = useQueryClient()
  const dossier = useQuery({ queryKey: ['dossier'], queryFn: ({ signal }) => api.get<DossierInfo>('/dossier', signal) })
  const [stage, setStage] = useState<'idle' | 'import' | 'ask' | 'profile'>('idle')

  return (
    <section className={styles.section} aria-labelledby="dossier-title">
      <h2 id="dossier-title" className={styles.sectionTitle}>
        Dossier e perfil de score
      </h2>
      {stage === 'import' && (
        <div className={styles.panel}>
          <DossierStep
            onDone={async () => {
              await queryClient.invalidateQueries({ queryKey: ['dossier'] })
              setStage('ask')
            }}
            onBack={() => setStage('idle')}
          />
        </div>
      )}
      {stage === 'ask' && (
        <div className={styles.panel}>
          <Notice>Dossier salvo como uma nova versão.</Notice>
          <p className={styles.muted}>Quer que a IA proponha um novo perfil de score com base nele? Seu perfil atual só muda se você escolher outro.</p>
          <div className={styles.actions}>
            <Button onClick={() => setStage('profile')}>Gerar novo perfil</Button>
            <Button variant="ghost" onClick={() => setStage('idle')}>
              Manter o atual
            </Button>
          </div>
        </div>
      )}
      {stage === 'profile' && (
        <div className={styles.panel}>
          <ProfileStep
            onDone={async () => {
              await queryClient.invalidateQueries()
              setStage('idle')
            }}
            onBack={() => setStage('idle')}
          />
        </div>
      )}
      {stage === 'idle' && (
        <>
          <div className={styles.row}>
            <div className={styles.rowText}>
              <span className={styles.rowTitle}>Dossier</span>
              <span className={styles.muted}>
                {dossier.data ? `Versão ${dossier.data.number}, de ${new Date(dossier.data.createdAt).toLocaleDateString('pt-BR')}` : 'Carregando'}
              </span>
            </div>
            <Button variant="secondary" onClick={() => setStage('import')}>
              Importar nova versão
            </Button>
          </div>
          <div className={styles.row}>
            <div className={styles.rowText}>
              <span className={styles.rowTitle}>Perfil de score com IA</span>
              <span className={styles.muted}>A IA propõe até três perfis a partir do Dossier atual.</span>
            </div>
            <Button variant="secondary" onClick={() => setStage('profile')}>
              Gerar opções
            </Button>
          </div>
          <ProfileEditor />
        </>
      )}
    </section>
  )
}

interface StoredProfile {
  profile: ProfileDocument
  dossierVersion: number | null
}

const split = (text: string) =>
  text
    .split(',')
    .map((t) => t.trim())
    .filter(Boolean)

/** Hand edits to the score profile (FR-SCO-02). Saving rescores every active job. */
function ProfileEditor() {
  const stored = useQuery({ queryKey: ['score-profile'], queryFn: ({ signal }) => api.get<StoredProfile>('/score-profile', signal) })
  const [open, setOpen] = useState(false)

  return (
    <div className={styles.row} style={{ flexDirection: 'column', alignItems: 'stretch' }}>
      <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', gap: 12 }}>
        <div className={styles.rowText}>
          <span className={styles.rowTitle}>Editar perfil à mão</span>
          <span className={styles.muted}>Grupos de termos, pesos e termos de busca. Salvar recalcula o score de todas as vagas.</span>
        </div>
        <Button variant="secondary" onClick={() => setOpen((value) => !value)} disabled={!stored.data}>
          {open ? 'Fechar' : 'Editar'}
        </Button>
      </div>
      {stored.isError && <Alert>{errorMessage(stored.error)}</Alert>}
      {open && stored.data && <ProfileForm key={JSON.stringify(stored.data.profile)} initial={stored.data} onSaved={() => setOpen(false)} />}
    </div>
  )
}

function ProfileForm({ initial, onSaved }: { initial: StoredProfile; onSaved: () => void }) {
  const queryClient = useQueryClient()
  const [groups, setGroups] = useState(initial.profile.groups.map((g) => ({ ...g, termsText: g.terms.join(', ') })))
  const [minHighlight, setMinHighlight] = useState(initial.profile.minHighlightScore)
  const [searchTerms, setSearchTerms] = useState(initial.profile.searchTerms.join(', '))
  const save = useMutation({
    mutationFn: () => {
      const profile: ProfileDocument = {
        groups: groups.map(({ termsText, ...g }) => ({ ...g, terms: split(termsText) })),
        minHighlightScore: minHighlight,
        yearsBands: initial.profile.yearsBands,
        searchTerms: split(searchTerms),
      }
      return api.put('/score-profile', { profile, dossierVersion: initial.dossierVersion })
    },
    onSuccess: async () => {
      await queryClient.invalidateQueries()
      onSaved()
    },
  })
  const errors = save.error instanceof ApiError ? Object.values(save.error.problem.errors ?? {}).flat() : []

  const update = (index: number, patch: Partial<ProfileGroup & { termsText: string }>) =>
    setGroups((current) => current.map((g, i) => (i === index ? { ...g, ...patch } : g)))

  const submit = (event: FormEvent) => {
    event.preventDefault()
    save.mutate()
  }

  return (
    <form onSubmit={submit} className={styles.profileForm} noValidate>
      {groups.map((group, index) => (
        <fieldset key={index} className={styles.group}>
          <legend className={styles.srOnly}>Grupo {index + 1}</legend>
          <div className={styles.groupHead}>
            <input className={styles.input} aria-label="Nome do grupo" value={group.name} onChange={(e) => update(index, { name: e.target.value })} maxLength={60} />
            <input
              className={`${styles.input} ${styles.weight}`}
              aria-label="Peso"
              type="number"
              value={group.weight}
              onChange={(e) => update(index, { weight: Number(e.target.value) })}
            />
            <button type="button" className={styles.linkButton} onClick={() => setGroups((current) => current.filter((_, i) => i !== index))}>
              Remover
            </button>
          </div>
          <textarea
            className={styles.textarea}
            aria-label="Termos separados por vírgula"
            value={group.termsText}
            onChange={(e) => update(index, { termsText: e.target.value })}
            rows={2}
          />
          <div className={styles.checks}>
            <label>
              <input type="checkbox" checked={group.stack} onChange={(e) => update(index, { stack: e.target.checked })} /> Núcleo (conta para destaque)
            </label>
            <label>
              <input type="checkbox" checked={group.bodyDampened} onChange={(e) => update(index, { bodyDampened: e.target.checked })} /> Vale metade só na descrição
            </label>
          </div>
        </fieldset>
      ))}
      <Button
        variant="ghost"
        onClick={() => setGroups((current) => [...current, { name: 'Novo grupo', weight: 8, terms: [], termsText: '', stack: false, bodyDampened: false }])}
      >
        Adicionar grupo
      </Button>
      <label className={styles.inline}>
        <span className={styles.rowTitle}>Score mínimo para destaque</span>
        <input className={`${styles.input} ${styles.weight}`} type="number" value={minHighlight} onChange={(e) => setMinHighlight(Number(e.target.value))} />
      </label>
      <label className={styles.stack}>
        <span className={styles.rowTitle}>Termos de busca (Gupy), separados por vírgula</span>
        <textarea className={styles.textarea} value={searchTerms} onChange={(e) => setSearchTerms(e.target.value)} rows={3} />
      </label>
      {errors.length > 0 && <Alert>{errors.join(' ')}</Alert>}
      {save.isError && errors.length === 0 && <Alert>{errorMessage(save.error)}</Alert>}
      <div className={styles.actions}>
        <Button type="submit" disabled={save.isPending}>
          {save.isPending ? 'Salvando e recalculando...' : 'Salvar'}
        </Button>
      </div>
    </form>
  )
}

interface UsageRow {
  feature: string
  model: string
  calls: number
  failed: number
  promptTokens: number
  completionTokens: number
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
  const usage = useQuery({
    queryKey: ['account', 'ai-usage'],
    queryFn: ({ signal }) => api.get<{ last30Days: UsageRow[]; allTime: UsageRow[] }>('/account/ai-usage', signal),
  })
  const rows = usage.data?.last30Days ?? []

  return (
    <section className={styles.section} aria-labelledby="usage-title">
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
      <p className={styles.muted}>O custo em dinheiro aparece no painel do OpenRouter, porque a chave é sua.</p>
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
