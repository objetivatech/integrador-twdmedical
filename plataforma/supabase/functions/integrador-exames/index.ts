// Recebe um exame do TWD Integrador (multipart: "arquivo" = PDF, "metadados" = JSON).
// Contrato completo em docs/API.md.
//
// Respostas:
//   201 { id }                    exame registrado
//   200 { id, mensagem }          já existia (mesmo arquivo ou mesmo nº/data) — integrador trata como sucesso
//   400/413/422 { mensagem }      conteúdo inválido — integrador não reenvia automaticamente
//   401 { mensagem }              token inválido/revogado
//   500 { mensagem }              erro interno — integrador tenta de novo mais tarde

import { autenticar, clienteAdmin, json, sha256Hex } from "../_shared/integrador.ts";

const TAMANHO_MAXIMO = 20 * 1024 * 1024;

type Metadados = {
  versao_contrato: number;
  arquivo: { nome: string; sha256: string; tamanho: number };
  origem: { conector: string; fonte: string; computador: string; versao_integrador: string; capturado_em: string };
  exame: {
    tipo?: string;
    numero?: string;
    data_hora?: string;
    paciente?: {
      nome?: string;
      data_nascimento?: string;
      idade?: number;
      sexo?: string;
      cpf?: string;
      rg?: string;
      registro_clinico?: string;
    };
    convenio?: string;
    frequencia_cardiaca?: number;
    serial_equipamento?: string;
    fabricante?: string;
  };
  texto_extraido?: string;
};

Deno.serve(async (req) => {
  if (req.method !== "POST") return json(405, { mensagem: "Use POST" });

  const db = clienteAdmin();
  const auth = await autenticar(req, db);
  if (!auth) return json(401, { mensagem: "Token inválido ou revogado" });

  let form: FormData;
  try {
    form = await req.formData();
  } catch {
    return json(400, { mensagem: "Corpo multipart inválido" });
  }

  const arquivo = form.get("arquivo");
  const metadadosTexto = form.get("metadados");
  if (!(arquivo instanceof File) || typeof metadadosTexto !== "string") {
    return json(400, { mensagem: "Campos 'arquivo' e 'metadados' são obrigatórios" });
  }
  if (arquivo.size === 0 || arquivo.size > TAMANHO_MAXIMO) {
    return json(413, { mensagem: "Arquivo vazio ou maior que 20 MB" });
  }

  let meta: Metadados;
  try {
    meta = JSON.parse(metadadosTexto);
  } catch {
    return json(400, { mensagem: "Metadados não são um JSON válido" });
  }
  if (meta.versao_contrato !== 1) return json(422, { mensagem: `versao_contrato ${meta.versao_contrato} não suportada` });

  const bytes = new Uint8Array(await arquivo.arrayBuffer());
  if (String.fromCharCode(...bytes.subarray(0, 5)) !== "%PDF-") {
    return json(422, { mensagem: "O arquivo não é um PDF" });
  }

  // O hash é recalculado aqui: garante integridade e serve de chave de duplicidade.
  const sha256 = await sha256Hex(bytes);
  if (meta.arquivo?.sha256 !== sha256 || req.headers.get("Idempotency-Key") !== sha256) {
    return json(422, { mensagem: "SHA-256 do arquivo não confere com o informado" });
  }

  const existente = await buscarExistente(db, auth.equipamentoId, sha256, meta);
  if (existente) return json(200, { id: existente, mensagem: "Exame já recebido anteriormente" });

  const id = crypto.randomUUID();
  const agora = new Date();
  const caminho = `${auth.equipamentoId}/${agora.getUTCFullYear()}/${String(agora.getUTCMonth() + 1).padStart(2, "0")}/${id}.pdf`;

  const upload = await db.storage.from("exames").upload(caminho, bytes, { contentType: "application/pdf", upsert: false });
  if (upload.error) {
    console.error("upload", upload.error);
    return json(500, { mensagem: "Falha ao armazenar o arquivo" });
  }

  const p = meta.exame?.paciente ?? {};
  const { error } = await db.from("integrador_exames").insert({
    id,
    equipamento_id: auth.equipamentoId,
    token_id: auth.tokenId,
    sha256,
    arquivo_path: caminho,
    nome_arquivo: meta.arquivo.nome?.slice(0, 255) ?? "exame.pdf",
    tamanho_bytes: bytes.length,
    conector: meta.origem?.conector ?? "desconhecido",
    tipo_exame: meta.exame?.tipo ?? null,
    numero_exame: meta.exame?.numero ?? null,
    data_hora_exame: meta.exame?.data_hora ?? null,
    paciente_nome: p.nome ?? null,
    paciente_cpf: p.cpf ?? null,
    paciente_nascimento: p.data_nascimento ?? null,
    paciente_sexo: p.sexo ?? null,
    paciente_idade: p.idade ?? null,
    convenio: meta.exame?.convenio ?? null,
    serial_equipamento: meta.exame?.serial_equipamento ?? null,
    metadados: { ...meta, texto_extraido: undefined },
    texto_extraido: meta.texto_extraido ?? null,
  });

  if (error) {
    await db.storage.from("exames").remove([caminho]);
    if (error.code === "23505") {
      // Corrida: outro envio do mesmo exame terminou primeiro.
      const id2 = await buscarExistente(db, auth.equipamentoId, sha256, meta);
      return json(200, { id: id2, mensagem: "Exame já recebido anteriormente" });
    }
    if (error.code?.startsWith("22")) {
      return json(422, { mensagem: `Dado inválido: ${error.message}` });
    }
    console.error("insert", error);
    return json(500, { mensagem: "Falha ao registrar o exame" });
  }

  return json(201, { id });
});

async function buscarExistente(
  db: ReturnType<typeof clienteAdmin>,
  equipamentoId: string,
  sha256: string,
  meta: Metadados,
): Promise<string | null> {
  const porHash = await db.from("integrador_exames").select("id").eq("sha256", sha256).maybeSingle();
  if (porHash.data) return porHash.data.id;

  if (meta.exame?.numero && meta.exame?.data_hora) {
    const porNumero = await db
      .from("integrador_exames")
      .select("id")
      .eq("equipamento_id", equipamentoId)
      .eq("numero_exame", meta.exame.numero)
      .eq("data_hora_exame", meta.exame.data_hora)
      .maybeSingle();
    if (porNumero.data) return porNumero.data.id;
  }
  return null;
}
