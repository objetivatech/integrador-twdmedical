# Contrato da API — Integrador ↔ Plataforma

Versão do contrato: **1**. Implementação de referência em
[`plataforma/supabase/functions`](../plataforma/supabase/functions).

URL base: `https://<projeto>.supabase.co/functions/v1` (configurada no integrador).

## Autenticação

Todo request leva o token do equipamento:

```
Authorization: Bearer twd_<64 hex>
```

- O token é gerado **na plataforma**, no cadastro do equipamento (`integrador_gerar_token`), e
  mostrado uma única vez para o técnico copiar.
- A plataforma guarda só o SHA-256 do token. Gerar um novo token revoga o anterior.
- O token identifica o **equipamento**. A plataforma já sabe a qual hospital/clínica e a quais
  médicos laudadores esse equipamento está vinculado, por isso o integrador não envia nada disso.

## `POST /integrador-exames`

`multipart/form-data` com dois campos:

| Campo       | Tipo               | Descrição                         |
|-------------|--------------------|-----------------------------------|
| `arquivo`   | `application/pdf`  | O PDF do exame, byte a byte       |
| `metadados` | `application/json` | Ver abaixo                        |

Cabeçalho `Idempotency-Key: <sha256 do PDF>`.

### `metadados`

Todos os campos de `exame` são **opcionais**: são enviados apenas os que existirem no PDF.

```json
{
  "versao_contrato": 1,
  "arquivo": { "nome": "exame-2.pdf", "sha256": "9f2c…", "tamanho": 488406 },
  "origem": {
    "conector": "wincardio",
    "fonte": "WinCardio (Micromed)",
    "computador": "CONSULTORIO-02",
    "versao_integrador": "0.1.0",
    "capturado_em": "2026-08-31T20:45:10-03:00"
  },
  "exame": {
    "tipo": "ECG de Repouso",
    "numero": "12142",
    "data_hora": "2026-08-31T20:44:53-03:00",
    "paciente": {
      "nome": "IDENE MARCONATO",
      "data_nascimento": "1953-12-17",
      "idade": null,
      "sexo": null,
      "cpf": "12345678909",
      "rg": null,
      "registro_clinico": null
    },
    "convenio": "UNIMED",
    "frequencia_cardiaca": 56,
    "serial_equipamento": "0002187791",
    "fabricante": "MICROMED",
    "extras": { "campo_qualquer": "valor" },
    "avisos": ["Número do exame não encontrado"]
  },
  "texto_extraido": "ECG de Repouso\nExame: 12142 Nome: …"
}
```

- Campos nulos são omitidos no envio real.
- `texto_extraido` traz o texto completo do PDF, para a plataforma reprocessar se um conector
  melhorar no futuro.
- `avisos` indica campos que o conector esperava e não achou (ajuda a detectar PDFs fora do padrão).

### Respostas

| Status  | Significado                                   | O que o integrador faz                         |
|---------|-----------------------------------------------|------------------------------------------------|
| **201** | `{ "id": "<uuid>" }`: exame registrado        | Marca como enviado e remove o PDF local        |
| **200** | `{ "id", "mensagem" }`: já existia            | Igual ao 201 (duplicidade não é erro)          |
| 401/403 | Token inválido/revogado                       | Para todos os envios e alerta o técnico        |
| 400/413/422 | Conteúdo inválido                         | Marca como **recusado**; reenvio só manual     |
| 408/429/5xx, rede | Falha temporária                    | Tenta de novo: 30 s, 1 min, 2 min… até 1 h     |

### Duplicidade

A plataforma barra duplicidade em dois níveis:

1. **Mesmo arquivo:** `sha256` é único.
2. **Mesmo exame exportado de novo:** `(equipamento, numero_exame, data_hora_exame)` é único.

O integrador também não reenvia um arquivo cujo SHA-256 já conhece.

## `GET /integrador-status`

Valida o token (botão "Testar conexão"). Resposta `200`:

```json
{ "equipamento": "ECG Consultório 2", "unidade": "Ambulatório Municipal – Lagoa Vermelha" }
```

## `POST /integrador-status` (heartbeat, a cada 5 min)

```json
{ "versao_integrador": "0.1.0", "computador": "CONSULTORIO-02", "pendentes": 0, "rejeitados": 0,
  "ultimo_envio": "2026-08-31T23:45:12Z" }
```

Com isso a plataforma pode mostrar quais consultórios estão online, qual versão cada um roda,
e alertar quando houver exames parados na fila.
