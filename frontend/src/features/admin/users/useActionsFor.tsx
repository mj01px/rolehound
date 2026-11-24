import { useState } from 'react'
import { ApiError } from '../../../lib/api'
import { useMe } from '../../auth/api'
import { useUserAction, type AdminUser, type UserAction } from './api'
import { DeactivateDialog } from './UserDialog'

/** What an admin can do to an account; nobody acts on their own role or access. */
export function useActionsFor(user: AdminUser) {
  const self = useMe().data?.id === user.id
  const action = useUserAction(user)
  const [confirmDeactivate, setConfirmDeactivate] = useState(false)

  const run = (kind: UserAction) => action.mutate(kind)
  const available = {
    resend: user.status === 'pending',
    role: !self && user.status !== 'pending',
    deactivate: !self && user.status !== 'deactivated',
    reactivate: user.status === 'deactivated',
  }

  const dialog = confirmDeactivate && (
    <DeactivateDialog
      user={user}
      pending={action.isPending}
      onClose={() => setConfirmDeactivate(false)}
      onConfirm={() =>
        action.mutate('deactivate', {
          onSuccess: () => setConfirmDeactivate(false),
        })
      }
    />
  )

  const error = action.error instanceof ApiError ? action.error.problem.detail : null
  return {
    available,
    run,
    askDeactivate: () => setConfirmDeactivate(true),
    pending: action.isPending,
    error,
    dialog,
    sent: action.isSuccess && action.variables === 'resend',
  }
}
