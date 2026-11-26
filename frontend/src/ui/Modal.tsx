import { useEffect, useRef, type ReactNode } from 'react'
import styles from './overlay.module.css'

/**
 * A modal built on the native dialog: Esc and the backdrop close it, focus stays inside.
 * Named by `titleId` (a heading inside), or by `label` when the content has none to point at.
 */
export function Modal({ titleId, label, wide = false, onClose, children }: { titleId?: string; label?: string; wide?: boolean; onClose: () => void; children: ReactNode }) {
  const dialog = useRef<HTMLDialogElement>(null)

  useEffect(() => {
    dialog.current?.showModal()
  }, [])

  return (
    <dialog
      ref={dialog}
      className={wide ? `${styles.dialog} ${styles.dialogWide}` : styles.dialog}
      aria-labelledby={titleId}
      aria-label={titleId ? undefined : label}
      onCancel={onClose}
      onClose={onClose}
      onClick={(event) => {
        if (event.target === dialog.current) onClose()
      }}
    >
      {children}
    </dialog>
  )
}

/** Title, one line of context and the close button at the top of a modal. */
export function ModalHead({ id, title, text, onClose }: { id: string; title: string; text: ReactNode; onClose: () => void }) {
  return (
    <div className={styles.head}>
      <div className={styles.heading}>
        <h2 id={id} className={styles.title}>
          {title}
        </h2>
        <p className={styles.text}>{text}</p>
      </div>
      <button type="button" className={styles.iconButton} aria-label="Fechar" onClick={onClose}>
        <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.5" strokeLinecap="round" aria-hidden="true">
          <path d="M6 6l12 12M18 6 6 18" />
        </svg>
      </button>
    </div>
  )
}
