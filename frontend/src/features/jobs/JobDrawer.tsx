import { useCallback, useEffect, useRef, useState, type FormEvent } from 'react'
import { Link, useLocation, useNavigate, useParams } from 'react-router'
import { errorMessage } from '../../lib/api'
import { Alert } from '../../ui/Alert'
import { Button } from '../../ui/Button'
import { MoreMenu } from '../../ui/MoreMenu'
import { useFollow } from '../companies/api'
import { useEnterFunnel } from '../funnel/api'
import { AtsPanel } from '../ats/AtsPanel'
import { PitchPanel } from '../pitch/PitchPanel'
import { ResumePanel } from '../resume/ResumePanel'
import { useArchive, useJob, useRestore } from './api'
import { ago, archiveReasonLabel, archiveReasons, seniorityLabels, sourceLabels, workModeLabels, type ArchiveReason } from './labels'
import styles from './jobs.module.css'

type Tool = 'pitch' | 'ats' | 'resume'

const tools: { value: Tool; label: string }[] = [
  { value: 'pitch', label: 'Pitch' },
  { value: 'ats', label: 'ATS' },
  { value: 'resume', label: 'Currículo' },
]

/**
 * A job's detail in a side panel over the list (FR-TRI-05). The address is still
 * /vagas/:id, so links work and the browser's back button closes it.
 */
export function JobDrawer() {
  const { id = '' } = useParams()
  const { search } = useLocation()
  const navigate = useNavigate()
  const heading = useRef<HTMLHeadingElement>(null)
  // The job whose panel is sliding out; the address changes once the slide ends.
  const [closingId, setClosingId] = useState<string | null>(null)
  const closing = closingId === id
  const leave = useCallback(() => navigate({ pathname: '/', search }), [navigate, search])
  const close = useCallback(() => {
    if (window.matchMedia('(prefers-reduced-motion: reduce)').matches) leave()
    else setClosingId(id)
  }, [id, leave])

  // Escape closes the panel, unless a dialog inside it is open and takes the key.
  useEffect(() => {
    const onKey = (event: KeyboardEvent) => {
      if (event.key === 'Escape' && !document.querySelector('dialog[open]')) close()
    }
    document.addEventListener('keydown', onKey)
    return () => document.removeEventListener('keydown', onKey)
  }, [close])

  useEffect(() => {
    heading.current?.focus()
  }, [id])

  return (
    <aside
      className={closing ? `${styles.drawer} ${styles.drawerClosing}` : styles.drawer}
      aria-labelledby="job-title"
      onAnimationEnd={(event) => {
        if (closing && event.target === event.currentTarget) leave()
      }}
    >
      <div className={styles.drawerBar}>
        <h2 id="job-title" ref={heading} tabIndex={-1} className={styles.drawerLabel}>
          <span className={styles.srOnly}>Vaga</span>
          <DrawerSource id={id} />
        </h2>
        <button type="button" className={styles.drawerClose} onClick={close} aria-label="Fechar">
          <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" aria-hidden="true">
            <path d="M6 6l12 12M18 6 6 18" />
          </svg>
        </button>
      </div>
      <div className={styles.drawerBody}>
        <JobDetail key={id} id={id} />
      </div>
    </aside>
  )
}

/** "Gupy · hoje" in the panel's top bar. */
function DrawerSource({ id }: { id: string }) {
  const job = useJob(id).data?.job
  if (!job) return null
  return (
    <span aria-hidden="true">
      {sourceLabels[job.source] ?? job.source} · {ago(job.publishedAt ?? job.firstSeenAt)}
    </span>
  )
}

const LONG_DESCRIPTION = 420

/** "Stack principal e Nível". */
const listed = (items: string[]) => (items.length < 2 ? items.join('') : `${items.slice(0, -1).join(', ')} e ${items[items.length - 1]}`)

function JobDetail({ id }: { id: string }) {
  const detail = useJob(id)
  const restore = useRestore(id)
  const enter = useEnterFunnel(id)
  const navigate = useNavigate()
  const [archiving, setArchiving] = useState(false)
  const [tool, setTool] = useState<Tool>('pitch')
  const [expanded, setExpanded] = useState(false)

  if (detail.isError) {
    return <Alert>{errorMessage(detail.error)}</Alert>
  }

  if (!detail.data) {
    return <p className={styles.muted}>Carregando…</p>
  }

  const { job, description, url, matchedGroups, archivedAt, archiveNote, addedByUser, applicationId, company } = detail.data
  const meta = [
    job.company,
    job.location,
    job.workMode !== 'unknown' ? workModeLabels[job.workMode] : null,
    job.seniority !== 'unknown' ? seniorityLabels[job.seniority] : null,
    addedByUser ? 'Adicionada por você' : null,
  ]
    .filter(Boolean)
    .join(' · ')
  const long = description.length > LONG_DESCRIPTION

  return (
    <article className={styles.detail}>
      <header className={styles.detailHeader}>
        <div className={styles.scoreLine}>
          <span className={job.score < 0 ? `${styles.points} ${styles.pointsNegative}` : styles.points} title="Pontos do seu perfil">
            {job.score > 0 ? `+${job.score}` : job.score}
          </span>
          {job.isHighlight && (
            <span className={styles.marker}>
              <span className={styles.markerDot} aria-hidden="true" />
              Destaque
            </span>
          )}
          {job.isNew && <span className={styles.markerQuiet}>Nova</span>}
        </div>
        <h3 className={styles.drawerTitle}>{job.title}</h3>
        {meta && <p className={styles.rowMeta}>{meta}</p>}
      </header>

      <div className={styles.actions}>
        {applicationId ? (
          <Link to={`/funil/${applicationId}`} className={styles.primaryLink}>
            Ver no funil
          </Link>
        ) : (
          <Button onClick={() => enter.mutate('applied', { onSuccess: (card) => navigate(`/funil/${card.id}`) })} disabled={enter.isPending}>
            Mover para o funil
          </Button>
        )}
        <a className={styles.secondaryLink} href={url} target="_blank" rel="noopener noreferrer">
          Vaga original
          <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.5" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
            <path d="M7 17 17 7M8 7h9v9" />
          </svg>
        </a>
        <span className={styles.actionsSpacer} />
        {!applicationId &&
          (archivedAt ? (
            <Button variant="ghost" onClick={() => restore.mutate()} disabled={restore.isPending}>
              Restaurar
            </Button>
          ) : (
            <Button variant="ghost" onClick={() => setArchiving(true)}>
              Arquivar
            </Button>
          ))}
        {company && <FollowCompanyMenu company={company} />}
      </div>
      {restore.isError && <Alert>{errorMessage(restore.error)}</Alert>}
      {enter.isError && <Alert>{errorMessage(enter.error)}</Alert>}

      {(archivedAt || job.closedAtSource) && (
        <div className={styles.statusLine}>
          {archivedAt && (
            <p>
              Arquivada por você: {archiveReasonLabel(job.archiveReason)}
              {archiveNote && <span className={styles.dim}> · {archiveNote}</span>}
            </p>
          )}
          {job.closedAtSource && <p>A fonte não lista mais essa vaga.</p>}
        </div>
      )}

      <section aria-labelledby="why-title" className={styles.drawerSection}>
        <h4 id="why-title" className={styles.drawerHeading}>
          Por que esse score
        </h4>
        {matchedGroups.length === 0 ? (
          <p className={styles.rowMeta}>Nenhum grupo do seu perfil apareceu na vaga.</p>
        ) : (
          <>
            {job.tags.length > 0 && (
              <div className={styles.terms}>
                {job.tags.map((tag) => (
                  <span key={tag} className={styles.term}>
                    {tag}
                  </span>
                ))}
              </div>
            )}
            <p className={styles.rowMeta}>Bateu em {listed(matchedGroups)}.</p>
          </>
        )}
      </section>

      <section aria-labelledby="description-title" className={styles.drawerSection}>
        <h4 id="description-title" className={styles.drawerHeading}>
          Descrição
        </h4>
        {description ? (
          <>
            <div className={long && !expanded ? `${styles.description} ${styles.descriptionClamped}` : styles.description}>{description}</div>
            {long && (
              <button type="button" className={styles.textButton} onClick={() => setExpanded((value) => !value)} aria-expanded={expanded}>
                {expanded ? 'Mostrar menos' : 'Mostrar tudo'}
              </button>
            )}
          </>
        ) : (
          <p className={styles.rowMeta}>A fonte ainda não mandou a descrição dessa vaga.</p>
        )}
      </section>

      <section aria-labelledby="tools-title" className={styles.drawerSection}>
        <div className={styles.toolsHead}>
          <h4 id="tools-title" className={styles.drawerHeading}>
            Com IA
          </h4>
          <div role="tablist" aria-label="Ferramentas de IA" className={styles.toolTabs}>
            {tools.map((option) => (
              <button
                key={option.value}
                type="button"
                role="tab"
                id={`tool-${option.value}`}
                aria-selected={tool === option.value}
                aria-controls="tool-panel"
                className={tool === option.value ? `${styles.toolTab} ${styles.toolTabActive}` : styles.toolTab}
                onClick={() => setTool(option.value)}
              >
                {option.label}
              </button>
            ))}
          </div>
        </div>
        <div role="tabpanel" id="tool-panel" aria-labelledby={`tool-${tool}`} className={styles.toolPanel}>
          {tool === 'pitch' && <PitchPanel jobId={id} />}
          {tool === 'ats' && <AtsPanel jobId={id} />}
          {tool === 'resume' && <ResumePanel jobId={id} />}
        </div>
      </section>

      {archiving && <ArchiveDialog id={id} onClose={() => setArchiving(false)} />}
    </article>
  )
}

function ArchiveDialog({ id, onClose }: { id: string; onClose: () => void }) {
  const dialog = useRef<HTMLDialogElement>(null)
  const archive = useArchive(id)
  const [reason, setReason] = useState<ArchiveReason | ''>('')
  const [note, setNote] = useState('')

  useEffect(() => {
    dialog.current?.showModal()
  }, [])

  const submit = (event: FormEvent) => {
    event.preventDefault()
    if (!reason) return
    archive.mutate({ reason, note }, { onSuccess: onClose })
  }

  return (
    <dialog ref={dialog} className={styles.dialog} onCancel={onClose} onClose={onClose} aria-labelledby="archive-title">
      <form className={styles.dialogForm} onSubmit={submit} noValidate>
        <div className={styles.dialogHead}>
          <h2 id="archive-title" className={styles.dialogTitle}>
            Arquivar vaga
          </h2>
          <p className={styles.muted}>Só some da sua lista. O motivo ajuda a medir a qualidade do seu score.</p>
        </div>
        <fieldset className={styles.reasons}>
          <legend className={styles.label}>Motivo</legend>
          {archiveReasons.map((option) => (
            <label key={option.value} className={reason === option.value ? `${styles.reason} ${styles.reasonActive}` : styles.reason}>
              <input type="radio" name="reason" value={option.value} checked={reason === option.value} onChange={() => setReason(option.value)} />
              {option.label}
            </label>
          ))}
        </fieldset>
        <label className={styles.textareaField}>
          <span className={styles.label}>Observação (opcional)</span>
          <textarea className={styles.textarea} value={note} onChange={(event) => setNote(event.target.value)} maxLength={500} rows={3} />
          <span className={styles.help}>{note.length}/500</span>
        </label>
        {archive.isError && <Alert>{errorMessage(archive.error)}</Alert>}
        <div className={styles.dialogActions}>
          <Button variant="ghost" onClick={onClose}>
            Cancelar
          </Button>
          <Button type="submit" disabled={!reason || archive.isPending}>
            Arquivar
          </Button>
        </div>
      </form>
    </dialog>
  )
}

/** The ⋯ next to the job actions: follow (or stop following) the company behind it. */
function FollowCompanyMenu({ company }: { company: { targetId: string; name: string; following: boolean } }) {
  const follow = useFollow(company.targetId)
  return (
    <MoreMenu
      label="Mais ações"
      disabled={follow.isPending}
      items={[
        {
          key: 'follow',
          label: company.following ? `Parar de avisar vagas da ${company.name}` : `Avisar vagas novas da ${company.name}`,
          run: () => follow.mutate(!company.following),
        },
      ]}
    />
  )
}
