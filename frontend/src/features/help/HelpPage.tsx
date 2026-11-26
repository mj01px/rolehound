import { useEffect, useMemo, useRef, useState, type ReactNode } from 'react'
import { Link } from 'react-router'
import { Logo } from '../../ui/Logo'
import { useMe } from '../auth/api'
import styles from './help.module.css'

interface Question {
  q: string
  a: ReactNode
  /** Plain words the search matches besides the question. */
  keywords?: string
}

interface Topic {
  id: string
  title: string
  /** What the topic covers, on its card. */
  summary: string
  icon: ReactNode
  questions: Question[]
}

const CONTACT_EMAIL = 'rolehound.app@gmail.com'
const YEAR = new Date().getFullYear()

const TOPICS: Topic[] = [
  {
    id: 'comecando',
    title: 'Começando',
    summary: 'Convite, primeiro acesso, senha',
    icon: (
      <TopicIcon>
        <path d="M5 12h14M13 6l6 6-6 6" />
      </TopicIcon>
    ),
    questions: [
      {
        q: 'O que é o rolehound?',
        a: 'Um caçador de vagas: junta vagas do Brasil de várias fontes, dá a cada uma uma nota de acordo com o seu perfil e organiza suas candidaturas num funil só. As funções de IA ajudam a se candidatar sem inventar nada sobre você.',
      },
      {
        q: 'Como alguém ganha acesso?',
        a: 'Só por convite. Um administrador cria a conta e a pessoa recebe um link por e-mail para definir a senha. O link vale por 72 horas.',
        keywords: 'convite cadastro conta',
      },
      {
        q: 'Esqueci minha senha. E agora?',
        a: (
          <>
            Na tela de entrada, use <Link to="/esqueci-senha">Esqueci a senha</Link>. O link enviado vale por 1 hora e só funciona uma vez. Para proteger a conta, mandamos no
            máximo 3 desses e-mails por hora.
          </>
        ),
        keywords: 'senha redefinir recuperar',
      },
    ],
  },
  {
    id: 'vagas',
    title: 'Vagas e nota',
    summary: 'Fontes, coletas, nota, destaques',
    icon: (
      <TopicIcon>
        <rect x="3" y="7" width="18" height="13" rx="2" />
        <path d="M8 7V5a2 2 0 0 1 2-2h4a2 2 0 0 1 2 2v2M3 13h18" />
      </TopicIcon>
    ),
    questions: [
      {
        q: 'De onde vêm as vagas?',
        a: 'De Gupy, InHire, Greenhouse, Lever, Workday, repositórios de vagas no GitHub e sites de carreira que publicam as vagas no formato que o Google lê. Você também pode adicionar uma vaga por link.',
        keywords: 'fontes gupy linkedin',
      },
      {
        q: 'Quando a lista é atualizada?',
        a: 'Às 9h, 12h, 15h e 18h, no horário de Brasília. Se uma coleta não acontecer, ela roda na próxima oportunidade.',
        keywords: 'coleta horário atualizar',
      },
      {
        q: 'Como a nota de cada vaga é calculada?',
        a: 'Seu perfil de score tem grupos de termos, cada um com um peso. Cada termo encontrado na vaga soma (ou tira) os pontos do grupo, e no título conta mais. Vagas que pedem mais anos de experiência do que o seu perfil prevê perdem pontos.',
        keywords: 'score pontos nota perfil',
      },
      {
        q: 'O que é um destaque?',
        a: 'Uma vaga com nota a partir do mínimo do seu perfil e que bate com pelo menos um grupo marcado como núcleo. Destaques novos aparecem nas notificações e, se você conectar, no Telegram.',
      },
      {
        q: 'Como mudo o que conta na nota?',
        a: (
          <>
            Em <Link to="/configuracoes/perfil">Configurações › Perfil de score</Link>. Dá para pedir um ajuste à IA com suas palavras ou editar os grupos à mão. Ao salvar, a nota
            de todas as vagas abertas é recalculada.
          </>
        ),
        keywords: 'ajustar editar perfil termos',
      },
      {
        q: 'Achei uma vaga que não está no rolehound.',
        a: 'Na tela de vagas, use o botão "+" para adicionar por link. Se a fonte tiver conector, a vaga é lida de lá; se não (LinkedIn, por exemplo), cole o título e o texto. Vagas coladas são só suas.',
        keywords: 'link linkedin adicionar colar',
      },
    ],
  },
  {
    id: 'empresas',
    title: 'Empresas e funil',
    summary: 'Seguir empresas, candidaturas',
    icon: (
      <TopicIcon>
        <path d="M4 20V8l6-4v16M10 20h10V10l-10-2M14 13h2M14 16h2" />
      </TopicIcon>
    ),
    questions: [
      {
        q: 'Como acompanho uma empresa?',
        a: (
          <>
            Em <Link to="/empresas">Empresas</Link>, cole o link da página de vagas dela e use "Seguir empresa". Quando surgir vaga dela que combina com você, aparece nas
            notificações. Cada pessoa pode adicionar até 30 sites.
          </>
        ),
        keywords: 'seguir empresa site carreira',
      },
      {
        q: 'O site da empresa não é aceito.',
        a: 'Se a empresa usa uma plataforma que o rolehound ainda não lê, o pedido fica anotado para o administrador, e você é avisado quando ela passar a ser lida. Sites que pedem login não podem ser acompanhados.',
      },
      {
        q: 'Para que serve o funil?',
        a: (
          <>
            Para acompanhar cada candidatura: status, conversas, próximo passo e data. Um follow-up atrasado vira notificação. Fica em <Link to="/funil">Funil</Link>.
          </>
        ),
        keywords: 'candidatura follow-up status',
      },
    ],
  },
  {
    id: 'ia',
    title: 'IA',
    summary: 'Chave, custo, limites, currículo',
    icon: (
      <TopicIcon>
        <path d="M12 3l1.8 4.7L18.5 9.5l-4.7 1.8L12 16l-1.8-4.7L5.5 9.5l4.7-1.8z" />
        <path d="M19 15l.8 2.2L22 18l-2.2.8L19 21l-.8-2.2L16 18l2.2-.8z" />
      </TopicIcon>
    ),
    questions: [
      {
        q: 'Preciso pagar pela IA?',
        a: 'A IA usa a sua própria chave do OpenRouter, então o custo, quando há, é cobrado na sua conta de lá. Você escolhe os modelos e pode usar modelos gratuitos. Atenção: modelos gratuitos podem usar o que você envia para treino.',
        keywords: 'openrouter chave custo gratuito',
      },
      {
        q: 'O que a IA faz aqui?',
        a: 'Escreve o "fale sobre você" para uma vaga, analisa seu currículo em PDF contra a vaga como um recrutador (ATS), monta um currículo sob medida e propõe ou ajusta seu perfil de score. Nada roda sozinho: só quando você pede.',
        keywords: 'pitch ats currículo',
      },
      {
        q: 'O currículo sob medida inventa coisas?',
        a: 'Não. Empresas, cargos, datas e formação vêm do seu Dossier, e cada habilidade citada tem que estar nele. O que foi reescrito aparece ao lado do original para você conferir, e números, links e e-mails que não estão no Dossier são apontados.',
      },
      {
        q: 'Existe algum limite?',
        a: '30 pedidos de IA por hora por pessoa, e um currículo sob medida sendo gerado por vez.',
        keywords: 'limite hora',
      },
      {
        q: 'Minha chave do OpenRouter fica segura?',
        a: 'Ela é guardada criptografada no servidor e nunca volta para o navegador. Você pode trocar ou remover quando quiser em Configurações.',
      },
    ],
  },
  {
    id: 'dossier',
    title: 'Dossier',
    summary: 'O que é, como atualizar',
    icon: (
      <TopicIcon>
        <path d="M7 3h7l5 5v13H7z" />
        <path d="M14 3v5h5M10 13h6M10 17h6" />
      </TopicIcon>
    ),
    questions: [
      {
        q: 'O que é o Dossier?',
        a: 'Um arquivo JSON com sua trajetória: experiências, projetos, formação, habilidades e preferências. É a única fonte de verdade sobre você no rolehound. Ele é montado fora do app, numa IA como o ChatGPT, com o prompt que o rolehound fornece.',
        keywords: 'json trajetória',
      },
      {
        q: 'Como atualizo meu Dossier?',
        a: (
          <>
            Em <Link to="/configuracoes/dossier">Configurações › Dossier</Link>, use "Importar nova versão". Depois, o rolehound pergunta se você quer gerar um perfil de score novo
            a partir dele; o atual só muda se você escolher.
          </>
        ),
        keywords: 'importar versão',
      },
    ],
  },
  {
    id: 'notificacoes',
    title: 'Notificações e Telegram',
    summary: 'Avisos e Telegram',
    icon: (
      <TopicIcon>
        <path d="M6 9a6 6 0 0 1 12 0c0 6 2.5 8 2.5 8h-17S6 15 6 9Z" />
        <path d="M10.3 20a1.9 1.9 0 0 0 3.4 0" />
      </TopicIcon>
    ),
    questions: [
      {
        q: 'O que aparece nas notificações?',
        a: 'Destaques novos, vagas novas das empresas que você segue, follow-ups atrasados e avisos sobre suas fontes e seus modelos de IA.',
      },
      {
        q: 'Como recebo avisos no Telegram?',
        a: (
          <>
            Em <Link to="/configuracoes">Configurações › Telegram</Link>, gere o link de conexão (vale por 10 minutos) e abra no Telegram. Para parar, envie /stop no chat. Trocar a
            senha ou sair de todos os aparelhos desconecta o Telegram por segurança.
          </>
        ),
        keywords: 'telegram bot avisos',
      },
    ],
  },
  {
    id: 'conta',
    title: 'Conta e privacidade',
    summary: 'Segurança, seus dados, contato',
    icon: (
      <TopicIcon>
        <path d="M12 3l7 3v5c0 4.5-3 8.5-7 10-4-1.5-7-5.5-7-10V6z" />
        <path d="M9 12l2 2 4-4" />
      </TopicIcon>
    ),
    questions: [
      {
        q: 'Como protejo minha conta?',
        a: (
          <>
            Ative a verificação em duas etapas em <Link to="/perfil">Perfil › Segurança</Link>, com um app autenticador ou com código por e-mail. Lá também dá para sair de todos os
            aparelhos.
          </>
        ),
        keywords: '2fa duas etapas segurança sessões',
      },
      {
        q: 'Posso baixar ou apagar meus dados?',
        a: (
          <>
            Sim. Em <Link to="/configuracoes">Configurações › Seus dados</Link> você exporta tudo em JSON ou apaga a conta de vez. Veja a{' '}
            <Link to="/privacidade">política de privacidade</Link>.
          </>
        ),
        keywords: 'lgpd exportar apagar excluir',
      },
      {
        q: 'Não achei minha resposta.',
        a: (
          <>
            Escreva para <a href={`mailto:${CONTACT_EMAIL}`}>{CONTACT_EMAIL}</a>.
          </>
        ),
        keywords: 'contato suporte',
      },
    ],
  },
]

const fold = (text: string) => text.normalize('NFD').replace(/[̀-ͯ]/g, '').toLowerCase()

/** Plain text of an answer, for the search. */
function textOf(node: ReactNode): string {
  if (node === null || node === undefined || typeof node === 'boolean') return ''
  if (typeof node === 'string' || typeof node === 'number') return String(node)
  if (Array.isArray(node)) return node.map(textOf).join(' ')
  if (typeof node === 'object' && 'props' in node) return textOf((node.props as { children?: ReactNode }).children)
  return ''
}

function TopicIcon({ children }: { children: ReactNode }) {
  return (
    <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.5" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
      {children}
    </svg>
  )
}

const plural = (count: number) => (count === 1 ? '1 pergunta' : `${count} perguntas`)

/**
 * The help center: public, outside the app's layout, so people who cannot sign in reach it too
 * (from the sign-in screens' footer). Topic cards, then every question by topic, searchable;
 * "/" focuses the search.
 */
export function HelpPage() {
  const signedIn = Boolean(useMe().data)
  const search = useRef<HTMLInputElement>(null)
  const [query, setQuery] = useState('')
  const searching = query.trim().length > 0

  useEffect(() => {
    const onKey = (event: KeyboardEvent) => {
      const typing = event.target instanceof HTMLElement && ['INPUT', 'TEXTAREA'].includes(event.target.tagName)
      if (event.key === '/' && !typing) {
        event.preventDefault()
        search.current?.focus()
      }
    }
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [])

  const topics = useMemo(() => {
    const words = fold(query).split(/\s+/).filter(Boolean)
    if (words.length === 0) return TOPICS
    return TOPICS.map((topic) => ({
      ...topic,
      questions: topic.questions.filter((item) => {
        const haystack = fold(`${item.q} ${textOf(item.a)} ${item.keywords ?? ''}`)
        return words.every((word) => haystack.includes(word))
      }),
    })).filter((topic) => topic.questions.length > 0)
  }, [query])

  return (
    <div className={styles.page}>
      <header className={styles.bar}>
        <div className={styles.barInner}>
          <Link to={signedIn ? '/' : '/entrar'} className={styles.brand}>
            <Logo size={22} label="" />
            <span>rolehound</span>
            <span className={styles.crumb}>Ajuda</span>
          </Link>
          <Link to={signedIn ? '/' : '/entrar'} className={styles.back}>
            <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.75" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
              <path d="M15 6l-6 6 6 6" />
            </svg>
            {signedIn ? 'Voltar para o app' : 'Entrar'}
          </Link>
        </div>
      </header>

      <main className={styles.main}>
        <section className={styles.hero}>
          <h1 className={styles.title}>Como podemos ajudar?</h1>
          <p className={styles.lead}>Respostas rápidas sobre vagas, nota, IA, Dossier e a sua conta.</p>
          <label className={styles.search}>
            <span className={styles.srOnly}>Procurar nas perguntas</span>
            <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.5" strokeLinecap="round" aria-hidden="true">
              <circle cx="11" cy="11" r="7" />
              <path d="M20 20l-3.5-3.5" />
            </svg>
            <input ref={search} type="search" value={query} onChange={(event) => setQuery(event.target.value)} placeholder="Procure por senha, Telegram, nota..." />
            <kbd className={styles.kbd} aria-hidden="true">
              /
            </kbd>
          </label>
        </section>

        {!searching && (
          <nav aria-label="Temas" className={styles.cards}>
            {TOPICS.map((topic) => (
              <a key={topic.id} href={`#${topic.id}`} className={styles.card}>
                <span className={styles.cardIcon}>{topic.icon}</span>
                <span className={styles.cardText}>
                  <span className={styles.cardTitle}>{topic.title}</span>
                  <span className={styles.cardSummary}>{topic.summary}</span>
                </span>
                <span className={styles.cardCount}>{plural(topic.questions.length)}</span>
              </a>
            ))}
            <a href={`mailto:${CONTACT_EMAIL}`} className={`${styles.card} ${styles.cardContact}`}>
              <span className={styles.cardIcon}>
                <TopicIcon>
                  <rect x="3" y="5" width="18" height="14" rx="2" />
                  <path d="M3 7l9 6 9-6" />
                </TopicIcon>
              </span>
              <span className={styles.cardText}>
                <span className={styles.cardTitle}>Falar com a gente</span>
                <span className={styles.cardSummary}>{CONTACT_EMAIL}</span>
              </span>
            </a>
          </nav>
        )}

        <div className={styles.topics}>
          {topics.length === 0 ? (
            <p className={styles.empty}>
              Nada encontrado para "{query}". Tente outra palavra ou escreva para <a href={`mailto:${CONTACT_EMAIL}`}>{CONTACT_EMAIL}</a>.
            </p>
          ) : (
            topics.map((topic) => (
              <section key={topic.id} id={topic.id} className={styles.topic} aria-labelledby={`${topic.id}-title`}>
                <h2 id={`${topic.id}-title`} className={styles.topicTitle}>
                  {topic.title}
                </h2>
                <div className={styles.list}>
                  {topic.questions.map((item) => (
                    <details key={item.q} className={styles.item} open={searching || undefined}>
                      <summary className={styles.question}>
                        <span>{item.q}</span>
                        <svg
                          className={styles.chevron}
                          width="16"
                          height="16"
                          viewBox="0 0 24 24"
                          fill="none"
                          stroke="currentColor"
                          strokeWidth="1.5"
                          strokeLinecap="round"
                          aria-hidden="true"
                        >
                          <path d="M6 9l6 6 6-6" />
                        </svg>
                      </summary>
                      <div className={styles.answer}>{item.a}</div>
                    </details>
                  ))}
                </div>
              </section>
            ))
          )}

          <section className={styles.contact}>
            <div className={styles.contactText}>
              <h2 className={styles.contactTitle}>Não achou o que procurava?</h2>
              <p>Escreva para a gente. A resposta chega no seu e-mail.</p>
            </div>
            <a href={`mailto:${CONTACT_EMAIL}`} className={styles.contactButton}>
              Enviar e-mail
            </a>
          </section>
        </div>
      </main>

      <footer className={styles.footer}>
        <div className={styles.footerInner}>
          <span>© {YEAR} rolehound</span>
          <span className={styles.footerLinks}>
            <Link to="/termos">Termos de uso</Link>
            <Link to="/privacidade">Política de privacidade</Link>
          </span>
        </div>
      </footer>
    </div>
  )
}
