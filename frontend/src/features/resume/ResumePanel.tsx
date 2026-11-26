import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import { api, apiUrl, errorMessage } from '../../lib/api'
import { Alert } from '../../ui/Alert'
import { Button } from '../../ui/Button'
import { Select } from '../../ui/Select'
import styles from './resume.module.css'

interface Change {
  section: string
  label: string
  original: string
  rewritten: string
  /** Numbers, links and emails in the rewording that the Dossier never mentions. */
  notInDossier?: string[] | null
}

interface Cut {
  section: string
  item: string
}

interface Generation {
  id: string
  jobId: string
  language: 'pt' | 'en'
  status: 'queued' | 'running' | 'done' | 'failed'
  error: string | null
  model: string | null
  dossierVersion: number
  createdAt: string
  finishedAt: string | null
  changes: Change[] | null
  cuts: Cut[] | null
}

const statusLabel = { queued: 'Na fila', running: 'Gerando', done: 'Pronto', failed: 'Falhou' } as const
const languageLabel = { pt: 'Português', en: 'Inglês' } as const

/**
 * Tailored resume for one job. Generation runs in a queue; the person reviews
 * every reworded sentence and every cut before downloading the PDF.
 */
const languages = [
  { value: 'pt', label: 'Português' },
  { value: 'en', label: 'Inglês' },
] as const

export function ResumePanel({ jobId }: { jobId: string }) {
  const queryClient = useQueryClient()
  const listKey = ['resumes', jobId]
  const list = useQuery({
    queryKey: listKey,
    queryFn: ({ signal }) => api.get<Generation[]>(`/jobs/${jobId}/resumes`, signal),
    // Follow the queue while something is waiting or running.
    refetchInterval: (query) => (query.state.data?.some((g) => g.status === 'queued' || g.status === 'running') ? 2000 : false),
  })
  const [language, setLanguage] = useState<'pt' | 'en'>('pt')
  const [selected, setSelected] = useState<string | null>(null)
  const generate = useMutation({
    mutationFn: () => api.post<Generation>(`/jobs/${jobId}/resumes`, { language }),
    onSuccess: (generation) => {
      setSelected(generation.id)
      return queryClient.invalidateQueries({ queryKey: listKey })
    },
  })

  const generations = list.data ?? []
  const busy = generations.some((g) => g.status === 'queued' || g.status === 'running')
  const current = generations.find((g) => g.id === selected) ?? generations[0]

  return (
    <section aria-labelledby="resume-title" className={styles.panel}>
      <h2 id="resume-title" className={styles.sectionTitle}>
        Currículo adaptado
      </h2>
      <p className={styles.muted}>
        Feito do seu Dossier para essa vaga. Empresas, cargos, datas e formação são copiados do Dossier; a IA só escolhe o que entra e reescreve frases.
      </p>

      <div className={styles.controls}>
        <Select variant="compact" label="Idioma do currículo" value={language} options={languages} onChange={setLanguage} />
        <Button variant="secondary" onClick={() => generate.mutate()} disabled={generate.isPending || busy}>
          {busy ? 'Gerando...' : 'Gerar currículo'}
        </Button>
      </div>
      {generate.isError && <Alert>{errorMessage(generate.error)}</Alert>}

      {generations.length > 1 && (
        <div className={styles.versions} role="group" aria-label="Gerações">
          {generations.map((g) => (
            <button
              key={g.id}
              type="button"
              className={g.id === current?.id ? `${styles.version} ${styles.versionActive}` : styles.version}
              aria-pressed={g.id === current?.id}
              onClick={() => setSelected(g.id)}
            >
              {new Date(g.createdAt).toLocaleString('pt-BR', { dateStyle: 'short', timeStyle: 'short' })} · {languageLabel[g.language]} · {statusLabel[g.status]}
            </button>
          ))}
        </div>
      )}

      {current && <GenerationView key={`${current.id}-${current.status}`} generation={current} />}
    </section>
  )
}

function GenerationView({ generation }: { generation: Generation }) {
  const detail = useQuery({
    queryKey: ['resume', generation.id, generation.status],
    queryFn: ({ signal }) => api.get<Generation>(`/resumes/${generation.id}`, signal),
    enabled: generation.status === 'done',
  })
  const [reviewed, setReviewed] = useState(false)

  if (generation.status === 'queued' || generation.status === 'running') {
    return <p className={styles.state}>{statusLabel[generation.status]}... isso leva de alguns segundos a um minuto.</p>
  }

  if (generation.status === 'failed') {
    return <Alert>{generation.error ?? 'A geração falhou. Tente de novo.'}</Alert>
  }

  const data = detail.data
  if (!data) return detail.isError ? <Alert>{errorMessage(detail.error)}</Alert> : null
  const changes = data.changes ?? []
  const cuts = data.cuts ?? []

  return (
    <div className={styles.review}>
      <div>
        <h3 className={styles.listTitle}>O que foi reescrito ({changes.length})</h3>
        {changes.length === 0 ? (
          <p className={styles.muted}>Nada foi reescrito.</p>
        ) : (
          <ul className={styles.changes}>
            {changes.map((change, index) => (
              <li key={`${change.label}-${index}`} className={styles.change}>
                <span className={styles.changeLabel}>
                  {change.section} · {change.label}
                </span>
                <span className={styles.diff}>
                  <span className={styles.before}>
                    <span className={styles.diffTag}>Dossier</span>
                    {change.original || '(vazio)'}
                  </span>
                  <span className={styles.after}>
                    <span className={styles.diffTag}>Currículo</span>
                    {change.rewritten}
                  </span>
                </span>
                {change.notInDossier && change.notInDossier.length > 0 && (
                  <p className={styles.notInDossier} role="note">
                    Confira: {change.notInDossier.join(', ')} não aparece no seu Dossier.
                  </p>
                )}
              </li>
            ))}
          </ul>
        )}
      </div>

      <div>
        <h3 className={styles.listTitle}>O que ficou de fora ({cuts.length})</h3>
        {cuts.length === 0 ? (
          <p className={styles.muted}>Nada ficou de fora.</p>
        ) : (
          <ul className={styles.cuts}>
            {cuts.map((cut, index) => (
              <li key={`${cut.item}-${index}`}>
                <span className={styles.dim}>{cut.section}:</span> {cut.item}
              </li>
            ))}
          </ul>
        )}
      </div>

      <label className={styles.check}>
        <input type="checkbox" checked={reviewed} onChange={(event) => setReviewed(event.target.checked)} />
        Revisei as mudanças e o que ficou de fora
      </label>
      <div className={styles.download}>
        {reviewed ? (
          <a className={styles.downloadLink} href={apiUrl(`/resumes/${generation.id}/pdf`)} download>
            Baixar PDF
          </a>
        ) : (
          <span className={`${styles.downloadLink} ${styles.downloadOff}`} aria-disabled="true">
            Baixar PDF
          </span>
        )}
        <span className={styles.dim}>
          {data.model} · Dossier v{data.dossierVersion}
        </span>
      </div>
    </div>
  )
}
