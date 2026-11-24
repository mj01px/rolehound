import { useState, type ReactNode } from 'react'
import { Link, useLocation } from 'react-router'
import { Logo } from '../ui/Logo'
import { icons } from './navIcons'
import styles from './Sidebar.module.css'

interface Item {
  to: string
  label: string
  icon: ReactNode
  /** Other paths that belong to this item, such as an open job. */
  also?: string
}

interface Group {
  id: string
  label: string
  items: Item[]
}

const searchGroup: Group = {
  id: 'search',
  label: 'Busca',
  items: [
    { to: '/', label: 'Vagas', icon: icons.jobs, also: '/vagas/' },
    { to: '/funil', label: 'Funil', icon: icons.funnel },
    { to: '/qualidade', label: 'Qualidade', icon: icons.quality },
  ],
}

const adminGroup: Group = {
  id: 'admin',
  label: 'Administração',
  items: [
    { to: '/admin/usuarios', label: 'Usuários', icon: icons.users },
    { to: '/admin/fontes', label: 'Fontes', icon: icons.sources },
    { to: '/admin/qualidade', label: 'Qualidade do score', icon: icons.accuracy },
    { to: '/admin/modelos', label: 'Modelos sugeridos', icon: icons.models },
  ],
}

interface Prefs {
  collapsed: boolean
  hidden: string[]
}

const PREFS_KEY = 'rolehound:sidebar'

// A per-browser convenience: when storage is blocked the sidebar just starts expanded.
function readPrefs(): Prefs {
  try {
    const stored = JSON.parse(localStorage.getItem(PREFS_KEY) ?? '{}') as Partial<Prefs>
    return { collapsed: stored.collapsed === true, hidden: Array.isArray(stored.hidden) ? stored.hidden : [] }
  } catch {
    return { collapsed: false, hidden: [] }
  }
}

function writePrefs(prefs: Prefs) {
  try {
    localStorage.setItem(PREFS_KEY, JSON.stringify(prefs))
  } catch {
    // Storage blocked: the choice lasts until the page reloads.
  }
}

interface SidebarProps {
  isAdmin: boolean
  onLogout: () => void
  loggingOut: boolean
  /** Phones: the sidebar slides over the page. */
  mobileOpen: boolean
  onCloseMobile: () => void
}

/**
 * Main navigation. On desktop it expands (icons and names) or collapses (icons
 * only); each module can be hidden. On phones it slides over the page.
 */
export function Sidebar({ isAdmin, onLogout, loggingOut, mobileOpen, onCloseMobile }: SidebarProps) {
  const [prefs, setPrefs] = useState(readPrefs)
  const { pathname } = useLocation()
  const groups = isAdmin ? [searchGroup, adminGroup] : [searchGroup]

  const update = (next: Prefs) => {
    setPrefs(next)
    writePrefs(next)
  }
  const toggleGroup = (id: string) =>
    update({ ...prefs, hidden: prefs.hidden.includes(id) ? prefs.hidden.filter((g) => g !== id) : [...prefs.hidden, id] })

  // Phones always show names; collapsing is a desktop choice.
  const collapsed = prefs.collapsed && !mobileOpen
  const classes = [styles.sidebar, collapsed && styles.collapsed, mobileOpen && styles.mobileOpen].filter(Boolean).join(' ')

  const link = (item: Item) => {
    const active = item.to === '/' ? pathname === '/' || (item.also !== undefined && pathname.startsWith(item.also)) : pathname.startsWith(item.to)
    return (
      <Link
        key={item.to}
        to={item.to}
        onClick={onCloseMobile}
        title={collapsed ? item.label : undefined}
        aria-label={collapsed ? item.label : undefined}
        aria-current={active ? 'page' : undefined}
        className={active ? `${styles.item} ${styles.active}` : styles.item}
      >
        <span className={styles.icon}>{item.icon}</span>
        <span className={styles.label}>{item.label}</span>
      </Link>
    )
  }

  return (
    <nav aria-label="Principal" className={classes}>
      <div className={styles.head}>
        <button
          type="button"
          className={styles.menu}
          onClick={mobileOpen ? onCloseMobile : () => update({ ...prefs, collapsed: !prefs.collapsed })}
          aria-label={mobileOpen ? 'Fechar menu' : collapsed ? 'Expandir menu' : 'Recolher menu'}
          aria-expanded={mobileOpen || !collapsed}
        >
          {icons.menu}
        </button>
        <span className={styles.brand}>
          <Logo size={20} label="" />
          <span className={styles.label}>rolehound</span>
        </span>
      </div>

      <div className={styles.groups}>
        {groups.map((group) => {
          const open = collapsed || !prefs.hidden.includes(group.id)
          const listId = `nav-${group.id}`
          return (
            <div key={group.id} className={styles.group}>
              {collapsed ? (
                <span className={styles.divider} aria-hidden="true" />
              ) : (
                <button type="button" className={styles.groupHead} onClick={() => toggleGroup(group.id)} aria-expanded={open} aria-controls={listId}>
                  <span>{group.label}</span>
                  <span className={open ? styles.chevron : `${styles.chevron} ${styles.chevronClosed}`}>{icons.chevron}</span>
                </button>
              )}
              {open && (
                <div id={listId} className={styles.items}>
                  {group.items.map(link)}
                </div>
              )}
            </div>
          )
        })}
      </div>

      <div className={styles.footer}>
        <button
          type="button"
          className={styles.item}
          onClick={onLogout}
          disabled={loggingOut}
          title={collapsed ? 'Sair' : undefined}
          aria-label={collapsed ? 'Sair' : undefined}
        >
          <span className={styles.icon}>{icons.logout}</span>
          <span className={styles.label}>Sair</span>
        </button>
      </div>
    </nav>
  )
}
