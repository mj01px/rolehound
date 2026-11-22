import { useEffect, useState } from 'react'
import { Link, useSearchParams } from 'react-router'
import { errorMessage } from '../../lib/api'
import { Alert } from '../../ui/Alert'
import { Button } from '../../ui/Button'
import { AddLinkDialog } from './AddLinkDialog'
import { useJobs, type JobFilters, type JobListItem, type JobView } from './api'
import { ago, archiveReasonLabel, seniorityLabels, sourceLabels, workModeLabels } from './labels'
import styles from './jobs.module.css'

const views: { value: JobView; label: string; count: 'all' | 'highlights' | 'archived' }[] = [
  { value: 'all', label: 'Todas', count: 'all' },
  { value: 'highlights', label: 'Destaques', count: 'highlights' },
  { value: 'archived', label: 'Arquivadas', count: 'archived' },
]

export function JobsPage() {
  const [params, setParams] = useSearchParams()
  const filters: JobFilters = {
    view: (params.get('aba') as JobView | null) ?? 'all',
    q: params.get('busca') ?? '',
    source: params.get('fonte') ?? '',
    workMode: params.get('modelo') ?? '',
    seniority: params.get('nivel') ?? '',
  }
  const [search, setSearch] = useState(filters.q)
  const [adding, setAdding] = useState(false)
  const jobs = useJobs(filters)

  const setParam = (key: string, value: string) => {
    const next = new URLSearchParams(params)
    if (value) next.set(key, value)
    else next.delete(key)
    setParams(next, { replace: true })
  }

  // Search waits for a pause in typing.
  useEffect(() => {
    if (search === filters.q) return
    const timer = setTimeout(() => setParam('busca', search.trim()), 300)
    return () => clearTimeout(timer)
  })

  const pages = jobs.data?.pages ?? []
  const counts = pages[0]?.counts
  const items = pages.flatMap((page) => page.items)
  const filtered = Boolean(filters.q || filters.source || filters.workMode || filters.seniority)

  return (
    <div className={styles.page}>
      <header className={styles.header}>
        <div>
          <h1 className={styles.title}>Vagas</h1>
          {counts && counts.new > 0 && <p className={styles.muted}>{counts.new === 1 ? '1 vaga nova' : `${counts.new} vagas novas`} desde a sua última visita</p>}
        </div>
        <Button variant="secondary" onClick={() => setAdding(true)}>
          Adicionar por link
        </Button>
      </header>

      <nav aria-label="Visões" className={styles.tabs}>
        {views.map((view) => (
          <button
            key={view.value}
            type="button"
            className={filters.view === view.value ? `${styles.tab} ${styles.tabActive}` : styles.tab}
            aria-current={filters.view === view.value ? 'page' : undefined}
            onClick={() => setParam('aba', view.value === 'all' ? '' : view.value)}
          >
            {view.label}
            {counts && <span className={styles.tabCount}>{counts[view.count]}</span>}
          </button>
        ))}
      </nav>

      <div className={styles.filters}>
        <input
          type="search"
          className={`${styles.control} ${styles.search}`}
          placeholder="Buscar por cargo ou empresa"
          aria-label="Buscar por cargo ou empresa"
          value={search}
          onChange={(event) => setSearch(event.target.value)}
          maxLength={100}
        />
        <Select label="Fonte" value={filters.source} options={sourceLabels} onChange={(value) => setParam('fonte', value)} />
        <Select label="Modelo" value={filters.workMode} options={workModeLabels} onChange={(value) => setParam('modelo', value)} />
        <Select label="Nível" value={filters.seniority} options={seniorityLabels} onChange={(value) => setParam('nivel', value)} />
      </div>

      {jobs.isError && <Alert>{errorMessage(jobs.error)}</Alert>}

      {jobs.data && items.length === 0 && (
        <p className={styles.empty}>
          {filtered
            ? 'Nenhuma vaga com esses filtros.'
            : filters.view === 'archived'
              ? 'Nada arquivado ainda.'
              : 'Nenhuma vaga combina com o seu perfil por enquanto. A coleta roda quatro vezes por dia.'}
        </p>
      )}

      <ul className={styles.list} aria-busy={jobs.isFetching}>
        {items.map((job) => (
          <JobRow key={job.id} job={job} />
        ))}
      </ul>

      {jobs.hasNextPage && (
        <Button variant="ghost" onClick={() => void jobs.fetchNextPage()} disabled={jobs.isFetchingNextPage}>
          {jobs.isFetchingNextPage ? 'Carregando...' : `Mostrar mais (${(pages[0]?.total ?? 0) - items.length} restantes)`}
        </Button>
      )}

      {adding && <AddLinkDialog onClose={() => setAdding(false)} />}
    </div>
  )
}

function JobRow({ job }: { job: JobListItem }) {
  const meta = [job.company, job.location].filter(Boolean).join(' · ')
  return (
    <li>
      <Link to={`/vagas/${job.id}`} className={styles.row}>
        <span className={job.isHighlight ? `${styles.score} ${styles.scoreHigh}` : styles.score} title="Score">
          {job.score}
        </span>
        <span className={styles.rowMain}>
          <span className={styles.rowTitle}>
            {job.isNew && <span className={styles.newDot} aria-label="Nova" />}
            {job.title}
          </span>
          {meta && <span className={styles.rowMeta}>{meta}</span>}
          <span className={styles.chips}>
            {job.workMode !== 'unknown' && <span className={styles.chip}>{workModeLabels[job.workMode]}</span>}
            {job.seniority !== 'unknown' && <span className={styles.chip}>{seniorityLabels[job.seniority]}</span>}
            <span className={styles.chip}>{sourceLabels[job.source] ?? job.source}</span>
            {job.archiveReason && <span className={`${styles.chip} ${styles.chipWarn}`}>{archiveReasonLabel(job.archiveReason)}</span>}
            {job.closedAtSource && !job.archiveReason && <span className={`${styles.chip} ${styles.chipWarn}`}>Encerrada na fonte</span>}
          </span>
        </span>
        <span className={styles.rowWhen}>{ago(job.publishedAt ?? job.firstSeenAt)}</span>
      </Link>
    </li>
  )
}

function Select({ label, value, options, onChange }: { label: string; value: string; options: Record<string, string>; onChange: (value: string) => void }) {
  return (
    <select className={styles.control} aria-label={label} value={value} onChange={(event) => onChange(event.target.value)}>
      <option value="">{label}: todos</option>
      {Object.entries(options)
        .filter(([key]) => key !== 'unknown')
        .map(([key, text]) => (
          <option key={key} value={key}>
            {text}
          </option>
        ))}
    </select>
  )
}
