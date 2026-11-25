import { useEffect, useId, useMemo, useRef, useState, type KeyboardEvent } from 'react'
import select from '../../ui/Select.module.css'
import type { CompanyCity } from './api'
import styles from './companies.module.css'

const normalize = (text: string) =>
  text
    .normalize('NFD')
    .replace(/\p{Diacritic}/gu, '')
    .toLowerCase()

/**
 * The city filter of a company page: a pill that opens a searchable list of every place
 * the company has open jobs in (a combobox: typing filters, arrows move, Enter picks, Escape closes).
 */
export function CityPicker({ cities, value, onChange }: { cities: CompanyCity[]; value: string; onChange: (city: string) => void }) {
  const [open, setOpen] = useState(false)
  const [query, setQuery] = useState('')
  const [active, setActive] = useState(0)
  const wrap = useRef<HTMLDivElement>(null)
  const input = useRef<HTMLInputElement>(null)
  const listId = useId()

  const words = normalize(query.trim())
  const options = useMemo(
    () => [{ name: '', jobs: cities.reduce((sum, city) => sum + city.jobs, 0) }, ...cities.filter((city) => !words || normalize(city.name).includes(words))],
    [cities, words],
  )

  useEffect(() => {
    if (!open) return
    input.current?.focus()
    const onPointer = (event: PointerEvent) => {
      if (!wrap.current?.contains(event.target as Node)) setOpen(false)
    }
    document.addEventListener('pointerdown', onPointer)
    return () => document.removeEventListener('pointerdown', onPointer)
  }, [open])

  const pick = (city: string) => {
    onChange(city)
    setOpen(false)
    setQuery('')
  }

  const onKey = (event: KeyboardEvent<HTMLInputElement>) => {
    if (event.key === 'ArrowDown') {
      event.preventDefault()
      setActive((index) => Math.min(index + 1, options.length - 1))
    } else if (event.key === 'ArrowUp') {
      event.preventDefault()
      setActive((index) => Math.max(index - 1, 0))
    } else if (event.key === 'Enter') {
      event.preventDefault()
      const option = options[active]
      if (option) pick(option.name)
    } else if (event.key === 'Escape') {
      setOpen(false)
    }
  }

  return (
    <div ref={wrap} className={select.wrap}>
      <button
        type="button"
        className={[select.button, select.pill, value && select.on, open && select.open].filter(Boolean).join(' ')}
        aria-haspopup="listbox"
        aria-expanded={open}
        onClick={() => {
          setOpen((current) => !current)
          setActive(0)
        }}
      >
        <span className={select.value}>{value || 'Todas as cidades'}</span>
        <svg className={select.chevron} width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" aria-hidden="true">
          <path d="m6 9 6 6 6-6" />
        </svg>
      </button>
      {open && (
        <div className={styles.cityPopover}>
          <input
            ref={input}
            type="search"
            role="combobox"
            aria-expanded="true"
            aria-controls={listId}
            aria-activedescendant={`${listId}-${active}`}
            aria-label="Buscar cidade"
            placeholder="Buscar cidade"
            className={styles.cityInput}
            value={query}
            onChange={(event) => {
              setQuery(event.target.value)
              setActive(0)
            }}
            onKeyDown={onKey}
          />
          <ul id={listId} role="listbox" aria-label="Cidades" className={styles.cityList}>
            {options.map((city, index) => (
              <li
                key={city.name || 'all'}
                id={`${listId}-${index}`}
                role="option"
                aria-selected={city.name === value}
                className={index === active ? `${select.option} ${select.active}` : select.option}
                onPointerEnter={() => setActive(index)}
                onClick={() => pick(city.name)}
              >
                <span className={select.value}>{city.name || 'Todas as cidades'}</span>
                <span className={styles.cityCount}>{city.jobs}</span>
              </li>
            ))}
            {options.length === 1 && <li className={styles.cityEmpty}>Nenhuma cidade com esse nome.</li>}
          </ul>
        </div>
      )}
    </div>
  )
}
