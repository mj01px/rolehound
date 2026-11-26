import { useState, type FormEvent } from 'react'
import { ApiError, errorMessage } from '../../../lib/api'
import { Alert } from '../../../ui/Alert'
import { Button } from '../../../ui/Button'
import { Field } from '../../../ui/Field'
import { Modal, ModalHead } from '../../../ui/Modal'
import { useCreateUser, useUpdateUser, type AdminUser, type Role, type UserDraft } from './api'
import styles from './users.module.css'

const USERNAME_HELP = 'Usado para entrar. De 3 a 32 caracteres: minúsculas, números, ponto, hífen ou sublinhado.'

function DraftFields({
  draft,
  onChange,
  error,
  currentEmail,
  emailLocked = false,
}: {
  draft: UserDraft
  onChange: (draft: UserDraft) => void
  error: ApiError | null
  /** When editing: the masked address on file; the field then takes a new one, empty keeps it. */
  currentEmail?: string
  /** The account is in use: only the person changes its email, from their profile. */
  emailLocked?: boolean
}) {
  return (
    <>
      <Field
        label="Nome"
        value={draft.name}
        onChange={(e) => onChange({ ...draft, name: e.target.value })}
        placeholder="Nome da pessoa"
        maxLength={100}
        error={error?.field('name')}
        autoFocus
      />
      {emailLocked ? (
        <p className={styles.noteQuiet}>E-mail: {currentEmail}. Numa conta em uso, só a própria pessoa troca o e-mail, pelo perfil.</p>
      ) : (
        <Field
          label={currentEmail ? 'Novo e-mail' : 'E-mail'}
          type="email"
          value={draft.email}
          onChange={(e) => onChange({ ...draft, email: e.target.value })}
          placeholder="usuario@email.com"
          help={currentEmail ? `Atual: ${currentEmail}. Deixe em branco para manter.` : undefined}
          error={error?.field('email')}
        />
      )}
      <Field
        label="Usuário"
        value={draft.username}
        onChange={(e) => onChange({ ...draft, username: e.target.value.toLowerCase() })}
        placeholder="usuario"
        className={styles.monoInput}
        help={USERNAME_HELP}
        error={error?.field('username')}
      />
    </>
  )
}

/** Errors that belong to no field (the cancelled password check is not one). */
function FormError({ error }: { error: Error | null }) {
  if (!error || error.message === 'cancelled') return null
  if (error instanceof ApiError && (error.field('name') || error.field('email') || error.field('username') || error.field('role'))) return null
  return <Alert>{errorMessage(error)}</Alert>
}

export function NewUserDialog({ onClose, onCreated }: { onClose: () => void; onCreated: (user: AdminUser) => void }) {
  const create = useCreateUser()
  const [draft, setDraft] = useState<UserDraft>({
    name: '',
    email: '',
    username: '',
  })
  const [role, setRole] = useState<Role>('user')
  const error = create.error instanceof ApiError ? create.error : null

  const submit = (event: FormEvent) => {
    event.preventDefault()
    create.mutate({ ...draft, role }, { onSuccess: onCreated })
  }

  return (
    <Modal titleId="new-user-title" onClose={onClose}>
      <form className={styles.dialogForm} onSubmit={submit} noValidate>
        <ModalHead id="new-user-title" title="Novo usuário" text="A pessoa recebe por e-mail um link para criar a senha. O link vale por 72 horas." onClose={onClose} />
        <DraftFields draft={draft} onChange={setDraft} error={error} />
        <fieldset className={styles.roles}>
          <legend className={styles.legend}>Papel</legend>
          <div className={styles.roleGrid}>
            <RoleOption value="user" current={role} onPick={setRole} title="Usuário" text="Usa o app com a própria chave de IA." />
            <RoleOption value="admin" current={role} onPick={setRole} title="Admin" text="Também gerencia usuários, fontes e modelos." />
          </div>
        </fieldset>
        <FormError error={create.error} />
        <div className={styles.dialogActions}>
          <Button variant="secondary" onClick={onClose}>
            Cancelar
          </Button>
          <Button type="submit" disabled={create.isPending || !draft.name.trim() || !draft.email.trim() || !draft.username.trim()}>
            Criar e enviar convite
          </Button>
        </div>
      </form>
    </Modal>
  )
}

function RoleOption({ value, current, onPick, title, text }: { value: Role; current: Role; onPick: (role: Role) => void; title: string; text: string }) {
  return (
    <label className={value === current ? `${styles.roleOption} ${styles.roleOptionOn}` : styles.roleOption}>
      <input type="radio" name="role" value={value} checked={value === current} onChange={() => onPick(value)} className={styles.radio} />
      <span className={styles.roleText}>
        <span className={styles.roleTitle}>{title}</span>
        <span className={styles.roleHint}>{text}</span>
      </span>
    </label>
  )
}

export function EditUserDialog({ user, onClose }: { user: AdminUser; onClose: () => void }) {
  const update = useUpdateUser(user.id)
  // The admin only sees the email masked, so the field starts empty and asks for a new one.
  const [draft, setDraft] = useState<UserDraft>({
    name: user.name,
    email: '',
    username: user.username,
  })
  const error = update.error instanceof ApiError ? update.error : null
  // Changing the email of an account in use would let an admin reset its password and take it over.
  const emailLocked = user.status !== 'pending'
  const emailChanged = !emailLocked && draft.email.trim() !== ''

  const submit = (event: FormEvent) => {
    event.preventDefault()
    update.mutate(draft, { onSuccess: onClose })
  }

  return (
    <Modal titleId="edit-user-title" onClose={onClose}>
      <form className={styles.dialogForm} onSubmit={submit} noValidate>
        <ModalHead id="edit-user-title" title={`Editar ${user.name || user.username}`} text="Papel e acesso ficam no detalhe da pessoa." onClose={onClose} />
        <DraftFields draft={draft} onChange={setDraft} error={error} currentEmail={user.email} emailLocked={emailLocked} />
        {emailChanged && draft.email.includes('@') && (
          <p className={styles.note} role="note">
            O convite atual deixa de valer e um novo vai para <strong>{draft.email.trim()}</strong>.
          </p>
        )}
        {draft.username !== user.username && <p className={styles.noteQuiet}>Trocar o usuário muda como a pessoa entra. Avise antes.</p>}
        <FormError error={update.error} />
        <div className={styles.dialogActions}>
          <Button variant="secondary" onClick={onClose}>
            Cancelar
          </Button>
          <Button type="submit" disabled={update.isPending || !draft.name.trim() || !draft.username.trim()}>
            Salvar
          </Button>
        </div>
      </form>
    </Modal>
  )
}

/** Deactivation ends the person's sessions, so it asks once. */
export function DeactivateDialog({ user, pending, onConfirm, onClose }: { user: AdminUser; pending: boolean; onConfirm: () => void; onClose: () => void }) {
  return (
    <Modal titleId="deactivate-title" onClose={onClose}>
      <div className={styles.dialogForm}>
        <ModalHead
          id="deactivate-title"
          title={`Desativar ${user.name || user.username}?`}
          text="A pessoa sai na hora e não consegue mais entrar. Os dados dela ficam guardados e dá para reativar depois."
          onClose={onClose}
        />
        <div className={styles.dialogActions}>
          <Button variant="secondary" onClick={onClose}>
            Cancelar
          </Button>
          <Button variant="danger" onClick={onConfirm} disabled={pending}>
            Desativar acesso
          </Button>
        </div>
      </div>
    </Modal>
  )
}
