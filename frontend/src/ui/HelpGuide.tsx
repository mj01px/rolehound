import { useId, useState, type ReactNode } from 'react'
import { Modal, ModalHead } from './Modal'
import styles from './HelpGuide.module.css'

export interface GuideStep {
  text: ReactNode
  /** A screenshot under /help, shown below the step. */
  image?: string
  alt?: string
}

export interface Guide {
  title: string
  intro: ReactNode
  steps: GuideStep[]
}

/** A small "?" next to a field that opens a step-by-step guide, with screenshots when there are any. */
export function HelpGuide({ guide, label }: { guide: Guide; label: string }) {
  const [open, setOpen] = useState(false)
  const titleId = useId()

  return (
    <>
      <button type="button" className={styles.trigger} onClick={() => setOpen(true)} aria-label={label} title={label}>
        <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.6" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
          <circle cx="12" cy="12" r="9" />
          <path d="M9.5 9.5a2.5 2.5 0 0 1 4.8.9c0 1.7-2.3 2.1-2.3 3.6M12 17h.01" />
        </svg>
      </button>
      {open && (
        <Modal titleId={titleId} onClose={() => setOpen(false)}>
          <div className={styles.body}>
            <ModalHead id={titleId} title={guide.title} text={guide.intro} onClose={() => setOpen(false)} />
            <ol className={styles.steps}>
              {guide.steps.map((step, index) => (
                <li key={index} className={styles.step}>
                  <span className={styles.number} aria-hidden="true">
                    {index + 1}
                  </span>
                  <div className={styles.stepBody}>
                    <p className={styles.text}>{step.text}</p>
                    {step.image && <img className={styles.image} src={step.image} alt={step.alt ?? ''} loading="lazy" />}
                  </div>
                </li>
              ))}
            </ol>
          </div>
        </Modal>
      )}
    </>
  )
}
