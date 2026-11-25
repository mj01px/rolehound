export const sourceLabels: Record<string, string> = {
  greenhouse: 'Greenhouse',
  lever: 'Lever',
  github: 'GitHub',
  inhire: 'InHire',
  gupy: 'Gupy',
  workday: 'Workday',
  careersite: 'Site de carreiras',
  emailalert: 'Alerta por e-mail',
  link: 'Link',
}

export const workModeLabels: Record<string, string> = {
  remote: 'Remoto',
  hybrid: 'Híbrido',
  onsite: 'Presencial',
  unknown: 'Modelo não informado',
}

export const seniorityLabels: Record<string, string> = {
  internship: 'Estágio',
  junior: 'Júnior',
  mid: 'Pleno',
  senior: 'Sênior',
  unknown: 'Nível não informado',
}

export const archiveReasons = [
  { value: 'not_interested', label: 'Sem interesse' },
  { value: 'requirements', label: 'Requisitos' },
  { value: 'onsite', label: 'Presencial' },
  { value: 'hybrid', label: 'Híbrido' },
  { value: 'remote', label: 'Remoto' },
  { value: 'compensation', label: 'Remuneração' },
  { value: 'closed', label: 'Vaga encerrada' },
  { value: 'other', label: 'Outro' },
] as const

export type ArchiveReason = (typeof archiveReasons)[number]['value']

export const archiveReasonLabel = (value: string | null): string =>
  archiveReasons.find((reason) => reason.value === value)?.label ?? 'Arquivada'

/** "há 3 dias", from an ISO date. */
const ZONE = 'America/Sao_Paulo'
const calendarDay = new Intl.DateTimeFormat('en-CA', { timeZone: ZONE, year: 'numeric', month: '2-digit', day: '2-digit' })
const shortDate = new Intl.DateTimeFormat('pt-BR', { timeZone: ZONE, day: '2-digit', month: '2-digit' })
const fullDate = new Intl.DateTimeFormat('pt-BR', { timeZone: ZONE, day: '2-digit', month: '2-digit', year: 'numeric' })

// Midnight UTC of the date São Paulo shows, so subtracting two gives whole calendar days.
const dayNumber = (date: Date) => Date.parse(calendarDay.format(date)) / 86_400_000

/**
 * When a job was posted, in calendar days in São Paulo: "hoje", "ontem", "há 3 dias"
 * for a week, then the date, with the year only when it is not this year.
 */
export function ago(iso: string | null, now: Date = new Date()): string {
  if (!iso) return ''
  const date = new Date(iso)
  const days = dayNumber(now) - dayNumber(date)
  if (days <= 0) return 'hoje'
  if (days === 1) return 'ontem'
  if (days < 7) return `há ${days} dias`
  return calendarDay.format(date).slice(0, 4) === calendarDay.format(now).slice(0, 4) ? shortDate.format(date) : fullDate.format(date)
}
