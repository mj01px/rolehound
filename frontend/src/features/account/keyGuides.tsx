import type { Guide } from '../../ui/HelpGuide'

const link = (href: string, label: string) => (
  <a href={href} target="_blank" rel="noreferrer">
    {label}
  </a>
)

/** Where to get the OpenRouter key the AI features run on. Screenshots in /help, taken on 2026-11-25. */
export const apiKeyGuide: Guide = {
  title: 'Como pegar a chave do OpenRouter',
  intro: 'O OpenRouter dá acesso a vários modelos de IA com uma chave só. Criar a conta é de graça; você paga só o que usar.',
  steps: [
    { text: <>Entre em {link('https://openrouter.ai', 'openrouter.ai')} e crie a sua conta (dá para entrar com o Google).</> },
    {
      text: (
        <>
          Para usar modelos pagos, coloque crédito em {link('https://openrouter.ai/settings/credits', 'Credits')} › <strong>Add credits</strong>. Só para testar, os modelos
          gratuitos funcionam sem crédito.
        </>
      ),
      image: '/help/api-key-0-credits.png',
      alt: 'Página Credits do OpenRouter com o botão Add credits destacado',
    },
    {
      text: (
        <>
          Abra {link('https://openrouter.ai/keys', 'Keys')} e clique em <strong>New key</strong>.
        </>
      ),
      image: '/help/api-key-1-keys.jpg',
      alt: 'Página de chaves do OpenRouter com o botão New key destacado',
    },
    {
      text: (
        <>
          Dê um nome, como "Rolehound", escolha a validade e, se quiser, um limite de gasto para a chave. Clique em <strong>Create</strong>.
        </>
      ),
      image: '/help/api-key-2-create.png',
      alt: 'Formulário Create API key com nome, validade, limite de crédito e o botão Create',
    },
    {
      text: (
        <>
          Copie a chave, que começa com <strong>sk-or-v1-</strong>, e cole aqui. Ela só aparece uma vez. O Rolehound testa antes de salvar e guarda criptografada.
        </>
      ),
    },
  ],
}

/** Where to get the optional management key that only reads the account balance. */
export const managementKeyGuide: Guide = {
  title: 'Como pegar a chave de gerenciamento',
  intro: 'Opcional. Serve só para mostrar o saldo da sua conta do OpenRouter no resumo de Configurações. Ela não roda modelos.',
  steps: [
    {
      text: (
        <>
          No OpenRouter, abra {link('https://openrouter.ai/settings/management-keys', 'Management keys')} e clique em <strong>New key</strong>.
        </>
      ),
      image: '/help/management-key-1-list.png',
      alt: 'Página Management keys do OpenRouter com o botão New key destacado',
    },
    {
      text: (
        <>
          Dê um nome e escolha uma <strong>validade</strong>. Essa chave tem mais poder que a outra (pode criar e apagar chaves), então prefira uma validade curta e troque quando
          vencer. Clique em <strong>Create</strong>.
        </>
      ),
      image: '/help/management-key-2-create.png',
      alt: 'Formulário Create a Management Key com nome e validade destacada',
    },
    { text: 'Copie a chave e cole aqui. O Rolehound pede a sua senha de novo, guarda criptografada e usa só para ler o saldo.' },
  ],
}
