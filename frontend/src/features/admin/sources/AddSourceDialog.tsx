import { useState, type FormEvent } from 'react'
import { ApiError, errorMessage } from '../../../lib/api'
import { Alert } from '../../../ui/Alert'
import { Button } from '../../../ui/Button'
import { Field } from '../../../ui/Field'
import { Modal, ModalHead } from '../../../ui/Modal'
import { kindLabels, useAddSource, type SourceKind, type SourceTarget } from './api'
import styles from './sources.module.css'

type Addable = Exclude<SourceKind, 'gupy'>

const fields: Record<Addable, { label: string; placeholder: string; help: string }> = {
  inhire: {
    label: 'Empresa no InHire',
    placeholder: 'empresa ou empresa/pagina',
    help: 'É o começo do endereço da página de vagas: em empresa.inhire.app/vagas, é empresa.',
  },
  greenhouse: {
    label: 'Board no Greenhouse',
    placeholder: 'identificador do board',
    help: 'Em job-boards.greenhouse.io/stone, é stone.',
  },
  lever: {
    label: 'Site no Lever',
    placeholder: 'identificador do site',
    help: 'Em jobs.lever.co/empresa, é empresa.',
  },
  github: {
    label: 'Repositório no GitHub',
    placeholder: 'dono/repositorio',
    help: 'Um repositório de vagas em issues, como backend-br/vagas.',
  },
  workday: {
    label: 'Site de vagas na Workday',
    placeholder: 'https://empresa.wd5.myworkdayjobs.com/Site',
    help: 'Cole o link do site de vagas ou de uma vaga. Só entram as vagas no Brasil.',
  },
  careersite: {
    label: 'Site de carreiras',
    placeholder: 'https://carreiras.empresa.com.br',
    help: 'Qualquer site público que publique as vagas para o Google. O Rolehound lê o sitemap e respeita o robots.txt.',
  },
}

const addable: Addable[] = ['inhire', 'greenhouse', 'lever', 'workday', 'careersite', 'github']

export function AddSourceDialog({ initial, onClose, onAdded }: { initial: SourceKind; onClose: () => void; onAdded: (target: SourceTarget) => void }) {
  const add = useAddSource()
  const [kind, setKind] = useState<Addable>(initial === 'gupy' ? 'inhire' : initial)
  const [key, setKey] = useState('')
  const [label, setLabel] = useState('')
  const error = add.error instanceof ApiError ? add.error : null
  const field = fields[kind]

  const submit = (event: FormEvent) => {
    event.preventDefault()
    add.mutate({ source: kind, key, label }, { onSuccess: onAdded })
  }

  return (
    <Modal titleId="add-source-title" onClose={onClose}>
      <form className={styles.dialogForm} onSubmit={submit} noValidate>
        <ModalHead
          id="add-source-title"
          title="Adicionar fonte"
          text="A fonte entra na próxima coleta. Os termos do Gupy não entram aqui: vêm dos perfis de score de quem usa o app."
          onClose={onClose}
        />
        <fieldset className={styles.kinds}>
          <legend className={styles.legend}>Tipo</legend>
          <div className={styles.kindGrid}>
            {addable.map((value) => (
              <label key={value} className={value === kind ? `${styles.kind} ${styles.kindOn}` : styles.kind}>
                <input type="radio" name="kind" value={value} checked={value === kind} onChange={() => setKind(value)} className={styles.kindInput} />
                {kindLabels[value]}
              </label>
            ))}
          </div>
        </fieldset>
        <Field
          label={field.label}
          value={key}
          onChange={(event) => setKey(event.target.value)}
          placeholder={field.placeholder}
          help={field.help}
          error={error?.field('key') ?? error?.field('source')}
          maxLength={200}
          className={styles.monoInput}
          autoFocus
        />
        <Field label="Nome para mostrar (opcional)" value={label} onChange={(event) => setLabel(event.target.value)} placeholder="Nome da empresa" maxLength={120} />
        {error && !error.field('key') && !error.field('source') && <Alert>{errorMessage(error)}</Alert>}
        <div className={styles.dialogActions}>
          <Button variant="secondary" onClick={onClose}>
            Cancelar
          </Button>
          <Button type="submit" disabled={add.isPending || !key.trim()}>
            Adicionar
          </Button>
        </div>
      </form>
    </Modal>
  )
}
