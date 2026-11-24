import { useState, type CSSProperties, type ReactNode } from 'react'
import { errorMessage } from '../../lib/api'
import { Alert, Notice } from '../../ui/Alert'
import { Button } from '../../ui/Button'
import { workModeLabels } from '../jobs/labels'
import { monthName, percent, useLabel, useMetrics, useSample, useSkipSample, type Metrics, type Rate, type Sample, type SampleItem } from './api'
import styles from './accuracy.module.css'

// The API turns a count into a percentage from this many answers on (MinimumLabels).
const ENOUGH = 20

/** Score quality: how it works, this month's sample, and what the answers say. */
export function AccuracyPage() {
  const metrics = useMetrics()
  const sample = useSample()
  const [labelling, setLabelling] = useState(false)
  const pending = sample.data?.items.filter((item) => item.status === 'pending') ?? []

  if (labelling && sample.data && pending.length > 0) {
    return (
      <div className={styles.page}>
        <Labeller sample={sample.data} item={pending[0]!} onStop={() => setLabelling(false)} />
      </div>
    )
  }

  return (
    <div className={styles.page}>
      <header className={styles.header}>
        <h1 className={styles.title}>Qualidade do score</h1>
        <p className={styles.lead}>O score decide o que vira destaque na sua lista. Esta tela mede se ele acerta, com a sua opinião sobre uma amostra de vagas por mês.</p>
      </header>

      {(metrics.isError || sample.isError) && <Alert>{errorMessage(metrics.error ?? sample.error)}</Alert>}

      {metrics.data?.inFirstThirtyDays && (
        <Notice>
          <strong className={styles.noticeTitle}>Seus números ainda estão se formando.</strong> Você entrou há menos de 30 dias. A precisão aparece com {ENOUGH} respostas, somando os meses. Até lá, cada resposta já ajuda.
        </Notice>
      )}

      <section aria-label="Como funciona" className={styles.steps}>
        <Step number={1} title={`Sorteamos até ${ENOUGH} destaques`}>
          Todo mês, entre as vagas que viraram destaque nos últimos 30 dias.
        </Step>
        <Step number={2} title="Você responde duas perguntas">
          A vaga era relevante? O modelo de trabalho (remoto, híbrido, presencial) estava certo?
        </Step>
        <Step number={3} title={`Com ${ENOUGH} respostas, sai a precisão`}>
          Somando os meses. Antes disso o número oscila demais para dizer alguma coisa.
        </Step>
      </section>

      {sample.data && <SampleCard sample={sample.data} pending={pending.length} onStart={() => setLabelling(true)} />}

      {metrics.data && <Numbers metrics={metrics.data} />}
    </div>
  )
}

function Step({ number, title, children }: { number: number; title: string; children: ReactNode }) {
  return (
    <div className={styles.step}>
      <span className={styles.stepNumber}>{number}</span>
      <span className={styles.stepTitle}>{title}</span>
      <span className={styles.stepText}>{children}</span>
    </div>
  )
}

function SampleCard({ sample, pending, onStart }: { sample: Sample; pending: number; onStart: () => void }) {
  const skip = useSkipSample()
  const total = sample.items.length
  const done = total - pending
  const skipped = sample.skipped > 0 && sample.labelled === 0 && pending === 0

  return (
    <section aria-labelledby="sample-title" className={styles.sample}>
      <div className={styles.sampleBody}>
        <div className={styles.sampleHead}>
          <h2 id="sample-title" className={styles.sampleTitle}>
            Amostra de {monthName(sample.month)}
          </h2>
          {total > 0 && (
            <span className={styles.mono}>
              {done} de {total}
            </span>
          )}
        </div>
        {total > 0 && <Progress done={done} total={total} />}
        <span className={styles.sampleText}>
          {total === 0
            ? 'Nenhuma vaga virou destaque nos últimos 30 dias, então não há o que rotular este mês.'
            : skipped
              ? 'Você pulou a amostra deste mês. A próxima chega no mês que vem.'
              : pending === 0
                ? `Pronto: ${sample.labelled} ${sample.labelled === 1 ? 'vaga rotulada' : 'vagas rotuladas'} este mês. A próxima amostra chega no mês que vem.`
                : done === 0
                  ? `${total} ${total === 1 ? 'destaque sorteado' : 'destaques sorteados'} dos últimos 30 dias. Leva uns dois minutos e dá para parar no meio.`
                  : `Faltam ${pending} ${pending === 1 ? 'vaga' : 'vagas'}. Dá para parar e continuar depois.`}
        </span>
        {skip.isError && <Alert>{errorMessage(skip.error)}</Alert>}
      </div>
      {pending > 0 && (
        <div className={styles.sampleActions}>
          <Button onClick={onStart}>{done > 0 ? 'Continuar' : 'Começar'}</Button>
          <button type="button" className={styles.quietButton} onClick={() => skip.mutate()} disabled={skip.isPending}>
            Pular este mês
          </button>
        </div>
      )}
    </section>
  )
}

function Progress({ done, total }: { done: number; total: number }) {
  return (
    <div className={styles.progress} role="progressbar" aria-valuemin={0} aria-valuemax={total} aria-valuenow={done} aria-label="Respostas da amostra">
      <div className={styles.progressFill} style={{ width: `${total ? (done / total) * 100 : 0}%` }} />
    </div>
  )
}

/** "85%", or the count while there are not enough answers for a percentage. */
function Figure({ rate }: { rate: Rate }) {
  const value = percent(rate)
  return <span className={value ? styles.figure : `${styles.figure} ${styles.figureWaiting}`}>{value ?? (rate.total === 0 ? 'Sem dados' : `${rate.hits} de ${rate.total}`)}</span>
}

/** "17 de 20 na amostra", or how many more are needed: one, many or none yet. */
function countLine(rate: Rate, where: string, one: string, many: string) {
  if (rate.total === 0) return `Nenhuma ${one} ainda`
  const missing = ENOUGH - rate.total
  if (rate.value === null) return missing === 1 ? `Falta 1 ${one} para a porcentagem` : `Faltam ${missing} ${many} para a porcentagem`
  return `${rate.hits} de ${rate.total} ${where}`
}

function Numbers({ metrics }: { metrics: Metrics }) {
  return (
    <>
      <section aria-label="Números" className={styles.cards}>
        <Card title="Precisão dos destaques" rate={metrics.precision} count={countLine(metrics.precision, 'na amostra', 'resposta', 'respostas')}>
          <Explain label="O que é">de cada destaque que você avaliou, quantos eram relevantes para você.</Explain>
          <Explain label="Se cair">o score está destacando vagas que não são para você. Vale ajustar o perfil em Configurações.</Explain>
        </Card>
        <Card title="Modelo de trabalho certo" rate={metrics.workMode} count={countLine(metrics.workMode, 'na amostra', 'resposta', 'respostas')}>
          <Explain label="O que é">quantas vezes o app acertou se a vaga é remota, híbrida ou presencial. Vagas sem modelo e respostas &quot;Não sei&quot; ficam de fora.</Explain>
          <Explain label="Se cair">o filtro de modelo mostra ou esconde as vagas erradas.</Explain>
        </Card>
        <Card title="Destaques perdidos" rate={metrics.missedHighlights} count={countLine(metrics.missedHighlights, 'candidaturas', 'candidatura', 'candidaturas')}>
          <Explain label="O que é">vagas em que você se candidatou mas que não chegaram como destaque.</Explain>
          <Explain label="Se subir">o score está deixando passar vagas boas. Aqui, menor é melhor.</Explain>
        </Card>
      </section>

      <div className={styles.bottom}>
        <MonthsChart months={metrics.months} />
        <section aria-labelledby="signals-title" className={styles.signals}>
          <div className={styles.sectionHead}>
            <h2 id="signals-title" className={styles.sectionTitle}>
              Sinais de uso
            </h2>
            <span className={styles.sectionHint}>O que você fez com os destaques, sem precisar responder nada. Complementam a amostra.</span>
          </div>
          <dl className={styles.signalList}>
            <div className={styles.signal}>
              <dt>Foram para o funil</dt>
              <dd>{metrics.signalsRelevant}</dd>
            </div>
            <div className={styles.signal}>
              <dt>Arquivados sem interesse ou por requisitos</dt>
              <dd>{metrics.signalsNotRelevant}</dd>
            </div>
            <div className={styles.signal}>
              <dt>Arquivados pelo modelo de trabalho</dt>
              <dd>{metrics.workModeArchives}</dd>
            </div>
          </dl>
        </section>
      </div>
    </>
  )
}

function Card({ title, rate, count, children }: { title: string; rate: Rate; count: string; children: ReactNode }) {
  return (
    <div className={styles.card}>
      <span className={styles.cardTitle}>{title}</span>
      <Figure rate={rate} />
      <span className={styles.cardCount}>{count}</span>
      <div className={styles.cardExplain}>{children}</div>
    </div>
  )
}

function Explain({ label, children }: { label: string; children: ReactNode }) {
  return (
    <p className={styles.explain}>
      <strong>{label}:</strong> {children}
    </p>
  )
}

const shortMonth = (month: string) => monthName(month).split(' ')[0]?.slice(0, 3) ?? month

/** Precision per month, oldest first; months short of answers show a dashed outline and the count. */
function MonthsChart({ months }: { months: Metrics['months'] }) {
  const shown = [...months].reverse().slice(-6)

  return (
    <section aria-labelledby="months-title" className={styles.chart}>
      <div className={styles.chartHead}>
        <h2 id="months-title" className={styles.sectionTitle}>
          Precisão por mês
        </h2>
        {shown.length > 0 && <span className={styles.sectionHint}>meses com menos de {ENOUGH} respostas ficam tracejados</span>}
      </div>
      {shown.length === 0 ? (
        <p className={styles.sectionHint}>O gráfico aparece depois da primeira amostra rotulada.</p>
      ) : (
        <>
          <div className={styles.bars} style={{ '--columns': shown.length } as CSSProperties}>
            {shown.map((month, index) => {
              const value = month.precision.value
              const last = index === shown.length - 1
              const height = value === null ? Math.max(12, (month.precision.total / ENOUGH) * 100) : value * 100
              return (
                <div key={month.month} className={styles.barColumn} title={`${monthName(month.month)}: ${percent(month.precision) ?? `${month.precision.hits} de ${month.precision.total}`}`}>
                  <span className={last ? `${styles.barValue} ${styles.barValueLast}` : styles.barValue}>{percent(month.precision) ?? `${month.precision.total} de ${ENOUGH}`}</span>
                  <div
                    className={value === null ? `${styles.bar} ${styles.barWaiting}` : last ? `${styles.bar} ${styles.barLast}` : styles.bar}
                    style={{ height: `${height}%` }}
                  />
                </div>
              )
            })}
          </div>
          <div className={styles.barLabels} style={{ '--columns': shown.length } as CSSProperties}>
            {shown.map((month, index) => (
              <span key={month.month} className={index === shown.length - 1 ? styles.barLabelLast : undefined}>
                {shortMonth(month.month)}
              </span>
            ))}
          </div>
        </>
      )}
    </section>
  )
}

/** One job at a time: is it relevant, and is the work mode right. Answering the second moves on. */
function Labeller({ sample, item, onStop }: { sample: Sample; item: SampleItem; onStop: () => void }) {
  const label = useLabel()
  const [relevant, setRelevant] = useState<boolean | null>(null)
  const knownMode = item.shownWorkMode !== 'unknown'
  const total = sample.items.length
  const done = total - sample.items.filter((i) => i.status === 'pending').length

  const send = (workModeRight: boolean | null) => {
    if (relevant === null) return
    label.mutate({ id: item.id, relevant, workModeRight }, { onSuccess: () => setRelevant(null) })
  }

  return (
    <div className={styles.labeller}>
      <div className={styles.labellerHead}>
        <div className={styles.sampleHead}>
          <span className={styles.sampleTitle}>Amostra de {monthName(sample.month)}</span>
          <span className={styles.mono}>
            {done + 1} de {total}
          </span>
        </div>
        <Progress done={done} total={total} />
      </div>

      <article className={styles.job} aria-label="Vaga">
        <span className={styles.jobScore}>
          <span className={styles.mono}>{item.score > 0 ? `+${item.score}` : item.score}</span>
          <span className={styles.marker}>
            <span className={styles.markerDot} aria-hidden="true" />
            Destaque
          </span>
        </span>
        <h2 className={styles.jobTitle}>{item.title}</h2>
        <p className={styles.jobMeta}>{[item.company, knownMode ? workModeLabels[item.shownWorkMode] : null].filter(Boolean).join(' · ')}</p>
        {item.jobId && (
          <a href={`/vagas/${item.jobId}`} target="_blank" rel="noopener noreferrer" className={styles.open}>
            Abrir a vaga
            <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.5" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
              <path d="M7 17 17 7M8 7h9v9" />
            </svg>
          </a>
        )}
      </article>

      <fieldset className={styles.question}>
        <legend>Essa vaga é relevante para você?</legend>
        <div className={styles.choices2}>
          <Choice active={relevant === true} onClick={() => setRelevant(true)}>
            Sim, me interessa
          </Choice>
          <Choice active={relevant === false} onClick={() => setRelevant(false)}>
            Não
          </Choice>
        </div>
      </fieldset>

      <fieldset className={styles.question} disabled={relevant === null || label.isPending}>
        <legend>{knownMode ? `O app disse "${workModeLabels[item.shownWorkMode]}". Está certo?` : 'A vaga não diz o modelo de trabalho.'}</legend>
        {knownMode ? (
          <div className={styles.choices3}>
            <Choice onClick={() => send(true)}>Certo</Choice>
            <Choice onClick={() => send(false)}>Errado</Choice>
            <Choice quiet onClick={() => send(null)}>
              Não sei
            </Choice>
          </div>
        ) : (
          <div className={styles.choices2}>
            <Choice onClick={() => send(null)}>Próxima vaga</Choice>
          </div>
        )}
        <span className={styles.questionHint}>{relevant === null ? 'Responda a primeira pergunta antes.' : 'Responder aqui já passa para a próxima vaga.'}</span>
      </fieldset>

      {label.isError && <Alert>{errorMessage(label.error)}</Alert>}

      <div className={styles.labellerFoot}>
        <button type="button" className={styles.quietButton} onClick={onStop}>
          Parar por agora
        </button>
        <span className={styles.sectionHint}>Suas respostas ficam salvas. Dá para continuar depois.</span>
      </div>
    </div>
  )
}

function Choice({ active = false, quiet = false, onClick, children }: { active?: boolean; quiet?: boolean; onClick: () => void; children: string }) {
  const classes = [styles.choice, active && styles.choiceActive, quiet && styles.choiceQuiet].filter(Boolean).join(' ')
  return (
    <button type="button" className={classes} aria-pressed={active} onClick={onClick}>
      {children}
    </button>
  )
}
