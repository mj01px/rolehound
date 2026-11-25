import styles from './Pager.module.css'

/** Page numbers to show: all when few, otherwise first, last and the neighbours of the current one, with gaps (null). */
function pageList(current: number, total: number): (number | null)[] {
  if (total <= 7) return Array.from({ length: total }, (_, index) => index)
  const middle = [current - 1, current, current + 1].filter((index) => index > 0 && index < total - 1)
  const list: (number | null)[] = [0]
  if ((middle[0] ?? 1) > 1) list.push(null)
  list.push(...middle)
  if ((middle[middle.length - 1] ?? total - 2) < total - 2) list.push(null)
  list.push(total - 1)
  return list
}

/** Numbered pages with previous and next; nothing when there is a single page. Pages are zero-based. */
export function Pager({ page, pages, onChange, label }: { page: number; pages: number; onChange: (page: number) => void; label: string }) {
  if (pages <= 1) return null
  return (
    <nav className={styles.pager} aria-label={label}>
      <button type="button" className={styles.button} onClick={() => onChange(page - 1)} disabled={page === 0} aria-label="Página anterior">
        <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.6" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
          <path d="m15 18-6-6 6-6" />
        </svg>
      </button>
      {pageList(page, pages).map((item, index) =>
        item === null ? (
          <span key={`gap-${index}`} className={styles.gap} aria-hidden="true">
            …
          </span>
        ) : (
          <button
            key={item}
            type="button"
            className={item === page ? `${styles.button} ${styles.current}` : styles.button}
            onClick={() => onChange(item)}
            aria-current={item === page ? 'page' : undefined}
            aria-label={`Página ${item + 1}`}
          >
            {item + 1}
          </button>
        ),
      )}
      <button type="button" className={styles.button} onClick={() => onChange(page + 1)} disabled={page === pages - 1} aria-label="Próxima página">
        <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.6" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
          <path d="m9 18 6-6-6-6" />
        </svg>
      </button>
    </nav>
  )
}
