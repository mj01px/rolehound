import { useMemo, useState, type FormEvent } from 'react'
import { ApiError, errorMessage } from '../../lib/api'
import { Alert, Notice } from '../../ui/Alert'
import { Button } from '../../ui/Button'
import { followMessage, sourceNames, useCompanies, useFollow, useFollowLink, type Company } from './api'
import styles from './companies.module.css'

const normalize = (text: string) => text.normalize('NFD').replace(/\p{Diacritic}/gu, '').toLowerCase()

/** Empresas: search the companies Rolehound reads, follow them, or add one by link. */
export function CompaniesPage() {
  const companies = useCompanies()
  const [query, setQuery] = useState('')
  const [linkOpen, setLinkOpen] = useState(false)

  const all = useMemo(() => companies.data ?? [], [companies.data])
  const words = normalize(query.trim())
  const results = useMemo(() => (words ? all.filter((company) => normalize(`${company.name} ${company.key}`).includes(words)) : all), [all, words])
  const following = all.filter((company) => company.following)
  const showLink = linkOpen || (words.length > 0 && results.length === 0)

  return (
    <div className={styles.page}>
      <header className={styles.heading}>
        <h1 className={styles.title}>Empresas</h1>
        <p className={styles.lead}>Siga uma empresa para saber quando ela abrir vagas que combinam com o seu perfil. Os avisos são só seus.</p>
      </header>

      {companies.isError && <Alert>{errorMessage(companies.error)}</Alert>}

      <label className={styles.search}>
        <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.5" strokeLinecap="round" aria-hidden="true">
          <circle cx="11" cy="11" r="7" />
          <path d="m20 20-3.5-3.5" />
        </svg>
        <input type="search" value={query} onChange={(event) => setQuery(event.target.value)} placeholder="Buscar empresa" aria-label="Buscar empresa" />
      </label>

      {showLink ? (
        <FollowByLink
          intro={words && results.length === 0 ? `Nenhuma empresa com "${query.trim()}" por aqui.` : 'Siga uma empresa que ainda não está na lista.'}
          onClose={linkOpen ? () => setLinkOpen(false) : undefined}
        />
      ) : (
        <button type="button" className={styles.linkToggle} onClick={() => setLinkOpen(true)}>
          Não achou a empresa? Cole o link da página de vagas
        </button>
      )}

      {!words && following.length > 0 && <CompanyList title="Você segue" companies={following} />}
      {companies.data && results.length > 0 && <CompanyList title={words ? 'Resultados' : 'Todas as empresas'} companies={words ? results : all.filter((company) => !company.following)} />}
    </div>
  )
}

function CompanyList({ title, companies }: { title: string; companies: Company[] }) {
  if (companies.length === 0) return null
  return (
    <section className={styles.panel} aria-label={title}>
      <h2 className={styles.panelTitle}>
        {title} <span className={styles.count}>{companies.length}</span>
      </h2>
      <ul className={styles.list}>
        {companies.map((company) => (
          <CompanyRow key={company.id} company={company} />
        ))}
      </ul>
    </section>
  )
}

function CompanyRow({ company }: { company: Company }) {
  const follow = useFollow(company.id)
  const meta = [
    sourceNames[company.source] ?? company.source,
    company.openJobs === 1 ? '1 vaga aberta' : `${company.openJobs} vagas abertas`,
    company.forYou > 0 ? `${company.forYou} para você` : null,
  ].filter(Boolean)

  return (
    <li className={styles.row}>
      <div className={styles.rowText}>
        <span className={styles.name}>{company.name}</span>
        <span className={styles.meta}>
          {meta.join(' · ')}
          {company.paused && <span className={styles.paused}> · coleta pausada</span>}
        </span>
        {follow.isError && <span className={styles.error}>{errorMessage(follow.error)}</span>}
      </div>
      <label className={styles.toggle}>
        <span className={styles.toggleText}>Avisar vagas novas</span>
        <button
          type="button"
          role="switch"
          aria-checked={company.following}
          aria-label={`Avisar vagas novas da ${company.name}`}
          className={company.following ? `${styles.switch} ${styles.switchOn}` : styles.switch}
          onClick={() => follow.mutate(!company.following)}
          disabled={follow.isPending}
        >
          <span className={styles.knob} aria-hidden="true" />
        </button>
      </label>
    </li>
  )
}

function FollowByLink({ intro, onClose }: { intro: string; onClose?: (() => void) | undefined }) {
  const followLink = useFollowLink()
  const [url, setUrl] = useState('')
  const error = followLink.error instanceof ApiError ? followLink.error : null

  const submit = (event: FormEvent) => {
    event.preventDefault()
    followLink.mutate(url.trim(), { onSuccess: () => setUrl('') })
  }

  return (
    <form className={styles.linkCard} onSubmit={submit} noValidate>
      <div className={styles.linkHead}>
        <div className={styles.linkHeading}>
          <span className={styles.linkTitle}>{intro}</span>
          <span className={styles.linkHint}>Cole o link da página de vagas da empresa, ou o de uma vaga dela. O Rolehound lê InHire, Greenhouse, Lever e Workday, e sites de carreira que publicam as vagas para o Google.</span>
        </div>
        {onClose && (
          <button type="button" className={styles.close} aria-label="Fechar" onClick={onClose}>
            <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.5" strokeLinecap="round" aria-hidden="true">
              <path d="M6 6l12 12M18 6 6 18" />
            </svg>
          </button>
        )}
      </div>
      <div className={styles.linkRow}>
        <input
          type="url"
          inputMode="url"
          value={url}
          onChange={(event) => setUrl(event.target.value)}
          placeholder="https://empresa.inhire.app/vagas"
          aria-label="Link da página de vagas"
          aria-invalid={error ? true : undefined}
          className={styles.linkInput}
        />
        <Button type="submit" disabled={followLink.isPending || !url.trim()}>
          {followLink.isPending ? 'Verificando…' : 'Seguir empresa'}
        </Button>
      </div>
      {followLink.isPending && <span className={styles.linkHint}>Conferindo a página de vagas. Pode levar alguns segundos.</span>}
      {error && <Alert>{error.field('url') ?? error.problem.detail ?? errorMessage(error)}</Alert>}
      {followLink.isError && !error && <Alert>{errorMessage(followLink.error)}</Alert>}
      {followLink.data && <Notice>{followMessage(followLink.data)}</Notice>}
    </form>
  )
}
