import { useMutation } from '@tanstack/react-query'
import { useState } from 'react'
import { api, errorMessage } from '../../lib/api'
import { Alert } from '../../ui/Alert'
import { Button } from '../../ui/Button'
import uiStyles from '../../ui/ui.module.css'
import type { ProfileDocument, ProfileOption } from './api'
import { ProfileRefiner } from './ProfileRefiner'
import styles from './onboarding.module.css'

interface OptionsResponse {
  dossierVersion: number
  options: ProfileOption[]
}

/**
 * AI proposes options only when asked (FR-SCO-01); the person picks one and may
 * ask the AI for changes before keeping it. Full editing lives on the settings page.
 */
export function ProfileStep({ onDone, onBack }: { onDone: () => Promise<void>; onBack?: (() => void) | undefined }) {
  const [selected, setSelected] = useState(0)
  // Options the person adjusted with the AI, by index; reset with every new batch.
  const [adjusted, setAdjusted] = useState<Record<number, ProfileDocument>>({})
  const generate = useMutation({
    mutationFn: () => api.post<OptionsResponse>('/score-profile/options'),
    onSuccess: () => {
      setSelected(0)
      setAdjusted({})
    },
  })
  const save = useMutation({
    mutationFn: (profile: ProfileDocument) => api.put('/score-profile', { profile, dossierVersion: generate.data?.dossierVersion }),
    onSuccess: onDone,
  })

  const options: ProfileOption[] = (generate.data?.options ?? []).map((option, i) => {
    const profile = adjusted[i]
    return profile ? { ...option, profile } : option
  })
  const chosen = options[selected]

  return (
    <div className={`${styles.column} ${styles.wide}`}>
      <div className={styles.titles}>
        <h1 className={styles.title}>Escolha seu perfil de score</h1>
        <p className={styles.lead}>
          A IA lê seu Dossier e propõe até três perfis. Eles definem o que soma e o que tira pontos de uma vaga. Dá para editar depois.
        </p>
      </div>

      {(generate.isError || save.isError) && <Alert>{errorMessage(generate.error ?? save.error)}</Alert>}

      {options.length > 0 && (
        <div role="radiogroup" aria-label="Perfis" className={styles.options}>
          {options.map((option, i) => (
            <label key={option.name} className={i === selected ? `${styles.option} ${styles.optionOn}` : styles.option}>
              <span className={styles.optionHead}>
                <input type="radio" name="perfil" checked={i === selected} onChange={() => setSelected(i)} />
                <span>
                  <span className={styles.optionName}>{option.name}</span>
                  <br />
                  <span className={styles.hint}>
                    {option.description}
                    {adjusted[i] && ' Ajustado por você.'}
                  </span>
                </span>
              </span>
              {i === selected && (
                <span className={styles.groups}>
                  {option.profile.groups.map((g) => (
                    <span key={g.name} style={{ display: 'contents' }}>
                      <span className={styles.label}>{g.name}</span>
                      <span className={styles.mono}>{g.terms.join(', ')}</span>
                      <span className={g.weight < 0 ? styles.negative : styles.positive}>{g.weight > 0 ? `+${g.weight}` : g.weight}</span>
                    </span>
                  ))}
                </span>
              )}
            </label>
          ))}
        </div>
      )}

      {chosen && (
        <ProfileRefiner
          key={selected}
          profile={chosen.profile}
          acceptLabel="Usar o ajuste"
          onAccept={(profile) => setAdjusted((current) => ({ ...current, [selected]: profile }))}
        />
      )}

      <div className={styles.actions}>
        {chosen ? (
          <Button size="large" block onClick={() => save.mutate(chosen.profile)} disabled={save.isPending}>
            Usar este perfil
          </Button>
        ) : (
          <Button size="large" block onClick={() => generate.mutate()} disabled={generate.isPending}>
            {generate.isPending ? 'Gerando opções…' : 'Gerar opções com IA'}
          </Button>
        )}
        <div className={styles.row} style={{ justifyContent: 'space-between' }}>
          {onBack ? (
            <button type="button" className={uiStyles.link} onClick={onBack}>
              Voltar
            </button>
          ) : (
            <span />
          )}
          {chosen && (
            <button type="button" className={uiStyles.link} onClick={() => generate.mutate()} disabled={generate.isPending}>
              Gerar outras opções
            </button>
          )}
        </div>
      </div>
    </div>
  )
}
