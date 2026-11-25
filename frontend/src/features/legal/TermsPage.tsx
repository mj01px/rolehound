import { Link } from 'react-router'
import { CONTACT, CONTROLLER, LegalLayout } from './LegalLayout'

/** Termos de uso. A change here needs a new Terms.CurrentVersion in the API, so everyone accepts again. */
export function TermsPage() {
  return (
    <LegalLayout title="Termos de uso">
      <p>
        O Rolehound é um app pessoal, mantido por {CONTROLLER}, que junta vagas de emprego publicadas no Brasil, compara cada uma com o seu perfil e ajuda você a acompanhar as suas
        candidaturas. Ao usar o Rolehound, você concorda com estes termos e com a <Link to="/privacidade">política de privacidade</Link>.
      </p>

      <h2>1. Acesso</h2>
      <ul>
        <li>O acesso é só por convite. Não há cadastro aberto nem cobrança.</li>
        <li>A conta é sua e pessoal. Não compartilhe a sua senha. Recomendamos ligar a verificação em duas etapas no seu perfil.</li>
        <li>Você é responsável pelo que for feito com a sua conta.</li>
      </ul>

      <h2>2. Inteligência artificial e a sua chave</h2>
      <ul>
        <li>As funções de IA (pitch, análise de currículo, currículo sob medida e perfil de score) rodam só quando você pede, com a sua própria chave do OpenRouter.</li>
        <li>O custo dessas chamadas é cobrado pelo OpenRouter na sua conta de lá. O Rolehound não cobra nada e não tem acesso ao seu cartão.</li>
        <li>
          A IA pode errar. O Rolehound confere o currículo gerado contra o seu Dossier para não inventar empresas, cargos, datas ou habilidades, mas revise tudo antes de enviar
          para alguém. O que você envia é responsabilidade sua.
        </li>
        <li>Modelos gratuitos podem usar o que recebem para treinamento. A escolha do modelo é sua.</li>
      </ul>

      <h2>3. Vagas</h2>
      <ul>
        <li>
          As vagas vêm de sites públicos de empresas e plataformas de recrutamento. O Rolehound não é dono delas e não garante que estejam corretas, completas ou ainda abertas.
        </li>
        <li>A candidatura é feita no site da empresa ou da plataforma, com as regras deles.</li>
        <li>O score é uma ajuda para priorizar, não uma promessa de que você combina com a vaga.</li>
      </ul>

      <h2>4. O que não é permitido</h2>
      <ul>
        <li>Tentar acessar dados de outras pessoas ou partes do app que não são suas.</li>
        <li>Automatizar o uso do app, sobrecarregar o servidor ou contornar os limites de uso.</li>
        <li>Usar o app para algo ilegal ou para prejudicar as empresas e plataformas de onde vêm as vagas.</li>
      </ul>

      <h2>5. Disponibilidade</h2>
      <p>
        O Rolehound é um projeto pessoal, oferecido como está. Ele pode ficar fora do ar, mudar ou ser encerrado. Se for encerrado, você será avisado com antecedência razoável para
        exportar os seus dados.
      </p>

      <h2>6. Encerramento da conta</h2>
      <ul>
        <li>Você pode apagar a sua conta quando quiser, no seu perfil.</li>
        <li>O administrador pode desativar uma conta que desrespeite estes termos.</li>
      </ul>

      <h2>7. Responsabilidade</h2>
      <p>
        Na medida permitida pela lei, o Rolehound não responde por decisões tomadas com base nas vagas, no score ou nos textos gerados por IA, nem por custos da sua conta no
        OpenRouter.
      </p>

      <h2>8. Mudanças nestes termos</h2>
      <p>Quando estes termos mudarem, o app mostra a nova versão e pede o seu aceite antes de você continuar.</p>

      <h2>9. Lei e contato</h2>
      <p>
        Estes termos seguem as leis do Brasil. Dúvidas: <a href={`mailto:${CONTACT}`}>{CONTACT}</a>.
      </p>
    </LegalLayout>
  )
}
