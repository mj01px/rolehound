import type { ReactNode } from 'react'
import { Link } from 'react-router'
import { Logo } from '../../ui/Logo'
import styles from './auth.module.css'

interface AuthLayoutProps {
  title: string
  subtitle?: ReactNode
  children: ReactNode
}

const CONTACT_EMAIL = 'rolehound.app@gmail.com'
const YEAR = new Date().getFullYear()

/** One centered column, the mark above a large title, and a footer with the brand, data guarantees and help. One screen per step. */
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
        <div className={styles.footerGrid}>
          <div className={styles.footerBrand}>
            <div className={styles.footerMark}>
              <Logo size={20} label="" />
              <span>rolehound</span>
            </div>
            <p>Vagas do Brasil ranqueadas pelo seu perfil, com as candidaturas num funil só.</p>
            <span className={styles.footerBadge}>Acesso só por convite</span>
          </div>

          <div className={styles.footerColumn}>
            <h2>Seus dados</h2>
            <ul>
              <li>
                <ShieldIcon />
                Verificação em duas etapas
              </li>
              <li>
                <LockIcon />
                Chaves de IA guardadas criptografadas
              </li>
              <li>
                <DownloadIcon />
                Exporte ou apague tudo quando quiser
              </li>
            </ul>
          </div>

          <nav className={styles.footerColumn} aria-label="Ajuda e documentos">
            <h2>Ajuda</h2>
            <Link to="/ajuda">Perguntas frequentes</Link>
            <Link to="/esqueci-senha">Esqueci a senha</Link>
            <Link to="/termos">Termos de uso</Link>
            <Link to="/privacidade">Política de privacidade</Link>
            <a href={`mailto:${CONTACT_EMAIL}`}>{CONTACT_EMAIL}</a>
          </nav>
        </div>

        <div className={styles.footerBottom}>
          <span>© {YEAR} rolehound</span>
          <span>Dados tratados conforme a LGPD</span>
        </div>
      </footer>
    </div>
  )
}

function Icon({ children }: { children: ReactNode }) {
  return (
    <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.5" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
      {children}
    </svg>
  )
}

function ShieldIcon() {
  return (
    <Icon>
      <path d="M12 3l7 3v5c0 4.5-3 8.5-7 10-4-1.5-7-5.5-7-10V6z" />
      <path d="M9 12l2 2 4-4" />
    </Icon>
  )
}

function LockIcon() {
  return (
    <Icon>
      <rect x="5" y="11" width="14" height="10" rx="2" />
      <path d="M8 11V7a4 4 0 0 1 8 0v4" />
    </Icon>
  )
}

function DownloadIcon() {
  return (
    <Icon>
      <path d="M12 4v11" />
      <path d="M7 10l5 5 5-5" />
      <path d="M5 20h14" />
    </Icon>
  )
}
