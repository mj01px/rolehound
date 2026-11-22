import { useEffect, useRef, useState, type FormEvent } from 'react'
import { useNavigate } from 'react-router'
import { ApiError, errorMessage } from '../../lib/api'
import { Alert, Notice } from '../../ui/Alert'
import { Button } from '../../ui/Button'
import { Field } from '../../ui/Field'
import { useAddLink } from './api'
import styles from './jobs.module.css'

/**
 * Add a job by link. Links from Greenhouse, Lever, GitHub and InHire are read
 * through their connector; any other link asks for the pasted job text and
 * is kept only as a reference.
 */
export function AddLinkDialog({ onClose }: { onClose: () => void }) {
  const dialog = useRef<HTMLDialogElement>(null)
  const navigate = useNavigate()
  const add = useAddLink()
  const [url, setUrl] = useState('')
  const [needsText, setNeedsText] = useState(false)
  const [title, setTitle] = useState('')
  const [company, setCompany] = useState('')
  const [location, setLocation] = useState('')
  const [description, setDescription] = useState('')

  useEffect(() => {
    dialog.current?.showModal()
  }, [])

  const error = add.error instanceof ApiError ? add.error : null
  const codeIs = (code: string) => error?.code === code

  const submit = (event: FormEvent) => {
    event.preventDefault()
    add.mutate(needsText ? { url, title, company, location, description } : { url }, {
      onSuccess: (result) => {
        onClose()
        navigate(`/vagas/${result.id}`)
      },
      onError: (failure) => {
        if (failure instanceof ApiError && failure.code === 'link_needs_text') setNeedsText(true)
      },
    })
  }

  return (
    <dialog ref={dialog} className={`${styles.dialog} ${styles.dialogWide}`} onCancel={onClose} onClose={onClose} aria-labelledby="add-link-title">
      <form className={styles.dialogForm} onSubmit={submit} noValidate>
        <div className={styles.dialogHead}>
          <h2 id="add-link-title" className={styles.dialogTitle}>
            Adicionar vaga por link
          </h2>
          <p className={styles.muted}>Links do Greenhouse, Lever, GitHub e InHire são lidos direto da fonte. Para os outros, você cola o texto da vaga.</p>
        </div>

        <Field
          label="Link da vaga"
          type="url"
          inputMode="url"
          placeholder="https://"
          value={url}
          onChange={(event) => {
            setUrl(event.target.value)
            setNeedsText(false)
          }}
          error={error?.field('url')}
          autoFocus
          required
        />

        {needsText && (
          <>
            <Notice>Esse site não tem conector. O link fica guardado só como referência; nada é buscado nele.</Notice>
            <Field label="Cargo" value={title} onChange={(event) => setTitle(event.target.value)} error={error?.field('title')} maxLength={500} required />
            <div className={styles.twoCols}>
              <Field label="Empresa" value={company} onChange={(event) => setCompany(event.target.value)} error={error?.field('company')} maxLength={200} />
              <Field label="Local" value={location} onChange={(event) => setLocation(event.target.value)} error={error?.field('location')} maxLength={300} />
            </div>
            <label className={styles.textareaField}>
              <span className={styles.label}>Texto da vaga</span>
              <textarea
                className={styles.textarea}
                value={description}
                onChange={(event) => setDescription(event.target.value)}
                maxLength={30000}
                rows={8}
                required
                aria-invalid={error?.field('description') && !codeIs('link_needs_text') ? true : undefined}
              />
              {error?.field('description') && !codeIs('link_needs_text') && <span className={styles.fieldError}>{error.field('description')}</span>}
            </label>
          </>
        )}

        {error && !codeIs('link_needs_text') && !error.problem.errors && <Alert>{errorMessage(error)}</Alert>}

        <div className={styles.dialogActions}>
          <Button variant="ghost" onClick={onClose}>
            Cancelar
          </Button>
          <Button type="submit" disabled={add.isPending || !url.trim()}>
            {add.isPending ? 'Adicionando...' : 'Adicionar'}
          </Button>
        </div>
      </form>
    </dialog>
  )
}
