import styles from '../account/account.module.css'

/** Placeholder until the jobs slice: the home of signed-in people. */
export function JobsPage() {
  return (
    <div className={styles.page}>
      <h1 className={styles.title}>Vagas</h1>
      <p className={styles.muted}>As vagas aparecem aqui quando a coleta e o onboarding estiverem prontos.</p>
    </div>
  )
}
