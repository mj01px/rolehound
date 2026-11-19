import type { ButtonHTMLAttributes } from 'react'
import styles from './ui.module.css'

type Variant = 'primary' | 'secondary' | 'ghost' | 'danger'

interface ButtonProps extends ButtonHTMLAttributes<HTMLButtonElement> {
  variant?: Variant
  size?: 'default' | 'large'
  block?: boolean
}

export function Button({ variant = 'primary', size = 'default', block = false, className, type = 'button', ...rest }: ButtonProps) {
  const classes = [styles.button, styles[variant], size === 'large' && styles.large, block && styles.block, className]
    .filter(Boolean)
    .join(' ')
  return <button type={type} className={classes} {...rest} />
}
