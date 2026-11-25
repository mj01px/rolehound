/** The page of a list to show, clamped so a list that shrank never shows an empty page. */
export function pageOf<T>(items: T[], page: number, size: number) {
  const pages = Math.max(1, Math.ceil(items.length / size))
  const shown = Math.min(Math.max(page, 0), pages - 1)
  return { pages, shown, items: items.slice(shown * size, shown * size + size) }
}
