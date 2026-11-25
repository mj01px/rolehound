import type { ReactNode } from 'react'
import { Link } from 'react-router'
import { Logo } from '../../ui/Logo'
import styles from './legal.module.css'

export const CONTROLLER = 'Mauro Junior'
export const CONTACT = 'mjuniordevv@gmail.com'

/** Public reading page for the terms and the privacy policy. */
export function LegalLayout({ title, children }: { title: string; children: ReactNode }) {
  return (
    <div className={styles.page}>
      <div className={styles.column}>
        <Link to="/" className={styles.brand}>
          <Logo size={20} label="" />
          rolehound
        </Link>
        <header>
          <h1 className={styles.title}>{title}</h1>
          <p className={styles.version}>Versão de 25 de novembro de 2026</p>
        </header>
        <div className={styles.body}>{children}</div>
        <footer className={styles.footer}>
          <Link to="/termos">Termos de uso</Link>
          <Link to="/privacidade">Política de privacidade</Link>
          <span>Contato: {CONTACT}</span>
        </footer>
      </div>
    </div>
  )
}
