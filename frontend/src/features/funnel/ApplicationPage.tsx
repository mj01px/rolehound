import { useState, type FormEvent } from 'react'
import { Link, useNavigate, useParams } from 'react-router'
import { ApiError, errorMessage } from '../../lib/api'
import { Alert } from '../../ui/Alert'
import { Button } from '../../ui/Button'
import { Field } from '../../ui/Field'
import uiStyles from '../../ui/ui.module.css'
import {
  formatDay,
  statuses,
  statusInfo,
  todayInBrazil,
  useApplication,
  useDeleteInteraction,
  useLeaveFunnel,
  useSaveApplication,
  useSaveInteraction,
  type ApplicationDetail,
  type ApplicationForm,
  type Interaction,
} from './api'
import styles from './funnel.module.css'

export function ApplicationPage() {
  const { id = '' } = useParams()
  const detail = useApplication(id)

  return (
    <div className={styles.page}>
      <Link to="/funil" className={styles.back}>
        ← Funil
      </Link>
      {detail.isError && <Alert>{errorMessage(detail.error)}</Alert>}
      {/* Keyed by update time so the form starts again from saved data after each save. */}
      {detail.data && <ApplicationView key={detail.data.application.updatedAt} id={id} detail={detail.data} />}
    </div>
  )
}

function ApplicationView({ id, detail }: { id: string; detail: ApplicationDetail }) {
  const { application } = detail
  const info = statusInfo(application.status)

  return (
    <>
      <header className={styles.detailHeader}>
        <h1 className={styles.title}>{application.title}</h1>
        <p className={styles.muted}>
          {application.company && `${application.company} · `}
          <Link to={`/vagas/${application.jobId}`} className={uiStyles.link}>
            Ver a vaga
          </Link>
          {' · '}
          <a href={detail.jobUrl} target="_blank" rel="noopener noreferrer" className={uiStyles.link}>
            Vaga original
          </a>
        </p>
        <div className={styles.cardMeta}>
          <span className={styles.statusTag}>
            <span className={styles.dot} style={{ background: info.color }} />
            {info.label}
          </span>
          {application.isOverdue && <span className={styles.overdueTag}>Próximo passo atrasado</span>}
          {application.jobClosedAtSource && <span className={styles.closedTag}>Encerrada na fonte</span>}
        </div>
      </header>
      <div className={styles.columns2}>
        <ApplicationEditor id={id} detail={detail} />
        <Timeline id={id} interactions={detail.interactions} />
      </div>
    </>
  )
}

function ApplicationEditor({ id, detail }: { id: string; detail: ApplicationDetail }) {
  const navigate = useNavigate()
  const save = useSaveApplication(id)
  const leave = useLeaveFunnel(id)
  const [confirmLeave, setConfirmLeave] = useState(false)
  const [form, setForm] = useState<ApplicationForm>({
    status: detail.application.status,
    priority: detail.application.priority,
    appliedOn: detail.application.appliedOn,
    nextStep: detail.application.nextStep,
    nextStepOn: detail.application.nextStepOn,
    contact: detail.contact,
    hasReferral: detail.hasReferral,
    notes: detail.notes,
  })
  const error = save.error instanceof ApiError ? save.error : null
  const set = <K extends keyof ApplicationForm>(key: K, value: ApplicationForm[K]) => setForm((current) => ({ ...current, [key]: value }))

  const submit = (event: FormEvent) => {
    event.preventDefault()
    save.mutate(form)
  }

  return (
    <section aria-labelledby="application-title" className={styles.panel}>
      <h2 id="application-title" className={styles.sectionTitle}>
        Candidatura
      </h2>
      <form className={styles.form} onSubmit={submit} noValidate>
        <div className={styles.row2}>
          <label className={styles.selectField}>
            <span className={uiStyles.label}>Status</span>
            <select className={uiStyles.input} value={form.status} onChange={(event) => set('status', event.target.value as ApplicationForm['status'])}>
              {statuses.map((status) => (
                <option key={status.value} value={status.value}>
                  {status.label}
                </option>
              ))}
            </select>
          </label>
          <label className={styles.selectField}>
            <span className={uiStyles.label}>Prioridade</span>
            <select className={uiStyles.input} value={form.priority} onChange={(event) => set('priority', Number(event.target.value))}>
              {[1, 2, 3, 4, 5].map((value) => (
                <option key={value} value={value}>
                  {value === 1 ? '1 (mais alta)' : value === 5 ? '5 (mais baixa)' : value}
                </option>
              ))}
            </select>
          </label>
        </div>
        <div className={styles.row2}>
          <Field label="Próximo passo" value={form.nextStep} onChange={(event) => set('nextStep', event.target.value)} maxLength={300} error={error?.field('nextStep')} />
          <Field label="Data do próximo passo" type="date" value={form.nextStepOn ?? ''} onChange={(event) => set('nextStepOn', event.target.value || null)} />
        </div>
        <div className={styles.row2}>
          <Field label="Contato" value={form.contact} onChange={(event) => set('contact', event.target.value)} maxLength={200} error={error?.field('contact')} />
          <Field label="Aplicada em" type="date" value={form.appliedOn ?? ''} onChange={(event) => set('appliedOn', event.target.value || null)} />
        </div>
        <label className={styles.check}>
          <input type="checkbox" checked={form.hasReferral} onChange={(event) => set('hasReferral', event.target.checked)} />
          Tenho indicação
        </label>
        <label className={styles.textareaField}>
          <span className={uiStyles.label}>Notas</span>
          <textarea className={styles.textarea} value={form.notes} onChange={(event) => set('notes', event.target.value)} maxLength={5000} rows={5} />
        </label>
        {save.isError && !error?.problem.errors && <Alert>{errorMessage(save.error)}</Alert>}
        <div className={styles.formActions}>
          {confirmLeave ? (
            <Button variant="danger" onClick={() => leave.mutate(undefined, { onSuccess: () => navigate('/funil') })} disabled={leave.isPending}>
              Confirmar: tirar do funil e apagar o histórico
            </Button>
          ) : (
            <Button variant="ghost" onClick={() => setConfirmLeave(true)}>
              Tirar do funil
            </Button>
          )}
          <Button type="submit" disabled={save.isPending}>
            Salvar
          </Button>
        </div>
        {leave.isError && <Alert>{errorMessage(leave.error)}</Alert>}
      </form>
    </section>
  )
}

function Timeline({ id, interactions }: { id: string; interactions: Interaction[] }) {
  const [editing, setEditing] = useState<Interaction | 'new' | null>(null)

  return (
    <section aria-labelledby="timeline-title" className={styles.panel}>
      <div className={styles.panelHead}>
        <h2 id="timeline-title" className={styles.sectionTitle}>
          Histórico
        </h2>
        {editing === null && (
          <Button variant="secondary" onClick={() => setEditing('new')}>
            Registrar
          </Button>
        )}
      </div>
      {editing === 'new' && <InteractionEditor applicationId={id} onDone={() => setEditing(null)} />}
      <ol className={styles.timeline}>
        {interactions.map((interaction) =>
          editing !== 'new' && editing?.id === interaction.id ? (
            <li key={interaction.id}>
              <InteractionEditor applicationId={id} interaction={interaction} onDone={() => setEditing(null)} />
            </li>
          ) : (
            <li key={interaction.id} className={interaction.isSystem ? `${styles.entry} ${styles.entrySystem}` : styles.entry}>
              <span className={styles.entryDate}>{formatDay(interaction.date)}</span>
              <span className={styles.entryBody}>
                <span className={styles.entryTitle}>{interaction.title}</span>
                {interaction.detail && <span className={styles.entryDetail}>{interaction.detail}</span>}
              </span>
              {!interaction.isSystem && editing === null && (
                <button type="button" className={uiStyles.link} onClick={() => setEditing(interaction)}>
                  Editar
                </button>
              )}
            </li>
          ),
        )}
      </ol>
    </section>
  )
}

function InteractionEditor({ applicationId, interaction, onDone }: { applicationId: string; interaction?: Interaction; onDone: () => void }) {
  const save = useSaveInteraction(applicationId)
  const remove = useDeleteInteraction(applicationId)
  const [date, setDate] = useState(interaction?.date ?? todayInBrazil())
  const [title, setTitle] = useState(interaction?.title ?? '')
  const [detail, setDetail] = useState(interaction?.detail ?? '')
  const error = save.error instanceof ApiError ? save.error : null

  const submit = (event: FormEvent) => {
    event.preventDefault()
    save.mutate({ ...(interaction ? { id: interaction.id } : {}), form: { date, title, detail } }, { onSuccess: onDone })
  }

  return (
    <form className={`${styles.form} ${styles.inlineForm}`} onSubmit={submit} noValidate>
      <div className={styles.row2}>
        <Field label="O que aconteceu" value={title} onChange={(event) => setTitle(event.target.value)} maxLength={200} error={error?.field('title')} autoFocus />
        <Field label="Data" type="date" value={date} onChange={(event) => setDate(event.target.value)} error={error?.field('date')} />
      </div>
      <label className={styles.textareaField}>
        <span className={uiStyles.label}>Detalhe (opcional)</span>
        <textarea className={styles.textarea} value={detail} onChange={(event) => setDetail(event.target.value)} maxLength={5000} rows={3} />
      </label>
      {(save.isError && !error?.problem.errors) || remove.isError ? <Alert>{errorMessage(save.error ?? remove.error)}</Alert> : null}
      <div className={styles.formActions}>
        {interaction && (
          <Button variant="ghost" onClick={() => remove.mutate(interaction.id, { onSuccess: onDone })} disabled={remove.isPending}>
            Apagar
          </Button>
        )}
        <Button variant="ghost" onClick={onDone}>
          Cancelar
        </Button>
        <Button type="submit" disabled={save.isPending || !title.trim()}>
          Salvar
        </Button>
      </div>
    </form>
  )
}
