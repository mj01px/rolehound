import { useState } from 'react'
import { errorMessage } from '../../lib/api'
import { Alert } from '../../ui/Alert'
import { Button } from '../../ui/Button'
import { workModeLabels } from '../jobs/labels'
import { monthName, percent, useLabel, useMetrics, useSample, useSkipSample, type Rate, type SampleItem } from './api'
import styles from './accuracy.module.css'

/** Score quality: the monthly sample to label and the numbers that come out of it. */
export function AccuracyPage() {
  const metrics = useMetrics()
  const sample = useSample()
  const [labelling, setLabelling] = useState(false)
  const pending = sample.data?.items.filter((item) => item.status === 'pending') ?? []

  return (
    <div className={styles.page}>
      <header className={styles.header}>
        <h1 className={styles.title}>Qualidade do score</h1>
        <p className={styles.muted}>Mede se os destaques acertam. A precisão vem só da amostra que você rotula; o resto é complemento.</p>
      </header>

      {(metrics.isError || sample.isError) && <Alert>{errorMessage(metrics.error ?? sample.error)}</Alert>}

      {sample.data && (
        <section className={styles.sampleBox} aria-labelledby="sample-title">
          <h2 id="sample-title" className={styles.sectionTitle}>
            Amostra de {monthName(sample.data.month)}
          </h2>
          {sample.data.items.length === 0 ? (
            <p className={styles.muted}>Nenhum destaque novo nos últimos 30 dias para rotular.</p>
          ) : labelling && pending.length > 0 ? (
            <Labeller item={pending[0]!} left={pending.length} onStop={() => setLabelling(false)} />
          ) : pending.length > 0 ? (
            <SampleIntro pending={pending.length} labelled={sample.data.labelled} onStart={() => setLabelling(true)} />
          ) : (
            <p className={styles.muted}>
              {sample.data.skipped > 0 && sample.data.labelled === 0
                ? 'Você pulou a amostra deste mês.'
                : `Pronto: ${sample.data.labelled} vagas rotuladas este mês. A próxima amostra chega no mês que vem.`}
            </p>
          )}
        </section>
      )}

      {metrics.data && (
        <>
          {metrics.data.inFirstThirtyDays && <p className={styles.note}>Você está nos primeiros 30 dias: os números ainda estão se formando.</p>}
          <div className={styles.cards}>
            <Card title="Precisão dos destaques" rate={metrics.data.precision} detail="destaques que eram relevantes, na amostra" />
            <Card title="Modelo de trabalho" rate={metrics.data.workMode} detail="modelos de trabalho certos, na amostra" />
            <Card title="Destaques perdidos" rate={metrics.data.missedHighlights} detail="candidaturas a vagas que não chegaram como destaque" />
          </div>
          <section aria-labelledby="signals-title" className={styles.section}>
            <h2 id="signals-title" className={styles.sectionTitle}>
              Sinais de uso
            </h2>
            <ul className={styles.signals}>
              <li>
                <strong>{metrics.data.signalsRelevant}</strong> destaques foram para o funil
              </li>
              <li>
                <strong>{metrics.data.signalsNotRelevant}</strong> destaques arquivados por falta de interesse ou requisitos
              </li>
              <li>
                <strong>{metrics.data.workModeArchives}</strong> vagas arquivadas pelo modelo de trabalho
              </li>
            </ul>
          </section>
          {metrics.data.months.length > 0 && (
            <section aria-labelledby="months-title" className={styles.section}>
              <h2 id="months-title" className={styles.sectionTitle}>
                Por mês
              </h2>
              <table className={styles.table}>
                <thead>
                  <tr>
                    <th>Mês</th>
                    <th>Precisão</th>
                    <th>Modelo de trabalho</th>
                  </tr>
                </thead>
                <tbody>
                  {metrics.data.months.map((month) => (
                    <tr key={month.month}>
                      <td>{monthName(month.month)}</td>
                      <td>{rateText(month.precision)}</td>
                      <td>{rateText(month.workMode)}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </section>
          )}
        </>
      )}
    </div>
  )
}

const rateText = (rate: Rate) => percent(rate) ?? `${rate.hits} de ${rate.total} (poucos dados)`

function Card({ title, rate, detail }: { title: string; rate: Rate; detail: string }) {
  const value = percent(rate)
  return (
    <div className={styles.card}>
      <span className={styles.cardTitle}>{title}</span>
      <span className={value ? styles.cardValue : `${styles.cardValue} ${styles.cardEmpty}`}>{value ?? 'Poucos dados'}</span>
      <span className={styles.cardDetail}>{rate.total > 0 ? `${rate.hits} de ${rate.total} ${detail}` : `Precisa de 20 para medir: ${detail}`}</span>
    </div>
  )
}

function SampleIntro({ pending, labelled, onStart }: { pending: number; labelled: number; onStart: () => void }) {
  const skip = useSkipSample()
  return (
    <div className={styles.intro}>
      <p className={styles.muted}>
        {labelled > 0 ? `Faltam ${pending} vagas.` : `${pending} destaques sorteados dos últimos 30 dias. Leva uns dois minutos.`} Você pode parar quando quiser.
      </p>
      <div className={styles.actions}>
        <Button onClick={onStart}>{labelled > 0 ? 'Continuar' : 'Começar'}</Button>
        <Button variant="ghost" onClick={() => skip.mutate()} disabled={skip.isPending}>
          Pular este mês
        </Button>
      </div>
      {skip.isError && <Alert>{errorMessage(skip.error)}</Alert>}
    </div>
  )
}

function Labeller({ item, left, onStop }: { item: SampleItem; left: number; onStop: () => void }) {
  const label = useLabel()
  const [relevant, setRelevant] = useState<boolean | null>(null)
  const knownMode = item.shownWorkMode !== 'unknown'

  const send = (workModeRight: boolean | null) => {
    if (relevant === null) return
    label.mutate({ id: item.id, relevant, workModeRight }, { onSuccess: () => setRelevant(null) })
  }

  return (
    <div className={styles.labeller}>
      <p className={styles.left}>Faltam {left}</p>
      <div className={styles.job}>
        <span className={styles.jobTitle}>{item.title}</span>
        <span className={styles.muted}>
          {item.company} · score {item.score} · {workModeLabels[item.shownWorkMode]}
        </span>
        {item.jobId && (
          <a href={`/vagas/${item.jobId}`} target="_blank" rel="noopener noreferrer" className={styles.open}>
            Abrir a vaga em outra aba
          </a>
        )}
      </div>

      <fieldset className={styles.question}>
        <legend>Essa vaga é relevante para você?</legend>
        <div className={styles.actions}>
          <Choice active={relevant === true} onClick={() => setRelevant(true)}>
            Sim
          </Choice>
          <Choice active={relevant === false} onClick={() => setRelevant(false)}>
            Não
          </Choice>
        </div>
      </fieldset>

      {relevant !== null && (
        <fieldset className={styles.question}>
          <legend>{knownMode ? `O modelo "${workModeLabels[item.shownWorkMode]}" está certo?` : 'Seguir para a próxima?'}</legend>
          <div className={styles.actions}>
            {knownMode ? (
              <>
                <Button variant="secondary" onClick={() => send(true)} disabled={label.isPending}>
                  Certo
                </Button>
                <Button variant="secondary" onClick={() => send(false)} disabled={label.isPending}>
                  Errado
                </Button>
                <Button variant="ghost" onClick={() => send(null)} disabled={label.isPending}>
                  Não sei
                </Button>
              </>
            ) : (
              <Button onClick={() => send(null)} disabled={label.isPending}>
                Próxima
              </Button>
            )}
          </div>
        </fieldset>
      )}

      {label.isError && <Alert>{errorMessage(label.error)}</Alert>}
      <Button variant="ghost" onClick={onStop}>
        Parar por agora
      </Button>
    </div>
  )
}

function Choice({ active, onClick, children }: { active: boolean; onClick: () => void; children: string }) {
  return (
    <button type="button" className={active ? `${styles.choice} ${styles.choiceActive}` : styles.choice} aria-pressed={active} onClick={onClick}>
      {children}
    </button>
  )
}
