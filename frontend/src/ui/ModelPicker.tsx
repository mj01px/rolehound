import { useQuery } from '@tanstack/react-query'
import { useEffect, useId, useMemo, useRef, useState, type KeyboardEvent, type ReactNode } from 'react'
import { api } from '../lib/api'
import styles from './ModelPicker.module.css'
import type { ModelUsage } from './modelUsage'
import uiStyles from './ui.module.css'

/**
 * One entry of GET /ai/models, best rated first. Prices are US dollars per
 * million tokens; intelligence is the Artificial Analysis index, when known.
 */
export interface ModelOption {
  id: string
  name: string
  promptPerMillion: number
  completionPerMillion: number
  contextLength: number
  free: boolean
  expiresOn: string | null
  intelligence: number | null
}

const MAX_SHOWN = 60

function useModelCatalog() {
  return useQuery({
    queryKey: ['ai', 'models'],
    queryFn: ({ signal }) => api.get<ModelOption[]>('/ai/models', signal),
    staleTime: 60 * 60 * 1000,
    retry: false,
  })
}

interface ModelPickerProps {
  label: string
  value: string
  onChange: (id: string) => void
  /** A typical use of the function this field serves, to show prices per use. */
  usage: ModelUsage
  suggested?: string | undefined
  error?: string | undefined
  aside?: ReactNode
}

/**
 * A searchable list of OpenRouter models with prices. Typing an id by hand
 * still works, so the field keeps working when the list cannot load.
 */
export function ModelPicker({ label, value, onChange, usage, suggested, error, aside }: ModelPickerProps) {
  const id = useId()
  const listId = `${id}-list`
  const hintId = `${id}-hint`
  const catalog = useModelCatalog()
  const [open, setOpen] = useState(false)
  const [query, setQuery] = useState('')
  const [onlyFree, setOnlyFree] = useState(false)
  const [active, setActive] = useState(0)
  const wrapRef = useRef<HTMLDivElement>(null)

  // A tap outside closes the list. Blur alone cannot tell: on phones, tapping the
  // "Só grátis" box blurs the field with no element receiving focus.
  useEffect(() => {
    if (!open) {
      return
    }
    const onPointerDown = (event: PointerEvent) => {
      if (!wrapRef.current?.contains(event.target as Node)) {
        setOpen(false)
      }
    }
    document.addEventListener('pointerdown', onPointerDown)
    return () => document.removeEventListener('pointerdown', onPointerDown)
  }, [open])

  const models = catalog.data
  const selected = models?.find((m) => m.id === value)

  const shown = useMemo(() => {
    if (!models) {
      return []
    }

    const terms = query.toLowerCase().split(/\s+/).filter(Boolean)
    const matches = models.filter((m) => {
      const text = `${m.id} ${m.name}`.toLowerCase()
      return (!onlyFree || m.free) && terms.every((t) => text.includes(t))
    })
    const pinned = matches.find((m) => m.id === suggested)
    return pinned ? [pinned, ...matches.filter((m) => m !== pinned)] : matches
  }, [models, query, onlyFree, suggested])

  const visible = shown.slice(0, MAX_SHOWN)

  const choose = (model: ModelOption) => {
    onChange(model.id)
    setOpen(false)
  }

  const onKeyDown = (event: KeyboardEvent<HTMLInputElement>) => {
    if (event.key === 'ArrowDown' || event.key === 'ArrowUp') {
      event.preventDefault()
      if (!open) {
        setOpen(true)
        return
      }
      const step = event.key === 'ArrowDown' ? 1 : -1
      setActive((i) => (visible.length === 0 ? 0 : (i + step + visible.length) % visible.length))
    } else if (event.key === 'Enter' && open && visible[active]) {
      event.preventDefault()
      choose(visible[active])
    } else if (event.key === 'Escape' && open) {
      event.preventDefault()
      setOpen(false)
    }
  }

  const hint = error ?? describe(selected, value, models !== undefined, catalog.isError)

  return (
    <div className={uiStyles.field}>
      <div className={uiStyles.labelRow}>
        <label htmlFor={id} className={uiStyles.label}>
          {label}
        </label>
        {aside}
      </div>
      <div
        ref={wrapRef}
        className={styles.wrap}
        onBlur={(event) => {
          // Only a move of focus to somewhere else (Tab) closes; a tap with no new focus is handled above.
          if (event.relatedTarget && !event.currentTarget.contains(event.relatedTarget)) {
            setOpen(false)
          }
        }}
      >
        <input
          id={id}
          role="combobox"
          aria-expanded={open}
          aria-controls={listId}
          aria-autocomplete="list"
          aria-activedescendant={open && visible[active] ? `${listId}-${active}` : undefined}
          aria-invalid={error ? true : undefined}
          aria-describedby={hint ? hintId : undefined}
          className={[uiStyles.input, styles.input, error && uiStyles.inputError].filter(Boolean).join(' ')}
          value={value}
          placeholder="Busque por nome ou provedor"
          spellCheck={false}
          autoComplete="off"
          onFocus={() => {
            setQuery('')
            setActive(0)
            setOpen(true)
          }}
          onClick={() => setOpen(true)}
          onChange={(event) => {
            onChange(event.target.value)
            setQuery(event.target.value)
            setActive(0)
            setOpen(true)
          }}
          onKeyDown={onKeyDown}
        />
        {open && models && (
          // Pressing inside the list keeps focus in the field; otherwise a tap on "Só grátis" moves it to the modal and the list closes.
          <div className={styles.panel} onMouseDown={(event) => event.preventDefault()}>
            <label className={styles.filter}>
              <input type="checkbox" checked={onlyFree} onChange={(event) => setOnlyFree(event.target.checked)} />
              Só grátis
            </label>
            <ul id={listId} role="listbox" className={styles.list} aria-label={label}>
              {visible.map((model, index) => (
                <li
                  key={model.id}
                  id={`${listId}-${index}`}
                  role="option"
                  aria-selected={model.id === value}
                  className={[styles.option, index === active && styles.active].filter(Boolean).join(' ')}
                  onMouseDown={(event) => event.preventDefault()}
                  onMouseEnter={() => setActive(index)}
                  onClick={() => choose(model)}
                >
                  <span className={styles.name}>
                    {model.name}
                    {model.id === suggested && <span className={styles.badge}>Sugerido</span>}
                    {model.free && <span className={styles.badge}>Grátis</span>}
                  </span>
                  <span className={styles.meta}>
                    <span className={styles.id}>{model.id}</span>
                    {!model.free && <span className={styles.price}>{perUse(model, usage)}</span>}
                  </span>
                </li>
              ))}
              {visible.length === 0 && <li className={styles.empty}>Nenhum modelo encontrado. Você pode digitar o identificador.</li>}
              {shown.length > MAX_SHOWN && <li className={styles.empty}>Mais {shown.length - MAX_SHOWN} modelos. Refine a busca.</li>}
            </ul>
          </div>
        )}
      </div>
      {hint && (
        <span id={hintId} className={error ? uiStyles.error : uiStyles.help}>
          {hint}
        </span>
      )}
    </div>
  )
}

const usd = new Intl.NumberFormat('pt-BR', { style: 'currency', currency: 'USD', minimumFractionDigits: 2, maximumFractionDigits: 2 })
const day = new Intl.DateTimeFormat('pt-BR', { timeZone: 'UTC' })

/** What one typical use costs, in dollars. */
function costOf(model: ModelOption, usage: ModelUsage) {
  return (usage.promptTokens * model.promptPerMillion + usage.completionTokens * model.completionPerMillion) / 1_000_000
}

function perUse(model: ModelOption, usage: ModelUsage) {
  const cost = costOf(model, usage)
  return cost < 0.01 ? `menos de ${usd.format(0.01)} por ${usage.unit}` : `≈ ${usd.format(cost)} por ${usage.unit}`
}

function describe(model: ModelOption | undefined, value: string, loaded: boolean, failed: boolean) {
  if (failed) {
    return 'Não deu para carregar a lista. Digite o identificador do OpenRouter, como deepseek/deepseek-chat.'
  }
  if (!model) {
    return loaded && value.trim() ? 'Esse modelo não está na lista. Vamos testar ao salvar.' : undefined
  }

  const parts = model.free ? ['Grátis: pode usar o que você envia para treino, e o OpenRouter limita os pedidos por dia.'] : []
  if (model.expiresOn) {
    parts.push(`Sai do OpenRouter em ${day.format(new Date(`${model.expiresOn}T00:00:00Z`))}.`)
  }
  return parts.length > 0 ? parts.join(' ') : undefined
}
