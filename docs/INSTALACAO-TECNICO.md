# Guia de instalação (técnico de suporte)

**Antes de ir à clínica:** cadastre o equipamento na plataforma e gere o **token do integrador**.
Ele aparece uma única vez; copie e leve com você.

1. No computador do consultório (onde o WinCardio está instalado e logado com o usuário que o
   operador usa), execute `TwdIntegrador-win-Setup.exe`.
   - Se aparecer "O Windows protegeu o computador", clique em **Mais informações → Executar assim mesmo**.
2. A tela **Configurações** abre sozinha. Preencha:
   - **Endereço da plataforma** (fornecido pela equipe TWD).
   - **Token do equipamento.** Clique em **Testar conexão**: deve aparecer o nome do equipamento e da unidade.
   - **Programa:** WinCardio.
   - **Pasta onde o programa salva os PDFs:** confira no WinCardio para onde ele exporta o PDF do exame.
   - Deixe **"Enviar também os PDFs que já estão na pasta"** desmarcado, a não ser que a clínica peça.
   - **Depois do envio:** *Excluir do computador* (padrão).
   - Defina uma **senha** para que o operador não altere a configuração.
3. Clique em **Salvar**. O ícone **T** ao lado do relógio fica com um ponto **verde**.
4. **Teste:** faça um exame de teste no WinCardio e salve. Em alguns segundos aparece o cartão
   "Dados do Exame" e depois "Exame enviado para a plataforma com sucesso". Confira na plataforma.

## Cores do ícone

| Ponto | Significado |
|-------|-------------|
| Verde | Funcionando |
| Amarelo | Sem internet (exames guardados, serão enviados depois) ou pausado |
| Vermelho | Falta configuração ou token recusado |

## Problemas comuns

- **O exame não aparece.** Confira a pasta configurada. Clique com o botão direito no ícone, abra
  **Abrir pasta de logs** e envie o log do dia para o suporte.
- **Token recusado.** Gere um novo token na plataforma e cole em Configurações. Os exames guardados
  são enviados em seguida.
- **Dados do paciente faltando na plataforma.** Rode `twd-diag exame.pdf` com uma cópia do PDF e envie a saída para a equipe de desenvolvimento.
