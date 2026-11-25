export type OnboardingStep = 'key' | 'models' | 'dossier' | 'profile' | 'scoring' | 'done'

/** The five screens, in order (the password was set with the access link). */
export const STEPS: Exclude<OnboardingStep, 'done'>[] = ['key', 'models', 'dossier', 'profile', 'scoring']

export const onboardingKey = ['onboarding'] as const

export interface Models {
  pitchAndAts: string
  resume: string
  profileAndFix: string
}

export interface AiSettings {
  hasKey: boolean
  keyHint: string | null
  models: Models | null
  suggested: Models
  /** Last four characters of the optional management key, which only reads the account balance. */
  managementKeyHint: string | null
}

export interface DossierError {
  path: string
  message: string
}

export interface ProfileGroup {
  name: string
  weight: number
  terms: string[]
  stack: boolean
  bodyDampened: boolean
}

export interface ProfileDocument {
  groups: ProfileGroup[]
  minHighlightScore: number
  yearsBands: { minYears: number; penalty: number }[]
  searchTerms: string[]
}

export interface ProfileOption {
  name: string
  description: string
  profile: ProfileDocument
}
