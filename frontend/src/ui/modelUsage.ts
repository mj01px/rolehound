/**
 * A typical use of each AI function, in tokens, to show prices as "about
 * US$ 0.01 per resume" instead of per million tokens. Rough sizes from an
 * average job description (~3,400 characters) and a typical Dossier; for
 * Pitch and ATS the heavier of the two (ATS) is used.
 */
export interface ModelUsage {
  /** The unit, as read after "por": "currículo", "análise". */
  unit: string
  promptTokens: number
  completionTokens: number
}

export const MODEL_USAGE = {
  pitchAndAts: { unit: 'análise', promptTokens: 5_000, completionTokens: 1_000 },
  resume: { unit: 'currículo', promptTokens: 8_000, completionTokens: 3_000 },
  profileAndFix: { unit: 'perfil gerado', promptTokens: 6_000, completionTokens: 4_000 },
} satisfies Record<string, ModelUsage>
