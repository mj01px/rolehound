import { useState } from 'react'

export const PASSWORD_HELP = 'Mínimo de 8 caracteres, com uma maiúscula, um número e um caractere especial.'

/** Password plus confirmation. The rules themselves are checked by the API, which explains each failure. */
export function usePasswordForm() {
  const [password, setPassword] = useState('')
  const [confirm, setConfirm] = useState('')
  const [checked, setChecked] = useState(false)

  const mismatch = checked && password !== confirm

  return {
    password,
    confirm,
    setPassword,
    setConfirm,
    mismatch,
    /** Returns true when the form can be sent. */
    validate: () => {
      setChecked(true)
      return password.length > 0 && password === confirm
    },
  }
}
