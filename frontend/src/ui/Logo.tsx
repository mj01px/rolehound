/** The mark: a geometric "r" and the signal cursor block. An empty label hides it from screen readers (the name is written beside it). */
export function Logo({ size = 24, label = 'rolehound' }: { size?: number; label?: string }) {
  const a11y = label ? { role: 'img', 'aria-label': label } : { 'aria-hidden': true }
  return (
    <svg width={size} height={size} viewBox="0 0 32 32" {...a11y}>
      <path d="M9 25V14.5a5 5 0 0 1 5-5h3" fill="none" stroke="var(--ink)" strokeWidth="4.5" />
      <rect x="19.5" y="20" width="5.5" height="5" fill="var(--signal)" />
    </svg>
  )
}
