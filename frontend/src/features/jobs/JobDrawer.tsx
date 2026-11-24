import { useCallback, useEffect, useRef, useState, type FormEvent } from 'react'
import { Link, useLocation, useNavigate, useParams } from 'react-router'
import { errorMessage } from '../../lib/api'
import { Alert } from '../../ui/Alert'
import { Button } from '../../ui/Button'
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
  { value: 'ats', label: 'Análise ATS' },
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
  const close = useCallback(() => navigate({ pathname: '/', search }), [navigate, search])

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
    <aside className={styles.drawer} aria-labelledby="job-title">
      <div className={styles.drawerBar}>
        <h2 id="job-title" ref={heading} tabIndex={-1} className={styles.drawerLabel}>
          Vaga
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

function JobDetail({ id }: { id: string }) {
  const detail = useJob(id)
  const restore = useRestore(id)
  const enter = useEnterFunnel(id)
  const navigate = useNavigate()
  const [archiving, setArchiving] = useState(false)
  const [tool, setTool] = useState<Tool>('pitch')

  if (detail.isError) {
    return <Alert>{errorMessage(detail.error)}</Alert>
  }

  if (!detail.data) {
    return <p className={styles.muted}>Carregando…</p>
  }

  const { job, description, url, matchedGroups, archivedAt, archiveNote, addedByUser, applicationId } = detail.data
  const meta = [job.company, job.location].filter(Boolean).join(' · ')

  return (
    <article className={styles.detail}>
      <header className={styles.detailHeader}>
        <div className={styles.detailTitleRow}>
          <h3 className={styles.title}>{job.title}</h3>
          <span className={job.isHighlight ? `${styles.score} ${styles.scoreHigh} ${styles.scoreLarge}` : `${styles.score} ${styles.scoreLarge}`}>
            {job.score}
          </span>
        </div>
        {meta && <p className={styles.rowMeta}>{meta}</p>}
        <div className={styles.chips}>
          {job.isHighlight && <span className={`${styles.chip} ${styles.chipSignal}`}>Destaque</span>}
          {job.isNew && <span className={`${styles.chip} ${styles.chipSignal}`}>Nova</span>}
          <span className={styles.chip}>{workModeLabels[job.workMode]}</span>
          <span className={styles.chip}>{seniorityLabels[job.seniority]}</span>
          <span className={styles.chip}>{sourceLabels[job.source] ?? job.source}</span>
          {addedByUser && <span className={styles.chip}>Adicionada por você</span>}
          <span className={styles.chipPlain}>{ago(job.publishedAt ?? job.firstSeenAt)}</span>
        </div>
      </header>

      <div className={styles.actions}>
        <a className={styles.external} href={url} target="_blank" rel="noopener noreferrer">
          Abrir vaga original
        </a>
        {applicationId ? (
          <Link to={`/funil/${applicationId}`} className={styles.secondaryLink}>
            Ver no funil
          </Link>
        ) : (
          <Button variant="secondary" onClick={() => enter.mutate('applied', { onSuccess: (card) => navigate(`/funil/${card.id}`) })} disabled={enter.isPending}>
            Mover para o funil
          </Button>
        )}
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
      </div>
      {restore.isError && <Alert>{errorMessage(restore.error)}</Alert>}
      {enter.isError && <Alert>{errorMessage(enter.error)}</Alert>}

      {(archivedAt || job.closedAtSource) && (
        <div className={styles.statusBox}>
          {archivedAt && (
            <p>
              Arquivada por você: <strong>{archiveReasonLabel(job.archiveReason)}</strong>
              {archiveNote && <span className={styles.dim}> · {archiveNote}</span>}
            </p>
          )}
          {job.closedAtSource && <p>A fonte não lista mais essa vaga.</p>}
        </div>
      )}

      <section aria-labelledby="why-title" className={styles.section}>
        <h4 id="why-title" className={styles.sectionTitle}>
          Por que esse score
        </h4>
        {matchedGroups.length === 0 ? (
          <p className={styles.muted}>Nenhum grupo do seu perfil apareceu na vaga.</p>
        ) : (
          <dl className={styles.why}>
            <dt>Grupos</dt>
            <dd>{matchedGroups.join(', ')}</dd>
            <dt>Termos</dt>
            <dd className={styles.terms}>
              {job.tags.map((tag) => (
                <span key={tag} className={styles.term}>
                  {tag}
                </span>
              ))}
            </dd>
          </dl>
        )}
      </section>

      <section aria-label="Ferramentas de IA" className={styles.section}>
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
        <div role="tabpanel" id="tool-panel" aria-labelledby={`tool-${tool}`} className={styles.toolPanel}>
          {tool === 'pitch' && <PitchPanel jobId={id} />}
          {tool === 'ats' && <AtsPanel jobId={id} />}
          {tool === 'resume' && <ResumePanel jobId={id} />}
        </div>
      </section>

      <section aria-labelledby="description-title" className={styles.section}>
        <h4 id="description-title" className={styles.sectionTitle}>
          Descrição
        </h4>
        {description ? <div className={styles.description}>{description}</div> : <p className={styles.muted}>A fonte ainda não mandou a descrição dessa vaga.</p>}
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
