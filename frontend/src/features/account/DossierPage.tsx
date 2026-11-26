import { useQuery } from '@tanstack/react-query'
import { useState } from 'react'
import { Link } from 'react-router'
import { api, errorMessage } from '../../lib/api'
import { Alert } from '../../ui/Alert'
import { Button } from '../../ui/Button'
import { DossierImport } from './DossierImport'
import styles from './account.module.css'

interface Dossier {
  number: number
  createdAt: string
  content: unknown
}

/** The person's current Dossier, as stored: read, copy, download or replace with a new version. */
export function DossierPage() {
  const dossier = useQuery({ queryKey: ['dossier', 'content'], queryFn: ({ signal }) => api.get<Dossier>('/dossier', signal) })
  const [copied, setCopied] = useState(false)
  const [importing, setImporting] = useState(false)
  const json = dossier.data ? JSON.stringify(dossier.data.content, null, 2) : ''

  const copy = async () => {
    await navigator.clipboard.writeText(json)
    setCopied(true)
    setTimeout(() => setCopied(false), 2000)
  }

  const download = () => {
    const url = URL.createObjectURL(new Blob([json], { type: 'application/json' }))
    const link = document.createElement('a')
    link.href = url
    link.download = `dossier-v${dossier.data?.number ?? ''}.json`
    link.click()
    URL.revokeObjectURL(url)
  }

  return (
    <div className={styles.dossierPage}>
      <Link to="/configuracoes" className={styles.back}>
        <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.75" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
          <path d="M15 6l-6 6 6 6" />
        </svg>
        Configurações
      </Link>
      <div className={styles.dossierHead}>
        <div className={styles.heading}>
          <h1 className={styles.title}>Seu Dossier</h1>
          <p className={styles.lead}>
            {dossier.data
              ? `Versão ${dossier.data.number}, importada em ${new Date(dossier.data.createdAt).toLocaleDateString('pt-BR')}. Para mudar algo, importe uma versão nova.`
              : 'Carregando'}
          </p>
        </div>
        {dossier.data && (
          <div className={styles.actions}>
            <Button onClick={() => setImporting(true)}>Importar nova versão</Button>
            <Button variant="secondary" onClick={() => void copy()}>
              {copied ? 'Copiado' : 'Copiar'}
            </Button>
            <Button variant="secondary" onClick={download}>
              Baixar JSON
            </Button>
          </div>
        )}
      </div>
      {dossier.isError && <Alert>{errorMessage(dossier.error)}</Alert>}
      {importing && <DossierImport onClose={() => setImporting(false)} />}
      {dossier.data && (
        <pre className={styles.json} tabIndex={0} aria-label="Conteúdo do Dossier">
          {json}
        </pre>
      )}
    </div>
  )
}
