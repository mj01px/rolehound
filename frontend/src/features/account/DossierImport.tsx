import { useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import { Button } from '../../ui/Button'
import { Modal, ModalHead } from '../../ui/Modal'
import overlayStyles from '../../ui/overlay.module.css'
import { DossierStep } from '../onboarding/DossierStep'
import { ProfileStep } from '../onboarding/ProfileStep'
import { StepModal } from './StepModal'

/**
 * Importing a new Dossier version, in modals: the import itself, then the offer to have the AI
 * propose a new score profile from it (FR-DOS-07). The current profile changes only if the person picks another.
 */
export function DossierImport({ onClose }: { onClose: () => void }) {
  const queryClient = useQueryClient()
  const [stage, setStage] = useState<'import' | 'ask' | 'profile'>('import')

  if (stage === 'import') {
    return (
      <StepModal label="Importar nova versão do Dossier" wide onClose={onClose}>
        <DossierStep
          onDone={async () => {
            await queryClient.invalidateQueries({ queryKey: ['dossier'] })
            setStage('ask')
          }}
          onBack={onClose}
        />
      </StepModal>
    )
  }

  if (stage === 'ask') {
    return (
      <Modal titleId="dossier-saved-title" onClose={onClose}>
        <div className={overlayStyles.form}>
          <ModalHead
            id="dossier-saved-title"
            title="Dossier salvo como uma nova versão"
            text="Quer que a IA proponha um novo perfil de score com base nele? Seu perfil atual só muda se você escolher outro."
            onClose={onClose}
          />
          <div className={overlayStyles.actions}>
            <Button variant="ghost" onClick={onClose}>
              Manter o atual
            </Button>
            <Button onClick={() => setStage('profile')}>Gerar novo perfil</Button>
          </div>
        </div>
      </Modal>
    )
  }

  return (
    <StepModal label="Gerar perfil de score com IA" wide onClose={onClose}>
      <ProfileStep
        onDone={async () => {
          await queryClient.invalidateQueries()
          onClose()
        }}
        onBack={onClose}
      />
    </StepModal>
  )
}
