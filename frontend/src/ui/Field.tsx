import { useId, useState, type InputHTMLAttributes, type ReactNode } from 'react'
import styles from './ui.module.css'

interface FieldProps extends InputHTMLAttributes<HTMLInputElement> {
  label: string
  /** Shown next to the label, for a link such as "Esqueci a senha". */
  aside?: ReactNode
  help?: string
  error?: string | undefined
}

export function Field({ label, aside, help, error, className, type, ...input }: FieldProps) {
  const id = useId()
  const hintId = `${id}-hint`
  const hint = error ?? help
  const isPassword = type === 'password'
  const [visible, setVisible] = useState(false)

  const field = (
    <input
      id={id}
      type={isPassword && visible ? 'text' : type}
      className={[styles.input, isPassword && styles.inputWithToggle, error && styles.inputError, className].filter(Boolean).join(' ')}
      aria-invalid={error ? true : undefined}
      aria-describedby={hint ? hintId : undefined}
      {...input}
    />
  )

  return (
    <div className={styles.field}>
      <div className={styles.labelRow}>
        <label htmlFor={id} className={styles.label}>
          {label}
        </label>
        {aside}
      </div>
      {isPassword ? (
        <div className={styles.inputWrap}>
          {field}
          <button
            type="button"
            className={styles.toggle}
            onClick={() => setVisible((v) => !v)}
            aria-label={visible ? 'Esconder' : 'Mostrar'}
            aria-pressed={visible}
            aria-controls={id}
          >
            {visible ? <EyeOffIcon /> : <EyeIcon />}
          </button>
        </div>
      ) : (
        field
      )}
      {hint && (
        <span id={hintId} className={error ? styles.error : styles.help}>
          {hint}
        </span>
      )}
    </div>
  )
}

function EyeIcon() {
  return (
    <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
      <path d="M2 12s3.5-7 10-7 10 7 10 7-3.5 7-10 7S2 12 2 12Z" />
      <circle cx="12" cy="12" r="3" />
    </svg>
  )
}

function EyeOffIcon() {
  return (
    <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
      <path d="M10.6 5.1A10.6 10.6 0 0 1 12 5c6.5 0 10 7 10 7a17.6 17.6 0 0 1-2.2 3.2" />
      <path d="M6.6 6.6C3.7 8.5 2 12 2 12s3.5 7 10 7a9.7 9.7 0 0 0 5.4-1.6" />
      <path d="M9.9 9.9a3 3 0 0 0 4.2 4.2" />
      <path d="m2 2 20 20" />
    </svg>
  )
}
