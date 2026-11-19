import type { ReactNode } from 'react'
import styles from './ui.module.css'

export function Alert({ children }: { children: ReactNode }) {
  return (
    <p role="alert" className={styles.alert}>
      {children}
    </p>
  )
}

export function Notice({ children }: { children: ReactNode }) {
  return (
    <p role="status" className={styles.notice}>
      {children}
    </p>
  )
}
