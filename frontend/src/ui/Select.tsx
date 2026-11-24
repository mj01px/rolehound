import { useEffect, useId, useRef, useState, type KeyboardEvent } from 'react'
import styles from './Select.module.css'

export interface SelectOption<T extends string> {
  value: T
  label: string
}

interface SelectProps<T extends string> {
  value: T
  options: readonly SelectOption<T>[]
  onChange: (value: T) => void
  /** Accessible name when no visible <label htmlFor={id}> points at the control. */
  label?: string
  id?: string
  /** pill: filter chip, highlighted while a value other than `empty` is chosen. compact: 36px. field: a form input. */
  variant?: 'pill' | 'compact' | 'field'
  /** The value that means "no filter", for the pill's look. */
  empty?: T
  /** Shown on the button instead of the chosen option's label, such as the filter's name. */
  display?: string
}

/**
 * A select drawn by the app instead of the browser: a button that opens a listbox
 * (the WAI-ARIA "select-only combobox"). Focus stays on the button; arrows move,
 * Enter or Space chooses, Escape closes, Home/End jump, and typing a letter finds an option.
 */
export function Select<T extends string>({ value, options, onChange, label, id, variant = 'field', empty, display }: SelectProps<T>) {
  const fallbackId = useId()
  const buttonId = id ?? fallbackId
  const listId = `${buttonId}-list`
  const wrap = useRef<HTMLDivElement>(null)
  const list = useRef<HTMLUListElement>(null)
  const [open, setOpen] = useState(false)
  const [active, setActive] = useState(0)

  const selectedIndex = Math.max(
    0,
    options.findIndex((option) => option.value === value),
  )
  const selected = options[selectedIndex]

  const openAt = (index: number) => {
    setActive(index)
    setOpen(true)
  }

  const choose = (index: number) => {
    const option = options[index]
    if (option) onChange(option.value)
    setOpen(false)
  }

  useEffect(() => {
    if (!open) return
    const onPointer = (event: PointerEvent) => {
      if (!wrap.current?.contains(event.target as Node)) setOpen(false)
    }
    document.addEventListener('pointerdown', onPointer)
    return () => document.removeEventListener('pointerdown', onPointer)
  }, [open])

  // Keep the active option in view while moving with the keyboard.
  useEffect(() => {
    if (open) list.current?.querySelector<HTMLElement>(`[data-index="${active}"]`)?.scrollIntoView({ block: 'nearest' })
  }, [open, active])

  const onKeyDown = (event: KeyboardEvent<HTMLButtonElement>) => {
    const last = options.length - 1
    switch (event.key) {
      case 'ArrowDown':
        event.preventDefault()
        if (open) setActive((index) => Math.min(index + 1, last))
        else openAt(selectedIndex)
        break
      case 'ArrowUp':
        event.preventDefault()
        if (open) setActive((index) => Math.max(index - 1, 0))
        else openAt(selectedIndex)
        break
      case 'Home':
        if (open) {
          event.preventDefault()
          setActive(0)
        }
        break
      case 'End':
        if (open) {
          event.preventDefault()
          setActive(last)
        }
        break
      case 'Enter':
      case ' ':
        event.preventDefault()
        if (open) choose(active)
        else openAt(selectedIndex)
        break
      case 'Escape':
        if (open) {
          event.preventDefault()
          event.stopPropagation()
          setOpen(false)
        }
        break
      case 'Tab':
        setOpen(false)
        break
      default:
        if (event.key.length === 1 && /\S/.test(event.key)) {
          const letter = event.key.toLowerCase()
          const from = open ? active : selectedIndex
          const order = [...options.slice(from + 1), ...options.slice(0, from + 1)]
          const match = order.find((option) => option.label.toLowerCase().startsWith(letter))
          if (match) {
            const index = options.indexOf(match)
            if (open) setActive(index)
            else onChange(match.value)
          }
        }
    }
  }

  const on = variant === 'pill' && empty !== undefined && value !== empty
  const buttonClass = [styles.button, styles[variant], on && styles.on, open && styles.open].filter(Boolean).join(' ')

  return (
    <div ref={wrap} className={variant === 'field' ? `${styles.wrap} ${styles.wrapField}` : styles.wrap}>
      <button
        id={buttonId}
        type="button"
        role="combobox"
        aria-haspopup="listbox"
        aria-expanded={open}
        aria-controls={listId}
        aria-activedescendant={open ? `${listId}-${active}` : undefined}
        aria-label={label}
        className={buttonClass}
        onClick={() => (open ? setOpen(false) : openAt(selectedIndex))}
        onKeyDown={onKeyDown}
      >
        <span className={styles.value}>{display ?? selected?.label}</span>
        <svg className={styles.chevron} width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.5" strokeLinecap="round" aria-hidden="true">
          <path d="m6 9 6 6 6-6" />
        </svg>
      </button>
      {open && (
        <ul ref={list} id={listId} role="listbox" aria-label={label} className={styles.list}>
          {options.map((option, index) => (
            <li
              key={option.value}
              id={`${listId}-${index}`}
              data-index={index}
              role="option"
              aria-selected={option.value === value}
              className={index === active ? `${styles.option} ${styles.active}` : styles.option}
              onPointerDown={(event) => event.preventDefault()}
              onPointerEnter={() => setActive(index)}
              onClick={() => choose(index)}
            >
              <span>{option.label}</span>
              {option.value === value && (
                <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
                  <path d="m5 12 5 5 9-10" />
                </svg>
              )}
            </li>
          ))}
        </ul>
      )}
    </div>
  )
}
