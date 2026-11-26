import type { ReactNode } from 'react'
import { Modal } from '../../ui/Modal'
import styles from './account.module.css'

/** A setup step reused from onboarding (key, models, Dossier, profile) shown in a modal; the step keeps its own title and fields. */
export function StepModal({ label, wide = false, onClose, children }: { label: string; wide?: boolean; onClose: () => void; children: ReactNode }) {
  return (
    <Modal label={label} wide={wide} onClose={onClose}>
      <div className={styles.stepModal}>
        <button type="button" className={styles.stepModalClose} aria-label="Fechar" onClick={onClose}>
          <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.5" strokeLinecap="round" aria-hidden="true">
            <path d="M6 6l12 12M18 6 6 18" />
          </svg>
        </button>
        {children}
      </div>
    </Modal>
  )
}
