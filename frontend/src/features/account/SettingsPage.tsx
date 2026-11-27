import { useQuery } from '@tanstack/react-query'
import { useState } from 'react'
import { Link } from 'react-router'
import { api } from '../../lib/api'
import { AiSection, DossierSection, TelegramSection, UsageSection } from './SettingsSections'
import { usageQuery } from './usage'
import styles from './account.module.css'

interface Spending {
  hasKey: boolean
  /** Null when OpenRouter did not answer. */
  spending: { usageMonthly: number; limit: number | null; limitRemaining: number | null; limitReset: string | null } | null
  hasManagementKey: boolean
  /** The account's credit balance in USD; only with a management key OpenRouter accepts. */
  balance: number | null
}

const usd = new Intl.NumberFormat('pt-BR', { style: 'currency', currency: 'USD' })

/** Configurações: the person's AI, Dossier, score profile and alerts. The account itself is on Perfil. */
export function SettingsPage() {
  return (
    <div className={styles.page}>
      <div className={styles.heading}>
        <h1 className={styles.title}>Configurações</h1>
        <p className={styles.lead}>
          Sua IA, seu Dossier, seu perfil de score e os avisos. Conta e segurança ficam no <Link to="/perfil">seu perfil</Link>.
        </p>
      </div>
      <Summary />
      <AiSection />
      <DossierSection />
      <UsageSection />
      <TelegramSection />
    </div>
  )
}

/** What the person's key spent and may still spend, straight from OpenRouter, and how much AI they used. */
function Summary() {
  // Fresh each time the page or the tab is opened: the numbers move as the person uses AI.
  const spending = useQuery({ queryKey: ['ai', 'spending'], queryFn: ({ signal }) => api.get<Spending>('/ai/spending', signal), staleTime: 0, refetchOnWindowFocus: true })
  const usage = useQuery(usageQuery)
  const data = spending.data
  const key = data?.spending
  // OpenRouter counts the month in UTC.
  const [month] = useState(() => new Date().toLocaleDateString('pt-BR', { month: 'long', timeZone: 'UTC' }))
  const calls = usage.data?.last30Days.reduce((sum, row) => sum + row.calls, 0)
  const missing = spending.isError
    ? 'Não deu para ler agora. Tente de novo em instantes'
    : data && !data.hasKey
      ? 'Cadastre sua chave do OpenRouter abaixo'
      : data && !key
        ? 'O OpenRouter não respondeu agora'
        : null

  return (
    <section aria-label="Resumo da IA" className={styles.summary}>
      <div className={styles.summaryCell}>
        <span className={styles.summaryLabel}>Gasto em {month}</span>
        <span className={styles.summaryValue}>{key ? usd.format(key.usageMonthly) : '-'}</span>
        <span className={styles.summaryHint}>{missing ?? 'Nesta chave, pelo OpenRouter'}</span>
      </div>
      <div className={styles.summaryCell}>
        <span className={styles.summaryLabel}>Saldo da conta</span>
        <span className={styles.summaryValue}>{data?.balance != null ? usd.format(data.balance) : '-'}</span>
        <span className={styles.summaryHint}>
          {spending.isError
            ? 'Não deu para ler agora. Tente de novo em instantes'
            : data?.balance != null
              ? 'Crédito na sua conta do OpenRouter'
              : data?.hasManagementKey
                ? 'O OpenRouter não respondeu ou recusou a chave de gerenciamento'
                : 'Cadastre a chave de gerenciamento abaixo para ver'}
        </span>
      </div>
      <div className={styles.summaryCell}>
        <span className={styles.summaryLabel}>Uso nos últimos 30 dias</span>
        <span className={styles.summaryValue}>
          {calls ?? '-'} <span className={styles.summaryUnit}>{calls === 1 ? 'pedido' : 'pedidos'}</span>
        </span>
        <span className={styles.summaryHint}>
          <a href="#uso">Ver por função</a>
        </span>
      </div>
    </section>
  )
}
