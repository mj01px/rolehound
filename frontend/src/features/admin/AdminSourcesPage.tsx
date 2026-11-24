import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useState, type FormEvent } from 'react'
import { ApiError, api, errorMessage } from '../../lib/api'
import { Alert, Notice } from '../../ui/Alert'
import { Button } from '../../ui/Button'
import { Field } from '../../ui/Field'
import { Select } from '../../ui/Select'
import uiStyles from '../../ui/ui.module.css'
import { sourceLabels } from '../jobs/labels'
import styles from './admin.module.css'

interface SourceTarget {
  id: string
  source: string
  key: string
  label: string | null
  enabled: boolean
  lastRunAt: string | null
  lastJobCount: number | null
  lastError: string | null
  activeJobs: number
}

interface CollectionRun {
  id: string
  source: string
  key: string
  startedAt: string
  finishedAt: string
  listed: number
  added: number
  reopened: number
  closed: number
  error: string | null
}

interface CollectionSummary {
  targets: number
  failed: number
  added: number
  reopened: number
  closed: number
}

const sourcesKey = ['admin', 'sources'] as const

const addable = [
  { value: 'inhire', label: 'InHire', placeholder: 'empresa ou empresa/pagina' },
  { value: 'greenhouse', label: 'Greenhouse', placeholder: 'identificador do board' },
  { value: 'lever', label: 'Lever', placeholder: 'identificador do site' },
  { value: 'github', label: 'GitHub', placeholder: 'dono/repositorio' },
] as const

const when = (value: string | null) => (value ? new Date(value).toLocaleString('pt-BR', { dateStyle: 'short', timeStyle: 'short' }) : 'nunca')

export function AdminSourcesPage() {
  const queryClient = useQueryClient()
  const targets = useQuery({ queryKey: [...sourcesKey, 'targets'], queryFn: ({ signal }) => api.get<SourceTarget[]>('/admin/sources', signal) })
  const runs = useQuery({ queryKey: [...sourcesKey, 'runs'], queryFn: ({ signal }) => api.get<CollectionRun[]>('/admin/sources/runs', signal) })
  const run = useMutation({
    mutationFn: () => api.post<CollectionSummary>('/admin/sources/run'),
    onSettled: () => queryClient.invalidateQueries({ queryKey: sourcesKey }),
  })

  const groups = Object.entries(
    (targets.data ?? []).reduce<Record<string, SourceTarget[]>>((acc, target) => {
      ;(acc[target.source] ??= []).push(target)
      return acc
    }, {}),
  )

  return (
    <div className={styles.page}>
      <header className={styles.header}>
        <h1 className={styles.title}>Fontes</h1>
        <p className={styles.muted}>De onde a coleta lê as vagas. Os termos do Gupy vêm dos perfis de score de quem usa o app.</p>
      </header>

      <section aria-labelledby="run-title">
        <h2 id="run-title" className={styles.sectionTitle}>
          Coleta
        </h2>
        <div className={styles.grant}>
          <Button onClick={() => run.mutate()} disabled={run.isPending}>
            {run.isPending ? 'Coletando... pode levar alguns minutos' : 'Coletar agora'}
          </Button>
        </div>
        {run.isError && <Alert>{errorMessage(run.error)}</Alert>}
        {run.data && (
          <Notice>
            {run.data.targets} fontes lidas, {run.data.added} vagas novas, {run.data.reopened} reabertas, {run.data.closed} encerradas
            {run.data.failed > 0 ? `, ${run.data.failed} com erro` : ''}.
          </Notice>
        )}
        <p className={styles.muted}>A coleta automática roda às 9h, 12h, 15h e 18h. A manual pode rodar uma vez a cada 15 minutos.</p>
      </section>

      <AddSourceForm />

      {targets.isError && <Alert>{errorMessage(targets.error)}</Alert>}
      {groups.map(([source, list]) => (
        <section key={source} aria-labelledby={`source-${source}`}>
          <h2 id={`source-${source}`} className={styles.sectionTitle}>
            {sourceLabels[source] ?? source} · {list.length}
          </h2>
          <div className={styles.tableWrap}>
            <table className={styles.table}>
              <thead>
                <tr>
                  <th>{source === 'gupy' ? 'Termo' : 'Fonte'}</th>
                  <th>Vagas abertas</th>
                  <th>Última leitura</th>
                  <th>
                    <span className={styles.srOnly}>Ações</span>
                  </th>
                </tr>
              </thead>
              <tbody>
                {list.map((target) => (
                  <TargetRow key={target.id} target={target} />
                ))}
              </tbody>
            </table>
          </div>
        </section>
      ))}

      <section aria-labelledby="runs-title">
        <h2 id="runs-title" className={styles.sectionTitle}>
          Últimas leituras
        </h2>
        {runs.isError && <Alert>{errorMessage(runs.error)}</Alert>}
        {runs.data?.length === 0 && <p className={styles.muted}>Nenhuma coleta rodou ainda.</p>}
        {runs.data && runs.data.length > 0 && (
          <div className={styles.tableWrap}>
            <table className={styles.table}>
              <thead>
                <tr>
                  <th>Fonte</th>
                  <th>Quando</th>
                  <th>Listadas</th>
                  <th>Novas</th>
                  <th>Reabertas</th>
                  <th>Encerradas</th>
                </tr>
              </thead>
              <tbody>
                {runs.data.map((item) => (
                  <tr key={item.id}>
                    <td>
                      <span className={styles.person}>
                        <span className={styles.name}>{item.key}</span>
                        <span className={styles.dim}>{sourceLabels[item.source] ?? item.source}</span>
                        {item.error && <span className={styles.warn}>{item.error}</span>}
                      </span>
                    </td>
                    <td className={styles.dim}>{when(item.startedAt)}</td>
                    <td className={styles.mono}>{item.listed}</td>
                    <td className={styles.mono}>{item.added}</td>
                    <td className={styles.mono}>{item.reopened}</td>
                    <td className={styles.mono}>{item.closed}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </section>
    </div>
  )
}

function TargetRow({ target }: { target: SourceTarget }) {
  const queryClient = useQueryClient()
  const managed = target.source === 'gupy'
  const toggle = useMutation({
    mutationFn: () => api.put(`/admin/sources/${target.id}`, { enabled: !target.enabled, label: target.label }),
    onSettled: () => queryClient.invalidateQueries({ queryKey: sourcesKey }),
  })
  const remove = useMutation({
    mutationFn: () => api.delete(`/admin/sources/${target.id}`),
    onSettled: () => queryClient.invalidateQueries({ queryKey: sourcesKey }),
  })
  const [confirming, setConfirming] = useState(false)
  const failure = toggle.error ?? remove.error

  return (
    <tr>
      <td>
        <span className={styles.person}>
          <span className={styles.name}>{target.label ?? target.key}</span>
          {target.label && <span className={`${styles.dim} ${styles.mono}`}>{target.key}</span>}
          {!target.enabled && <span className={styles.dim}>{managed ? 'Ninguém usa esse termo agora' : 'Desativada'}</span>}
          {target.lastError && <span className={styles.warn}>{target.lastError}</span>}
          {failure && <span className={styles.error}>{errorMessage(failure)}</span>}
        </span>
      </td>
      <td className={styles.mono}>{target.activeJobs}</td>
      <td className={styles.dim}>{when(target.lastRunAt)}</td>
      <td className={styles.rowActions}>
        {!managed && (
          <>
            <button type="button" className={uiStyles.link} onClick={() => toggle.mutate()} disabled={toggle.isPending}>
              {target.enabled ? 'Desativar' : 'Ativar'}
            </button>
            {confirming ? (
              <button type="button" className={uiStyles.link} onClick={() => remove.mutate()} disabled={remove.isPending}>
                Confirmar remoção
              </button>
            ) : (
              <button type="button" className={uiStyles.link} onClick={() => setConfirming(true)}>
                Remover
              </button>
            )}
          </>
        )}
      </td>
    </tr>
  )
}

function AddSourceForm() {
  const queryClient = useQueryClient()
  const [source, setSource] = useState<(typeof addable)[number]['value']>('inhire')
  const [key, setKey] = useState('')
  const [label, setLabel] = useState('')
  const add = useMutation({
    mutationFn: () => api.post('/admin/sources', { source, key, label }),
    onSuccess: () => {
      setKey('')
      setLabel('')
      void queryClient.invalidateQueries({ queryKey: sourcesKey })
    },
  })
  const error = add.error instanceof ApiError ? add.error : null

  const submit = (event: FormEvent) => {
    event.preventDefault()
    add.mutate()
  }

  return (
    <section aria-labelledby="add-source-title">
      <h2 id="add-source-title" className={styles.sectionTitle}>
        Adicionar fonte
      </h2>
      <form className={styles.grant} onSubmit={submit} noValidate>
        <div className={styles.roleField}>
          <label htmlFor="source-kind" className={uiStyles.label}>
            Tipo
          </label>
          <Select id="source-kind" value={source} options={addable} onChange={setSource} />
        </div>
        <div className={styles.grow2}>
          <Field
            label="Identificador"
            placeholder={addable.find((option) => option.value === source)?.placeholder}
            value={key}
            onChange={(event) => setKey(event.target.value)}
            error={error?.field('key') ?? error?.field('source')}
            maxLength={200}
          />
        </div>
        <div className={styles.grow1}>
          <Field label="Nome da empresa (opcional)" value={label} onChange={(event) => setLabel(event.target.value)} maxLength={120} />
        </div>
        <Button type="submit" disabled={add.isPending || !key.trim()}>
          Adicionar
        </Button>
      </form>
      {error && !error.problem.errors && <Alert>{errorMessage(error)}</Alert>}
    </section>
  )
}
