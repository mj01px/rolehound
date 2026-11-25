import { Link } from 'react-router'
import { CONTACT, CONTROLLER, LegalLayout } from './LegalLayout'

/** Política de privacidade (LGPD). A change here needs a new Terms.CurrentVersion in the API. */
export function PrivacyPage() {
  return (
    <LegalLayout title="Política de privacidade">
      <p>
        Esta política explica quais dados o Rolehound guarda sobre você, para quê, com quem eles são compartilhados e como você exerce os seus direitos pela Lei Geral de Proteção
        de Dados (Lei 13.709/2018). Leia junto com os <Link to="/termos">termos de uso</Link>.
      </p>

      <h2>1. Quem cuida dos seus dados</h2>
      <p>
        O controlador é {CONTROLLER}, que mantém o Rolehound. Contato para qualquer assunto de privacidade: <a href={`mailto:${CONTACT}`}>{CONTACT}</a>.
      </p>

      <h2>2. Dados que guardamos</h2>
      <ul>
        <li>
          <strong>Conta:</strong> nome, nome de usuário, e-mail, senha (só um hash, nunca a senha em si) e a configuração da verificação em duas etapas.
        </li>
        <li>
          <strong>Dossier:</strong> o arquivo que você importa com a sua trajetória profissional (experiências, formação, habilidades, idiomas e o que mais você incluir).
        </li>
        <li>
          <strong>Perfil de score e preferências:</strong> grupos de termos, pesos e o limite de destaque.
        </li>
        <li>
          <strong>Funil:</strong> vagas em que você se candidatou, etapas, interações, próximos passos e anotações.
        </li>
        <li>
          <strong>Textos gerados:</strong> pitches, análises de currículo e currículos sob medida, e o PDF de currículo que você enviar para análise.
        </li>
        <li>
          <strong>Chaves do OpenRouter:</strong> a chave de IA e, se você cadastrar, a chave de gerenciamento, guardadas criptografadas. Elas nunca voltam para a tela.
        </li>
        <li>
          <strong>Uso de IA:</strong> quantos pedidos e tokens por função e modelo, sem o conteúdo das conversas.
        </li>
        <li>
          <strong>Telegram:</strong> o identificador do chat, se você conectar.
        </li>
        <li>
          <strong>Segurança e uso:</strong> histórico de acessos e mudanças na conta, com o endereço IP, e a data da sua última visita.
        </li>
      </ul>

      <h2>3. Para que usamos</h2>
      <ul>
        <li>Mostrar as vagas que combinam com você, com o score do seu perfil.</li>
        <li>Rodar as funções de IA que você pedir.</li>
        <li>Guardar e acompanhar o seu funil de candidaturas.</li>
        <li>Mandar os e-mails de acesso e os avisos que você ligar (sino e Telegram).</li>
        <li>Proteger a sua conta e investigar acessos suspeitos.</li>
      </ul>
      <p>
        A base legal é a execução do serviço que você pediu ao aceitar estes termos (art. 7º, V da LGPD) e, para o histórico de segurança, o legítimo interesse em proteger as
        contas (art. 7º, IX).
      </p>

      <h2>4. Com quem compartilhamos</h2>
      <ul>
        <li>
          <strong>OpenRouter</strong> e o provedor do modelo que você escolher: só quando você usa uma função de IA. Vai a vaga e só as partes do Dossier que a função precisa.
        </li>
        <li>
          <strong>Telegram:</strong> se você conectar, as mensagens levam título da vaga, empresa, score e links, nunca dados do seu Dossier.
        </li>
        <li>
          <strong>Brevo:</strong> envia os e-mails de acesso, troca de senha e troca de e-mail.
        </li>
        <li>
          <strong>Google Cloud:</strong> hospeda o servidor e o banco de dados, nos Estados Unidos.
        </li>
        <li>
          <strong>Cloudflare:</strong> entrega o site.
        </li>
      </ul>
      <p>
        Por causa desses serviços, os seus dados podem ser tratados fora do Brasil, com as garantias do art. 33 da LGPD. Nenhum dado é vendido nem usado para publicidade. O
        administrador vê os seus dados de conta e números de qualidade do score, nunca o seu Dossier, as suas vagas ou o seu funil.
      </p>

      <h2>5. Por quanto tempo</h2>
      <ul>
        <li>Os dados da conta ficam enquanto a conta existir.</li>
        <li>Ao apagar a conta, tudo é apagado na hora. Cópias de backup do servidor somem em até 7 dias.</li>
        <li>O histórico de segurança é apagado depois de 90 dias. Links e códigos de acesso usados, depois de 7 dias.</li>
      </ul>

      <h2>6. Seus direitos</h2>
      <ul>
        <li>
          <strong>Acesso e portabilidade:</strong> baixe todos os seus dados em Perfil › Seus dados › Exportar.
        </li>
        <li>
          <strong>Correção:</strong> edite o seu perfil, o Dossier e o funil no próprio app.
        </li>
        <li>
          <strong>Eliminação:</strong> apague a sua conta em Perfil › Seus dados › Apagar conta.
        </li>
        <li>
          <strong>Informação, revogação e outros pedidos:</strong> escreva para <a href={`mailto:${CONTACT}`}>{CONTACT}</a>. Respondemos em até 15 dias.
        </li>
        <li>Você também pode reclamar à Autoridade Nacional de Proteção de Dados (ANPD).</li>
      </ul>

      <h2>7. Segurança</h2>
      <p>
        Conexão sempre por HTTPS, senha guardada só como hash, chaves criptografadas, verificação em duas etapas opcional, limites contra tentativas repetidas e histórico de
        acessos para cada conta.
      </p>

      <h2>8. Cookies</h2>
      <p>
        O Rolehound usa só os cookies necessários para manter você conectado com segurança e guarda no seu navegador algumas preferências de tela. Não há cookies de publicidade nem
        de rastreamento.
      </p>

      <h2>9. Mudanças nesta política</h2>
      <p>Quando esta política mudar, o app mostra a nova versão e pede o seu aceite antes de você continuar.</p>
    </LegalLayout>
  )
}
