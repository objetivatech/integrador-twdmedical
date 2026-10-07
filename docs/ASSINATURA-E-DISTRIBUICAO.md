# Assinatura de código e distribuição

## O problema

Executáveis sem assinatura digital disparam o **SmartScreen** ("O Windows protegeu o computador")
e são vistos com mais desconfiança pelos antivírus (no vídeo de referência aparece o Kaspersky).

## Opções avaliadas

| Opção | Custo | Prós | Contras |
|-------|-------|------|---------|
| **Microsoft Store (MSIX)** | Conta de desenvolvedor: gratuita para pessoa física; taxa única para empresa | A Microsoft assina o pacote; atualização pela Store | Precisa da capacidade restrita `runFullTrust` (justificada na submissão), passa por revisão a cada versão e o app fica com listagem pública ou de audiência privada. Em computadores de hospital a Store costuma estar **bloqueada pela TI**. Ciclo de versão mais lento |
| **Azure Artifact Signing** (antigo Trusted Signing) | ~US$ 9,99/mês | Barato; assina EXE/MSI/MSIX; integra com o GitHub Actions | Até onde verificamos, só aceita **organizações dos EUA, Canadá, UE e Reino Unido**. Empresa brasileira provavelmente não consegue conta (confirmar no FAQ oficial) |
| **Certificado OV comercial** (Sectigo, Certum, DigiCert…) | ~US$ 150–400/ano | Funciona para qualquer empresa com CNPJ; aceito por todos os antivírus | Custo anual. Desde 2023 a chave fica em token físico ou HSM na nuvem, o que muda como o CI assina |
| **Sem assinatura** | R$ 0 | Imediato | Alerta do SmartScreen na instalação ("Mais informações → Executar assim mesmo") |

## Recomendação

1. **Piloto (agora): sem assinatura.** Quem instala é o técnico, que clica em "Executar assim
   mesmo" uma única vez. Depois disso o Velopack atualiza sozinho, sem novos alertas de instalação.
   Para reduzir falsos positivos:
   - enviar cada versão para análise em <https://www.microsoft.com/wdsi/filesubmission>, como desenvolvedor de software;
   - se algum antivírus bloquear, reportar o falso positivo no portal do fabricante (o Kaspersky tem um);
   - nunca empacotar com ferramentas que comprimem o executável (UPX etc.).
2. **Produção (80+ equipamentos): certificado OV comercial**, em nome da TWD Medical ou da Ranktop.
   A opção de menor custo costuma ser um OV da Certum ou de revendas da Sectigo, com assinatura na
   nuvem (sem token USB), o que permite assinar no próprio GitHub Actions. O
   `.github/workflows/release.yml` já tem o ponto marcado para incluir a assinatura.
3. **Microsoft Store: não recomendada como canal principal.** A Store resolveria a assinatura, mas
   o bloqueio comum em redes hospitalares e a revisão a cada versão atrapalham correções urgentes.
   Pode ser um canal **adicional** no futuro, porque o mesmo código pode ser empacotado como MSIX.

## Distribuição e atualização

- O técnico baixa `TwdIntegrador-win-Setup.exe` da página de Releases (ou de um link da plataforma).
- A instalação é por usuário (`%LocalAppData%\TwdIntegrador`) e não pede administrador.
- O integrador verifica atualizações ao iniciar e a cada 6 h. A versão nova é aplicada quando o
  programa reinicia (logoff/reinício do PC), nunca no meio de um envio.
- Se o repositório for privado, troque a origem do Velopack para um bucket (por exemplo Cloudflare R2,
  via `SimpleWebSource`). Releases de um repositório privado não são acessíveis sem token.
