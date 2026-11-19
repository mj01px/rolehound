import { useMutation } from '@tanstack/react-query'
import { useState, type ChangeEvent } from 'react'
import { api, errorMessage } from '../../lib/api'
import { Alert, Notice } from '../../ui/Alert'
import { Button } from '../../ui/Button'
import uiStyles from '../../ui/ui.module.css'
import type { DossierError } from './api'
import styles from './onboarding.module.css'

const MAX_BYTES = 1_000_000

interface Check {
  valid: boolean
  errors: DossierError[]
}

interface Summary {
  name: string
  headline: string
  experience: number
  education: number
  skills: number
  projects: number
}

function summarize(content: string): Summary {
  const doc = JSON.parse(content) as Record<string, unknown> & {
    person?: { name?: string; headline?: { pt?: string } }
  }
  const count = (key: string) => (Array.isArray(doc[key]) ? (doc[key] as unknown[]).length : 0)
  return {
    name: doc.person?.name ?? '',
    headline: doc.person?.headline?.pt ?? '',
    experience: count('experience'),
    education: count('education'),
    skills: count('skills'),
    projects: count('projects'),
  }
}

/** Prompt, paste or upload, validation with AI fix, then a review before saving (FR-DOS-01 to 05). */
export function DossierStep({ onDone, onBack }: { onDone: () => Promise<void>; onBack?: (() => void) | undefined }) {
  const [content, setContent] = useState('')
  const [errors, setErrors] = useState<DossierError[]>([])
  const [review, setReview] = useState<Summary | null>(null)
  const [fixed, setFixed] = useState(false)
  const [copied, setCopied] = useState(false)
  const [fileError, setFileError] = useState<string | null>(null)

  const copyPrompt = useMutation({
    mutationFn: async () => {
      const { prompt } = await api.get<{ prompt: string }>('/dossier/prompt')
      await navigator.clipboard.writeText(prompt)
    },
    onSuccess: () => setCopied(true),
  })

  const check = useMutation({
    mutationFn: () => api.post<Check>('/dossier/check', { content }),
    onSuccess: (result) => {
      setErrors(result.errors)
      setReview(result.valid ? summarize(content) : null)
    },
  })

  const fix = useMutation({
    mutationFn: () => api.post<Check & { content: string }>('/dossier/fix', { content }),
    onSuccess: (result) => {
      setContent(result.content)
      setErrors(result.errors)
      setFixed(true)
      setReview(result.valid ? summarize(result.content) : null)
    },
  })

  const save = useMutation({ mutationFn: () => api.post('/dossier', { content }), onSuccess: onDone })

  const readFile = (event: ChangeEvent<HTMLInputElement>) => {
    const file = event.target.files?.[0]
    setFileError(null)
    if (!file) return
    if (file.size > MAX_BYTES) {
      setFileError('O arquivo passa de 1 MB.')
      return
    }
    file.text().then((text) => {
      setContent(text)
      setErrors([])
      setReview(null)
    })
  }

  const edit = (value: string) => {
    setContent(value)
    setErrors([])
    setReview(null)
    setFixed(false)
  }

  if (review) {
    return (
      <div className={styles.column}>
        <div className={styles.titles}>
          <h1 className={styles.title}>Confira seu Dossier</h1>
          <p className={styles.lead}>Está tudo certo com o formato. Veja se o conteúdo é você antes de salvar.</p>
        </div>
        {fixed && <Notice>A IA corrigiu o formato. Confira se nada foi trocado: ela não deve inventar nem remover informação.</Notice>}
        <div className={styles.summary}>
          <div className={styles.summaryRow}><span>Nome</span><span>{review.name}</span></div>
          <div className={styles.summaryRow}><span>Título</span><span>{review.headline}</span></div>
          <div className={styles.summaryRow}><span>Experiências</span><span className={styles.mono}>{review.experience}</span></div>
          <div className={styles.summaryRow}><span>Formação</span><span className={styles.mono}>{review.education}</span></div>
          <div className={styles.summaryRow}><span>Projetos</span><span className={styles.mono}>{review.projects}</span></div>
          <div className={styles.summaryRow}><span>Habilidades</span><span className={styles.mono}>{review.skills}</span></div>
        </div>
        {save.isError && <Alert>{errorMessage(save.error)}</Alert>}
        <div className={styles.actions}>
          <Button size="large" block onClick={() => save.mutate()} disabled={save.isPending}>
            Salvar Dossier
          </Button>
          <button type="button" className={`${uiStyles.link} ${styles.center}`} onClick={() => setReview(null)}>
            Editar o JSON
          </button>
        </div>
      </div>
    )
  }

  return (
    <div className={`${styles.column} ${styles.wide}`}>
      <div className={styles.titles}>
        <h1 className={styles.title}>Monte seu Dossier</h1>
        <p className={styles.lead}>É tudo o que o rolehound sabe sobre você. A IA só usa o que estiver aqui, e o currículo nunca cita algo fora dele.</p>
      </div>
      <ol className={styles.steps}>
        <li className={styles.stepItem}>
          <span className={styles.number}>1</span>
          <div className={styles.stepBody}>
            <div>
              <div className={styles.stepTitle}>Copie o prompt</div>
              <p className={styles.hint}>Ele faz uma IA externa entrevistar você e devolver um JSON.</p>
            </div>
            <div className={styles.row}>
              <Button variant="secondary" onClick={() => copyPrompt.mutate()} disabled={copyPrompt.isPending}>
                {copied ? 'Prompt copiado' : 'Copiar prompt'}
              </Button>
            </div>
          </div>
        </li>
        <li className={styles.stepItem}>
          <span className={styles.number}>2</span>
          <div className={styles.stepBody}>
            <div className={styles.stepTitle}>Responda a entrevista</div>
            <p className={styles.hint}>Use a IA que preferir. Se quiser, envie seu currículo atual para ela ler.</p>
          </div>
        </li>
        <li className={styles.stepItem}>
          <span className={styles.number}>3</span>
          <div className={styles.stepBody}>
            <div>
              <div className={styles.stepTitle}>Cole o JSON aqui</div>
              <p className={styles.hint}>Ou envie o arquivo .json, até 1 MB.</p>
            </div>
            <label>
              <span className={uiStyles.label} style={{ position: 'absolute', width: 1, height: 1, overflow: 'hidden', clip: 'rect(0 0 0 0)' }}>
                JSON do Dossier
              </span>
              <textarea className={styles.textarea} value={content} onChange={(e) => edit(e.target.value)} placeholder='{ "schemaVersion": 1, … }' spellCheck={false} />
            </label>
            <div className={styles.row}>
              <label className={`${uiStyles.button} ${uiStyles.secondary}`}>
                Enviar arquivo
                <input type="file" accept="application/json,.json" onChange={readFile} hidden />
              </label>
            </div>
            {fileError && <Alert>{fileError}</Alert>}
          </div>
        </li>
      </ol>

      {errors.length > 0 && (
        <>
          <ul className={styles.errors} aria-label="Problemas no Dossier">
            {errors.map((e) => (
              <li key={`${e.path}-${e.message}`}>
                <code>{e.path}</code> {e.message}
              </li>
            ))}
          </ul>
          <Button variant="secondary" onClick={() => fix.mutate()} disabled={fix.isPending}>
            {fix.isPending ? 'Corrigindo…' : 'Corrigir com IA'}
          </Button>
        </>
      )}
      {(check.isError || fix.isError) && <Alert>{errorMessage(check.error ?? fix.error)}</Alert>}

      <div className={styles.actions}>
        <Button size="large" block onClick={() => check.mutate()} disabled={check.isPending || !content.trim()}>
          Validar e continuar
        </Button>
        {onBack && (
          <button type="button" className={`${uiStyles.link} ${styles.center}`} onClick={onBack}>
            Voltar
          </button>
        )}
      </div>
    </div>
  )
}
