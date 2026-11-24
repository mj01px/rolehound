import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useState, type FormEvent } from 'react'
import { ApiError, api, errorMessage } from '../../lib/api'
import { Alert } from '../../ui/Alert'
import { Button } from '../../ui/Button'
import { Select } from '../../ui/Select'
import uiStyles from '../../ui/ui.module.css'
import styles from './pitch.module.css'

interface Pitch {
  id: string
  number: number
  language: 'pt' | 'en'
  instruction: string | null
  text: string
  model: string
  dossierVersion: number
  createdAt: string
}

/**
 * "Tell us about yourself" text for one job, written by the person's own AI
 * model from their Dossier only. Every version stays listed.
 */
const languages = [
  { value: 'pt', label: 'Português' },
  { value: 'en', label: 'Inglês' },
] as const

export function PitchPanel({ jobId }: { jobId: string }) {
  const queryClient = useQueryClient()
  const key = ['pitches', jobId]
  const versions = useQuery({ queryKey: key, queryFn: ({ signal }) => api.get<Pitch[]>(`/jobs/${jobId}/pitches`, signal) })
  const [language, setLanguage] = useState<'pt' | 'en'>('pt')
  const [instruction, setInstruction] = useState('')
  const [withInstruction, setWithInstruction] = useState(false)
  const [shown, setShown] = useState(0)
  const generate = useMutation({
    mutationFn: () => api.post<Pitch>(`/jobs/${jobId}/pitches`, { language, instruction }),
    onSuccess: () => {
      setShown(0)
      return queryClient.invalidateQueries({ queryKey: key })
    },
  })
  const error = generate.error instanceof ApiError ? generate.error : null

  const submit = (event: FormEvent) => {
    event.preventDefault()
    generate.mutate()
  }

  const list = versions.data ?? []
  const current = list[shown]

  return (
    <section aria-labelledby="pitch-title" className={styles.panel}>
      <h2 id="pitch-title" className={styles.sectionTitle}>
        Pitch
      </h2>
      <p className={styles.muted}>Um "fale sobre você" para essa vaga, escrito só com o que está no seu Dossier.</p>

      {current && <PitchText pitch={current} />}

      {list.length > 1 && (
        <div className={styles.versions} role="group" aria-label="Versões">
          {list.map((pitch, index) => (
            <button
              key={pitch.id}
              type="button"
              className={index === shown ? `${styles.version} ${styles.versionActive}` : styles.version}
              onClick={() => setShown(index)}
              aria-pressed={index === shown}
            >
              v{pitch.number}
            </button>
          ))}
        </div>
      )}

      <form className={styles.form} onSubmit={submit} noValidate>
        <div className={styles.row}>
          <Select variant="compact" label="Idioma" value={language} options={languages} onChange={setLanguage} />
          <Button type="submit" variant="secondary" disabled={generate.isPending}>
            {generate.isPending ? 'Escrevendo...' : list.length ? 'Gerar outra versão' : 'Gerar pitch'}
          </Button>
          {!withInstruction && (
            <button type="button" className={styles.addInstruction} onClick={() => setWithInstruction(true)}>
              Adicionar instrução
            </button>
          )}
        </div>
        {withInstruction && (
          <input
            className={styles.select}
            aria-label="Instrução (opcional)"
            value={instruction}
            onChange={(event) => setInstruction(event.target.value)}
            maxLength={500}
            placeholder="Ex.: mais curto, foque em dados"
          />
        )}
        {error && <Alert>{error.field('instruction') ?? errorMessage(error)}</Alert>}
      </form>
    </section>
  )
}

function PitchText({ pitch }: { pitch: Pitch }) {
  const [copied, setCopied] = useState(false)

  const copy = async () => {
    try {
      await navigator.clipboard.writeText(pitch.text)
      setCopied(true)
      setTimeout(() => setCopied(false), 2000)
    } catch {
      setCopied(false)
    }
  }

  return (
    <article className={styles.pitch}>
      <p className={styles.text}>{pitch.text}</p>
      <footer className={styles.meta}>
        <span>
          v{pitch.number} · {pitch.language === 'pt' ? 'Português' : 'Inglês'} · {pitch.model} · Dossier v{pitch.dossierVersion}
          {pitch.instruction && ` · "${pitch.instruction}"`}
        </span>
        <button type="button" className={uiStyles.link} onClick={() => void copy()}>
          {copied ? 'Copiado' : 'Copiar'}
        </button>
      </footer>
    </article>
  )
}
