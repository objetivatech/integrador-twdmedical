# TWD Integrador

Aplicativo para Windows que captura automaticamente os PDFs de exames gerados no consultório
(v1: **WinCardio**; próximos: **Holter** e **MAPA**) e os envia para a plataforma **TWD Core**.

- Captura em tempo real da pasta onde o programa salva o PDF.
- Lê do PDF os dados do exame e do paciente que existirem (nº, nome, nascimento, CPF, convênio, data/hora, FC, serial do equipamento).
- Fila offline: sem internet, nada se perde, e o envio é refeito sozinho.
- Sem duplicidade: SHA-256 no integrador e na plataforma, mais nº+data do exame na plataforma.
- O PDF fica **só na plataforma**: é excluído do computador após o envio confirmado.
- Ícone na bandeja com estado (verde, amarelo ou vermelho), cartão "Dados do Exame", histórico e reenvio.
- Token por equipamento gerado na plataforma; segredos criptografados com DPAPI.
- Instalador simples e atualização automática (Velopack + GitHub Releases).

## Documentação

| Documento | Para quem |
|-----------|-----------|
| [docs/ARQUITETURA.md](docs/ARQUITETURA.md) | Desenvolvimento: decisões, fluxo, segurança/LGPD, como criar conectores |
| [docs/API.md](docs/API.md) | Equipe da plataforma: contrato do endpoint de recebimento |
| [docs/INSTALACAO-TECNICO.md](docs/INSTALACAO-TECNICO.md) | Técnico de suporte |
| [docs/ASSINATURA-E-DISTRIBUICAO.md](docs/ASSINATURA-E-DISTRIBUICAO.md) | Gestão: certificado de código, Microsoft Store, distribuição |

## Estrutura

```
src/TwdIntegrador.Core        lógica (multiplataforma, testada)
src/TwdIntegrador.App         aplicativo Windows (bandeja)
tools/TwdIntegrador.Diag      twd-diag: inspeciona o que é lido de um PDF
tests/TwdIntegrador.Core.Tests
plataforma/supabase           referência do servidor: migration SQL + Edge Functions
```

## Desenvolvimento

Requer o [.NET SDK 10](https://dotnet.microsoft.com/download). Core, testes e diagnóstico rodam
em Windows, Linux ou macOS. O aplicativo compila em qualquer sistema, mas só **executa** no Windows.

```bash
dotnet build TwdIntegrador.sln
dotnet test
dotnet run --project tools/TwdIntegrador.Diag -- caminho/do/exame.pdf wincardio
```

Para gerar o executável Windows: `dotnet publish src/TwdIntegrador.App -c Release -o publish`.
Para publicar uma versão com instalador: crie e envie uma tag `vX.Y.Z` (ver `.github/workflows/release.yml`).

> ⚠️ **Nunca** versione PDFs reais de pacientes. O `.gitignore` bloqueia `*.pdf`, e os testes usam PDFs sintéticos.
