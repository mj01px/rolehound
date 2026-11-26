import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useCallback, useEffect, useState, type FormEvent } from 'react'
import { Link } from 'react-router'
import { ApiError, api, errorMessage } from '../../lib/api'
import { Alert } from '../../ui/Alert'
import { Button } from '../../ui/Button'
import { Modal, ModalHead } from '../../ui/Modal'
import overlayStyles from '../../ui/overlay.module.css'
import { ProfileRefiner } from '../onboarding/ProfileRefiner'
import { ProfileStep } from '../onboarding/ProfileStep'
import type { ProfileDocument, ProfileGroup } from '../onboarding/api'
import { StepModal } from './StepModal'
import styles from './account.module.css'

interface StoredProfile {
  profile: ProfileDocument
  dossierVersion: number | null
}

const split = (text: string) =>
  text
    .split(',')
    .map((t) => t.trim())
    .filter(Boolean)

type Tool = 'refine' | 'edit'

/**
 * The score profile on its own page, like the Dossier's: what it is, plainly, and the ways to change it.
 * Editing by hand turns the groups themselves into fields, in place. Adjusting with the AI opens a modal
 * that shows what would change and its effect on the jobs before saving.
 */
export function ScoreProfilePage() {
  const queryClient = useQueryClient()
  const stored = useQuery({ queryKey: ['score-profile'], queryFn: ({ signal }) => api.get<StoredProfile>('/score-profile', signal) })
  const [tool, setTool] = useState<Tool | null>(null)
  const [generating, setGenerating] = useState(false)
  const [preview, setPreview] = useState<ProfileDocument | null>(null)
  const onDraft = useCallback((profile: ProfileDocument | null) => setPreview(profile), [])

  const open = (next: Tool | null) => {
    setTool(next)
    setPreview(null)
  }

  const profile = preview ?? stored.data?.profile
  const termCount = profile?.groups.reduce((total, group) => total + group.terms.length, 0) ?? 0

  return (
    <div className={styles.dossierPage}>
      <Link to="/configuracoes" className={styles.back}>
        <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.75" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
          <path d="M15 6l-6 6 6 6" />
        </svg>
        Configurações
      </Link>
      <div className={styles.dossierHead}>
        <div className={styles.heading}>
          <h1 className={styles.title}>Seu perfil de score</h1>
          <p className={styles.lead}>
            {stored.data
              ? `${stored.data.dossierVersion ? `Feito a partir do Dossier versão ${stored.data.dossierVersion}. ` : ''}Cada termo encontrado numa vaga soma ou tira os pontos do grupo.`
              : 'Carregando'}
          </p>
        </div>
        {stored.data && tool !== 'edit' && (
          <div className={styles.actions}>
            <Button onClick={() => open('refine')}>Ajustar com IA</Button>
            <Button variant="secondary" onClick={() => open('edit')}>
              Editar à mão
            </Button>
            <Button variant="secondary" onClick={() => setGenerating(true)}>
              Gerar de novo
            </Button>
          </div>
        )}
      </div>
      {stored.isError && <Alert>{errorMessage(stored.error)}</Alert>}

      {stored.data && profile && (
        <section className={styles.profileCard} aria-labelledby="profile-view-title">
          <div className={styles.profileCardHead}>
            <h2 id="profile-view-title" className={styles.cardTitle}>
              {tool === 'edit' ? 'Editando os grupos' : 'Grupos de termos'}
            </h2>
            {preview ? (
              <span className={styles.unsaved}>Não salvo</span>
            ) : (
              <span className={styles.profileSummary}>
                {profile.groups.length} grupos · {termCount} termos · destaque a partir de <span className={styles.score}>{profile.minHighlightScore}</span>
              </span>
            )}
          </div>
          {tool === 'edit' ? (
            <ProfileForm key={JSON.stringify(stored.data.profile)} initial={stored.data} onDraft={onDraft} onDone={() => open(null)} />
          ) : (
            <ProfileView profile={stored.data.profile} />
          )}
        </section>
      )}

      {tool === 'refine' && stored.data && (
        <Modal titleId="profile-refine-title" onClose={() => open(null)}>
          <div className={overlayStyles.form}>
            <ModalHead
              id="profile-refine-title"
              title="Ajustar com IA"
              text="Diga o que mudar com suas palavras. Você vê cada mudança e o efeito nas vagas antes de salvar."
              onClose={() => open(null)}
            />
            <ProfileRefiner
              profile={stored.data.profile}
              acceptLabel="Aplicar e salvar"
              onAccept={async (revised) => {
                await api.put('/score-profile', { profile: revised, dossierVersion: stored.data.dossierVersion })
                await queryClient.invalidateQueries()
                open(null)
              }}
            />
          </div>
        </Modal>
      )}

      {generating && (
        <StepModal label="Gerar perfil de score com IA" wide onClose={() => setGenerating(false)}>
          <ProfileStep
            onDone={async () => {
              await queryClient.invalidateQueries()
              setGenerating(false)
            }}
            onBack={() => setGenerating(false)}
          />
        </StepModal>
      )}
    </div>
  )
}

/** One row per group: name, weight, and its terms in one line; then the Gupy search terms. */
function ProfileView({ profile }: { profile: ProfileDocument }) {
  return (
    <div className={styles.profileView}>
      <ul className={styles.profileGroups}>
        {profile.groups.map((group, index) => (
          <li key={`${group.name}-${index}`} className={styles.profileGroup}>
            <div className={styles.profileGroupHead}>
              <span className={styles.rowTitle}>
                {group.name}
                {group.stack && <span className={styles.flag}>núcleo</span>}
                {group.bodyDampened && <span className={styles.flag}>metade na descrição</span>}
              </span>
              <span className={group.weight < 0 ? `${styles.score} ${styles.negative}` : styles.score}>{group.weight > 0 ? `+${group.weight}` : group.weight}</span>
            </div>
            <p className={styles.termLine}>{group.terms.join(', ')}</p>
          </li>
        ))}
      </ul>
      <div className={styles.profileSearch}>
        <span className={styles.flagLabel}>Busca na Gupy</span>
        <p className={styles.termLine}>{profile.searchTerms.join(', ')}</p>
      </div>
    </div>
  )
}

/** Hand edits (FR-SCO-02), in place of the groups. A change is reported as a draft, which marks the card as not saved. */
function ProfileForm({ initial, onDraft, onDone }: { initial: StoredProfile; onDraft: (profile: ProfileDocument | null) => void; onDone: () => void }) {
  const queryClient = useQueryClient()
  const [groups, setGroups] = useState(initial.profile.groups.map((g) => ({ ...g, termsText: g.terms.join(', ') })))
  const [minHighlight, setMinHighlight] = useState(initial.profile.minHighlightScore)
  const [searchTerms, setSearchTerms] = useState(initial.profile.searchTerms.join(', '))
  const draft: ProfileDocument = {
    groups: groups.map(({ termsText, ...g }) => ({ ...g, terms: split(termsText) })),
    minHighlightScore: minHighlight,
    yearsBands: initial.profile.yearsBands,
    searchTerms: split(searchTerms),
  }
  const changed = JSON.stringify(draft) !== JSON.stringify(initial.profile)
  const draftKey = changed ? JSON.stringify(draft) : null

  useEffect(() => {
    onDraft(draftKey ? (JSON.parse(draftKey) as ProfileDocument) : null)
  }, [draftKey, onDraft])

  const save = useMutation({
    mutationFn: () => api.put('/score-profile', { profile: draft, dossierVersion: initial.dossierVersion }),
    onSuccess: async () => {
      await queryClient.invalidateQueries()
      onDone()
    },
  })
  const errors = save.error instanceof ApiError ? Object.values(save.error.problem.errors ?? {}).flat() : []

  const update = (index: number, patch: Partial<ProfileGroup & { termsText: string }>) => setGroups((current) => current.map((g, i) => (i === index ? { ...g, ...patch } : g)))

  const submit = (event: FormEvent) => {
    event.preventDefault()
    save.mutate()
  }

  return (
    <form onSubmit={submit} className={styles.profileForm} noValidate>
      {groups.map((group, index) => (
        <fieldset key={index} className={styles.group}>
          <legend className={styles.srOnly}>Grupo {index + 1}</legend>
          <div className={styles.groupHead}>
            <input className={styles.input} aria-label="Nome do grupo" value={group.name} onChange={(e) => update(index, { name: e.target.value })} maxLength={60} />
            <input
              className={`${styles.input} ${styles.weight}`}
              aria-label="Peso"
              type="number"
              value={group.weight}
              onChange={(e) => update(index, { weight: Number(e.target.value) })}
            />
            <button type="button" className={styles.linkButton} onClick={() => setGroups((current) => current.filter((_, i) => i !== index))}>
              Remover
            </button>
          </div>
          <textarea
            className={styles.textarea}
            aria-label="Termos separados por vírgula"
            value={group.termsText}
            onChange={(e) => update(index, { termsText: e.target.value })}
            rows={2}
          />
          <div className={styles.checks}>
            <label>
              <input type="checkbox" checked={group.stack} onChange={(e) => update(index, { stack: e.target.checked })} /> Núcleo (conta para destaque)
            </label>
            <label>
              <input type="checkbox" checked={group.bodyDampened} onChange={(e) => update(index, { bodyDampened: e.target.checked })} /> Vale metade só na descrição
            </label>
          </div>
        </fieldset>
      ))}
      <Button variant="ghost" onClick={() => setGroups((current) => [...current, { name: 'Novo grupo', weight: 8, terms: [], termsText: '', stack: false, bodyDampened: false }])}>
        Adicionar grupo
      </Button>
      <label className={styles.inline}>
        <span className={styles.rowTitle}>Score mínimo para destaque</span>
        <input className={`${styles.input} ${styles.weight}`} type="number" value={minHighlight} onChange={(e) => setMinHighlight(Number(e.target.value))} />
      </label>
      <label className={styles.stack}>
        <span className={styles.rowTitle}>Termos de busca (Gupy), separados por vírgula</span>
        <textarea className={styles.textarea} value={searchTerms} onChange={(e) => setSearchTerms(e.target.value)} rows={3} />
      </label>
      {errors.length > 0 && <Alert>{errors.join(' ')}</Alert>}
      {save.isError && errors.length === 0 && <Alert>{errorMessage(save.error)}</Alert>}
      <div className={styles.actions}>
        <Button type="submit" disabled={save.isPending || !changed}>
          {save.isPending ? 'Salvando e recalculando...' : 'Salvar'}
        </Button>
        <Button variant="ghost" onClick={onDone} disabled={save.isPending}>
          Cancelar
        </Button>
      </div>
    </form>
  )
}
