import { useEffect, useLayoutEffect, useRef, useState, type CSSProperties, type KeyboardEvent } from 'react'
import { createPortal } from 'react-dom'
import styles from './overlay.module.css'

export interface MenuItem {
  key: string
  label: string
  danger?: boolean
  run: () => void
}

const MENU_WIDTH = 220
const EDGE = 8

/**
 * The ⋯ button and its menu. The menu renders on the page, not inside the
 * table's scroll box, so opening it never adds a scrollbar. It sits centered
 * under the button and stays on screen.
 */
export function MoreMenu({ label, items, disabled = false }: { label: string; items: MenuItem[]; disabled?: boolean }) {
  const [open, setOpen] = useState(false)
  const [place, setPlace] = useState<CSSProperties>({})
  const wrap = useRef<HTMLDivElement>(null)
  const trigger = useRef<HTMLButtonElement>(null)
  const list = useRef<HTMLDivElement>(null)

  useLayoutEffect(() => {
    if (!open || !trigger.current) return
    const rect = trigger.current.getBoundingClientRect()
    const centered = rect.left + rect.width / 2 - MENU_WIDTH / 2
    const left = Math.min(Math.max(EDGE, centered), window.innerWidth - MENU_WIDTH - EDGE)
    setPlace({ top: rect.bottom + 6, left, width: MENU_WIDTH })
  }, [open])

  useEffect(() => {
    if (!open) return
    list.current?.querySelector<HTMLButtonElement>('[role="menuitem"]')?.focus()
    const onPointer = (event: PointerEvent) => {
      const target = event.target as Node
      if (!wrap.current?.contains(target) && !list.current?.contains(target)) setOpen(false)
    }
    const onMove = () => setOpen(false)
    document.addEventListener('pointerdown', onPointer)
    window.addEventListener('resize', onMove)
    window.addEventListener('scroll', onMove, true)
    return () => {
      document.removeEventListener('pointerdown', onPointer)
      window.removeEventListener('resize', onMove)
      window.removeEventListener('scroll', onMove, true)
    }
  }, [open])

  const close = () => {
    setOpen(false)
    trigger.current?.focus()
  }

  const onKey = (event: KeyboardEvent<HTMLDivElement>) => {
    const buttons = [...(list.current?.querySelectorAll<HTMLButtonElement>('[role="menuitem"]') ?? [])]
    const index = buttons.indexOf(document.activeElement as HTMLButtonElement)
    const move = (to: number) => buttons[(to + buttons.length) % buttons.length]?.focus()
    if (event.key === 'ArrowDown') move(index + 1)
    else if (event.key === 'ArrowUp') move(index - 1)
    else if (event.key === 'Home') move(0)
    else if (event.key === 'End') move(buttons.length - 1)
    else if (event.key === 'Escape') close()
    else if (event.key === 'Tab') setOpen(false)
    else return
    if (event.key !== 'Tab') event.preventDefault()
  }

  return (
    <div ref={wrap} className={styles.more}>
      <button
        ref={trigger}
        type="button"
        className={open ? `${styles.iconButton} ${styles.iconButtonOn}` : styles.iconButton}
        aria-label={label}
        aria-haspopup="menu"
        aria-expanded={open}
        onClick={() => setOpen((value) => !value)}
        disabled={disabled}
      >
        <svg width="16" height="16" viewBox="0 0 24 24" fill="currentColor" aria-hidden="true">
          <circle cx="5" cy="12" r="1.6" />
          <circle cx="12" cy="12" r="1.6" />
          <circle cx="19" cy="12" r="1.6" />
        </svg>
      </button>
      {open &&
        createPortal(
          <div ref={list} role="menu" aria-label={label} className={styles.menu} style={place} onKeyDown={onKey}>
            {items.map((item) => (
              <div key={item.key}>
                {item.danger && <div className={styles.menuSeparator} role="separator" />}
                <button
                  type="button"
                  role="menuitem"
                  className={item.danger ? `${styles.menuItem} ${styles.menuDanger}` : styles.menuItem}
                  onClick={() => {
                    setOpen(false)
                    item.run()
                  }}
                >
                  {item.label}
                </button>
              </div>
            ))}
          </div>,
          document.body,
        )}
    </div>
  )
}
