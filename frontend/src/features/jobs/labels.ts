export const sourceLabels: Record<string, string> = {
  greenhouse: 'Greenhouse',
  lever: 'Lever',
  github: 'GitHub',
  inhire: 'InHire',
  gupy: 'Gupy',
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
export function ago(iso: string | null): string {
  if (!iso) return ''
  const days = Math.floor((Date.now() - new Date(iso).getTime()) / 86_400_000)
  if (days <= 0) return 'hoje'
  if (days === 1) return 'ontem'
  if (days < 30) return `há ${days} dias`
  return new Date(iso).toLocaleDateString('pt-BR')
}
