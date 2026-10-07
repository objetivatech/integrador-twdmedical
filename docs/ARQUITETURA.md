# Arquitetura — TWD Integrador

## Objetivo

Capturar automaticamente os PDFs de exames gerados no computador do consultório (v1:
**WinCardio**; depois **Holter** e **MAPA**) e enviá-los para a plataforma TWD Core, onde
ficam **exclusivamente**. O operador não precisa fazer nada além do que já faz no WinCardio.

## Decisões

| Tema | Decisão | Motivo |
|------|---------|--------|
| Linguagem | **C# / .NET 10 (LTS até nov/2028)** | Nativo no Windows; executável leve; bom suporte de PDF/SQLite; menos alertas de antivírus que Python ou Electron |
| Formato | App na **bandeja do Windows**, iniciado com o usuário | O WinCardio exige um usuário logado, então um serviço do Windows não traz ganho. Assim também não precisa de administrador nem ficar invisível |
| Dados do exame | **Somente do PDF** | Pedido do cliente. Funciona com qualquer programa que gere PDF com texto |
| Identidade | **Token por equipamento**, gerado na plataforma | A plataforma já vincula equipamento ↔ hospital ↔ médicos |
| Plataforma | Supabase (Edge Functions + Storage privado + Postgres) | Stack Ranktop |
| Instalador / atualização | **Velopack** (instala por usuário, atualiza sozinho via GitHub Releases) | Instalador simples para o técnico; versões novas chegam aos ~80 consultórios sem visita |

## Componentes

```
src/TwdIntegrador.Core     (multiplataforma, 100% testável)
  Conectores/              IConector + WinCardioConector + GenericoConector
  Pdf/                     extração de texto em linhas visuais (PdfPig)
  Arquivos/                espera o PDF terminar de ser gravado
  Fila/                    fila persistente SQLite (sha256 único)
  Envio/                   cliente HTTPS + contrato JSON (docs/API.md)
  Servicos/                captura → fila → envio → heartbeat
src/TwdIntegrador.App      (Windows) bandeja, popup, configurações, histórico, DPAPI, auto-update
tools/TwdIntegrador.Diag   twd-diag: mostra o que o integrador lê de um PDF
tests/                     testes do Core (PDF sintético no layout do WinCardio)
plataforma/supabase        referência do lado servidor (migration + Edge Functions)
```

## Fluxo

```
WinCardio grava C:\...\exame-N.pdf
  │  FileSystemWatcher (tempo real) + varredura a cada 30 s (rede de segurança)
  ▼
Espera estabilizar: tamanho parou de mudar, abre em modo exclusivo, termina em %%EOF
  ▼
SHA-256 ── já conhecido? ── sim → (se já enviado, remove o original) fim
  │ não
  ▼
Extrai texto → conector da fonte interpreta → metadados JSON
  ▼
Cópia CRIPTOGRAFADA (DPAPI) na fila + linha no SQLite        ← popup "Enviando…"
  ▼
POST /integrador-exames (Bearer token, Idempotency-Key = sha256)
  ├─ 201/200 → marca enviado, apaga a cópia, exclui o original*  ← popup "enviado com sucesso"
  ├─ 401     → pausa os envios, ícone vermelho, balão para o técnico
  ├─ 4xx     → "recusado" (fica no histórico para reenvio manual)
  └─ rede/5xx→ tenta de novo com espera crescente (30 s … 1 h)
```

\* O original só é excluído se o arquivo naquele caminho **ainda tiver o mesmo SHA-256**. Como o
WinCardio pode reutilizar nomes (`exame-1.pdf`), o integrador nunca apaga um exame que não enviou.

## Segurança e LGPD

- **PDF fica só na plataforma.** Após o envio confirmado, o original e a cópia local são removidos (padrão configurável).
- **Em repouso, tudo criptografado.** O token e as cópias dos PDFs na fila usam DPAPI, ligada ao usuário do Windows: copiar a pasta para outro computador não expõe nada.
- **Em trânsito:** só HTTPS (a tela de configuração recusa `http://`).
- **Histórico local mínimo.** Nome e número do exame ficam no histórico local por 30 dias após o envio. Depois disso, resta só o SHA-256, para continuar barrando duplicidades.
- **No servidor:** bucket privado, RLS ligado, token guardado só como hash, revogável, e duplicidade barrada por SHA-256 e por nº+data do exame.
- **Configuração protegida** por senha opcional (PBKDF2), para o operador não alterar pasta nem token.
- **Instalação existente:** PDFs antigos que já estão na pasta **não** são enviados, a menos que o técnico marque essa opção.

## Como adicionar um novo programa (Holter, MAPA…)

1. Obtenha PDFs reais (anonimizados) e rode `twd-diag exame.pdf generico` para ver as linhas de texto.
2. Crie `XyzConector : IConector`, transformando essas linhas em `DadosExame`.
3. Registre-o em `RegistroConectores.Padrao()`.
4. Escreva testes com um PDF sintético no mesmo layout (veja `PdfSintetico.cs`).

O técnico então escolhe o programa na tela de configurações. Programas não reconhecidos podem
usar o conector **Genérico**: o PDF e o texto vão mesmo assim, e a plataforma trata.

## Pendências conhecidas

- **PDF original do WinCardio.** O conector foi feito a partir do print do laudo já na plataforma. É preciso validar com o arquivo original (rodar o `twd-diag`) antes do piloto.
- **Caminho de exportação do WinCardio.** É configurável, e a tela sugere `C:\TWD` quando a pasta existe.
- **Logo e ícone oficiais da TWD.** Ver `src/TwdIntegrador.App/assets/README.md`.
- **Várias pastas por computador.** O Core já suporta (`Fontes` é uma lista); a tela da v1 edita uma só.
- **Nomes em `integrador-status`.** A plataforma precisa preencher os nomes reais de equipamento e unidade.
