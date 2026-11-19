import type { ReactNode } from 'react'
import { Logo } from '../../ui/Logo'
import styles from './auth.module.css'

interface AuthLayoutProps {
  title: string
  subtitle?: ReactNode
  children: ReactNode
}

/** One centered column, the mark above a large title, and a thin footer. One screen per step. */
export function AuthLayout({ title, subtitle, children }: AuthLayoutProps) {
  return (
    <div className={styles.page}>
      <main className={styles.main}>
        <div className={styles.column}>
          <div className={styles.head}>
            <Logo size={48} />
            <div className={styles.titles}>
              <h1 className={styles.title}>{title}</h1>
              {subtitle && <p className={styles.subtitle}>{subtitle}</p>}
            </div>
          </div>
          {children}
        </div>
      </main>
      <footer className={styles.footer}>
        <span>rolehound</span>
        <span>Acesso só por convite</span>
      </footer>
    </div>
  )
}
