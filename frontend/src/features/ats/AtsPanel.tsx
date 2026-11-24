import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useRef, type ChangeEvent } from 'react'
import { ApiError, api, errorMessage } from '../../lib/api'
import { Alert } from '../../ui/Alert'
import { Button } from '../../ui/Button'
import uiStyles from '../../ui/ui.module.css'
import styles from './ats.module.css'

interface Resume {
  fileName: string
  pages: number
  characters: number
  uploadedAt: string
}

interface AtsReport {
  score: number
  verdict: 'strong' | 'moderate' | 'weak'
  summary: string
  matchedKeywords: string[]
  missingKeywords: string[]
  strengths: string[]
  gaps: string[]
  suggestions: string[]
  resumeFileName: string
  model: string
  createdAt: string
}

const verdictLabel = { strong: 'Forte', moderate: 'Moderado', weak: 'Fraco' } as const

/** null when the API answers 404: "nothing yet" is a normal state here. */
async function orNull<T>(load: Promise<T>): Promise<T | null> {
  try {
    return await load
  } catch (error) {
    if (error instanceof ApiError && error.status === 404) return null
    throw error
  }
}

export function AtsPanel({ jobId }: { jobId: string }) {
  const queryClient = useQueryClient()
  const resume = useQuery({ queryKey: ['resume'], queryFn: ({ signal }) => orNull(api.get<Resume>('/resume', signal)) })
  const report = useQuery({ queryKey: ['ats', jobId], queryFn: ({ signal }) => orNull(api.get<AtsReport>(`/jobs/${jobId}/ats`, signal)) })
  const analyze = useMutation({
    mutationFn: () => api.post<AtsReport>(`/jobs/${jobId}/ats`),
    onSuccess: (data) => queryClient.setQueryData(['ats', jobId], data),
  })

  return (
    <section aria-labelledby="ats-title" className={styles.panel}>
      <h2 id="ats-title" className={styles.sectionTitle}>
        Análise ATS
      </h2>
      <p className={styles.muted}>Seu currículo em PDF contra essa vaga, com a régua de um recrutador exigente. Não usa o Dossier.</p>

      <ResumeBox resume={resume.data ?? null} loading={resume.isPending} />

      {report.data && <Report report={report.data} />}

      {analyze.isError && <Alert>{errorMessage(analyze.error)}</Alert>}
      {resume.data && (
        <div>
          <Button variant="secondary" onClick={() => analyze.mutate()} disabled={analyze.isPending}>
            {analyze.isPending ? 'Analisando...' : report.data ? 'Analisar de novo' : 'Analisar currículo'}
          </Button>
        </div>
      )}
    </section>
  )
}

function ResumeBox({ resume, loading }: { resume: Resume | null; loading: boolean }) {
  const queryClient = useQueryClient()
  const input = useRef<HTMLInputElement>(null)
  const upload = useMutation({
    mutationFn: (file: File) => {
      const form = new FormData()
      form.append('file', file)
      return api.upload<Resume>('/resume', form)
    },
    onSuccess: (data) => queryClient.setQueryData(['resume'], data),
  })
  const remove = useMutation({
    mutationFn: () => api.delete('/resume'),
    onSuccess: () => queryClient.setQueryData(['resume'], null),
  })
  const error = upload.error instanceof ApiError ? upload.error : null

  const choose = (event: ChangeEvent<HTMLInputElement>) => {
    const file = event.target.files?.[0]
    event.target.value = ''
    if (file) upload.mutate(file)
  }

  if (loading) return null

  return (
    <div className={styles.resume}>
      <input ref={input} type="file" accept="application/pdf,.pdf" className={styles.hiddenInput} onChange={choose} aria-label="Currículo em PDF" />
      {resume ? (
        <p className={styles.resumeLine}>
          <span>
            Currículo: <strong>{resume.fileName}</strong>
            <span className={styles.dim}>
              {' '}
              · {resume.pages} {resume.pages === 1 ? 'página' : 'páginas'}
            </span>
          </span>
          <span className={styles.resumeActions}>
            <button type="button" className={uiStyles.link} onClick={() => input.current?.click()} disabled={upload.isPending}>
              Trocar
            </button>
            <button type="button" className={uiStyles.link} onClick={() => remove.mutate()} disabled={remove.isPending}>
              Remover
            </button>
          </span>
        </p>
      ) : (
        <div className={styles.resumeEmpty}>
          <span className={styles.muted}>PDF com texto, até 5 MB e 10 páginas. Guardamos só o texto, não o arquivo.</span>
          <Button variant="secondary" onClick={() => input.current?.click()} disabled={upload.isPending}>
            {upload.isPending ? 'Enviando...' : 'Enviar currículo'}
          </Button>
        </div>
      )}
      {upload.isError && <Alert>{error?.field('file') ?? errorMessage(upload.error)}</Alert>}
      {remove.isError && <Alert>{errorMessage(remove.error)}</Alert>}
    </div>
  )
}

function Report({ report }: { report: AtsReport }) {
  return (
    <article className={styles.report}>
      <header className={styles.reportHead}>
        <span className={`${styles.score} ${styles[report.verdict]}`}>{report.score}</span>
        <span className={styles.reportTitle}>
          <strong>{verdictLabel[report.verdict]}</strong>
          <span className={styles.summary}>{report.summary}</span>
        </span>
      </header>
      <div className={styles.keywords}>
        <KeywordList title="A vaga pede e o currículo mostra" items={report.matchedKeywords} tone="ok" />
        <KeywordList title="A vaga pede e o currículo não mostra" items={report.missingKeywords} tone="miss" />
      </div>
      <List title="Pontos fortes" items={report.strengths} />
      <List title="Lacunas" items={report.gaps} />
      <List title="Sugestões" items={report.suggestions} />
      <footer className={styles.dim}>
        {report.resumeFileName} · {report.model} · {new Date(report.createdAt).toLocaleString('pt-BR', { dateStyle: 'short', timeStyle: 'short' })}
      </footer>
    </article>
  )
}

function KeywordList({ title, items, tone }: { title: string; items: string[]; tone: 'ok' | 'miss' }) {
  if (items.length === 0) return null
  return (
    <div className={styles.keywordGroup}>
      <h3 className={styles.listTitle}>{title}</h3>
      <div className={styles.tags}>
        {items.map((item) => (
          <span key={item} className={tone === 'ok' ? `${styles.tag} ${styles.tagOk}` : `${styles.tag} ${styles.tagMiss}`}>
            {item}
          </span>
        ))}
      </div>
    </div>
  )
}

function List({ title, items }: { title: string; items: string[] }) {
  if (items.length === 0) return null
  return (
    <div>
      <h3 className={styles.listTitle}>{title}</h3>
      <ul className={styles.list}>
        {items.map((item) => (
          <li key={item}>{item}</li>
        ))}
      </ul>
    </div>
  )
}
