import { useEffect, useState } from 'react'
import { Link, Outlet, useMatch, useParams } from 'react-router'
import { errorMessage } from '../../lib/api'
import { Alert } from '../../ui/Alert'
import { Pager } from '../../ui/Pager'
import { ago, workModeLabels } from '../jobs/labels'
import jobStyles from '../jobs/jobs.module.css'
import { sourceNames, useCompany, useCompanyCities, useCompanyJobs, useFollow, type Company, type CompanyJob } from './api'
import { CityPicker } from './CityPicker'
import styles from './companies.module.css'

/** A company's page: every open job it lists, not only the ones that match the person. */
export function CompanyPage() {
  const { companyId = '' } = useParams()
  const company = useCompany(companyId)
  const [query, setQuery] = useState('')
  const [search, setSearch] = useState('')
  const [city, setCity] = useState('')
  const [page, setPage] = useState(1)
  const cities = useCompanyCities(companyId)
  const jobs = useCompanyJobs(companyId, search, city, page)
  const openId = useMatch('/empresas/:companyId/vagas/:id')?.params.id

  // Ask the server a moment after typing stops; a new search starts on its first page.
  useEffect(() => {
    const timer = setTimeout(() => {
      setSearch(query.trim())
      setPage(1)
    }, 250)
    return () => clearTimeout(timer)
  }, [query])

  const data = jobs.data
  const pages = data ? Math.max(1, Math.ceil(data.total / data.perPage)) : 1

  return (
    <div className={styles.page}>
      <Link to="/empresas" className={styles.back}>
        <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.75" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
          <path d="M15 6l-6 6 6 6" />
        </svg>
        Empresas
      </Link>

      {company.isError && <Alert>{errorMessage(company.error)}</Alert>}
      {company.data && <Heading company={company.data} />}

      <div className={styles.filters}>
        <label className={styles.search}>
          <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.5" strokeLinecap="round" aria-hidden="true">
            <circle cx="11" cy="11" r="7" />
            <path d="m20 20-3.5-3.5" />
          </svg>
          <input type="search" value={query} onChange={(event) => setQuery(event.target.value)} placeholder="Buscar vaga" aria-label="Buscar vaga" />
        </label>
        <CityPicker
          cities={cities.data ?? []}
          value={city}
          onChange={(picked) => {
            setCity(picked)
            setPage(1)
          }}
        />
      </div>

      {jobs.isError && <Alert>{errorMessage(jobs.error)}</Alert>}
      {data && data.total === 0 && <p className={styles.emptyNote}>{search || city ? 'Nenhuma vaga com esses filtros.' : 'Nenhuma vaga aberta agora.'}</p>}
      {data && data.total > 0 && (
        <>
          <ul className={jobStyles.list} aria-busy={jobs.isFetching}>
            {data.items.map((job) => (
              <JobLine key={job.id} job={job} companyId={companyId} open={job.id === openId} panelOpen={openId !== undefined} />
            ))}
          </ul>
          <Pager page={page - 1} pages={pages} onChange={(index) => setPage(index + 1)} label="Páginas de vagas da empresa" />
        </>
      )}
      <Outlet />
    </div>
  )
}

function Heading({ company }: { company: Company }) {
  const follow = useFollow(company.id)
  const meta = [
    sourceNames[company.source] ?? company.source,
    company.openJobs === 1 ? '1 vaga aberta' : `${company.openJobs} vagas abertas`,
    company.forYou === 1 ? '1 para você' : `${company.forYou} para você`,
  ]

  return (
    <header className={styles.companyHead}>
      <div className={styles.heading}>
        <h1 className={styles.title}>{company.name}</h1>
        <p className={styles.lead}>
          {meta.join(' · ')}
          {company.paused && ' · coleta pausada'}
        </p>
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
    </header>
  )
}

function JobLine({ job, companyId, open, panelOpen }: { job: CompanyJob; companyId: string; open: boolean; panelOpen: boolean }) {
  const facts = [job.workMode !== 'unknown' ? workModeLabels[job.workMode] : null, job.score === null ? 'Fora do seu perfil' : null, job.inFunnel ? 'No seu funil' : null].filter(
    Boolean,
  )

  return (
    <li>
      <Link
        to={`/empresas/${companyId}/vagas/${job.id}`}
        replace={panelOpen}
        aria-current={open ? 'true' : undefined}
        className={open ? `${jobStyles.row} ${jobStyles.rowOpen}` : jobStyles.row}
      >
        <span className={job.highlight ? `${jobStyles.rowScore} ${jobStyles.rowScoreHigh}` : jobStyles.rowScore} title={job.score === null ? 'Fora do seu perfil' : 'Score'}>
          {job.score ?? '-'}
        </span>
        <span className={jobStyles.rowMain}>
          <span className={jobStyles.rowTitle}>{job.title}</span>
          {job.location && <span className={jobStyles.rowMeta}>{job.location}</span>}
          {facts.length > 0 && <span className={jobStyles.rowFacts}>{facts.join(' · ')}</span>}
        </span>
        <span className={jobStyles.rowWhen}>{ago(job.openedAt)}</span>
      </Link>
    </li>
  )
}
