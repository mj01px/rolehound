import { useId, type InputHTMLAttributes, type ReactNode } from 'react'
import styles from './ui.module.css'

interface FieldProps extends InputHTMLAttributes<HTMLInputElement> {
  label: string
  /** Shown next to the label, for a link such as "Esqueci a senha". */
  aside?: ReactNode
  help?: string
  error?: string | undefined
}

export function Field({ label, aside, help, error, className, ...input }: FieldProps) {
  const id = useId()
  const hintId = `${id}-hint`
  const hint = error ?? help

  return (
    <div className={styles.field}>
      <div className={styles.labelRow}>
        <label htmlFor={id} className={styles.label}>
          {label}
        </label>
        {aside}
      </div>
      <input
        id={id}
        className={[styles.input, error && styles.inputError, className].filter(Boolean).join(' ')}
        aria-invalid={error ? true : undefined}
        aria-describedby={hint ? hintId : undefined}
        {...input}
      />
      {hint && (
        <span id={hintId} className={error ? styles.error : styles.help}>
          {hint}
        </span>
      )}
    </div>
  )
}
