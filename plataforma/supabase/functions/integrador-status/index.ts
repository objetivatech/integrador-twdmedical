// GET  -> valida o token ("Testar conexão" no integrador) e devolve equipamento/unidade.
// POST -> heartbeat periódico: versão, fila pendente, último envio. Permite à plataforma
//         mostrar quais consultórios estão online e alertar sobre filas paradas.

import { autenticar, clienteAdmin, json } from "../_shared/integrador.ts";

Deno.serve(async (req) => {
  const db = clienteAdmin();
  const auth = await autenticar(req, db);
  if (!auth) return json(401, { mensagem: "Token inválido ou revogado" });

  if (req.method === "GET") {
    // TODO: buscar nome do equipamento e da unidade nas tabelas reais da plataforma.
    return json(200, { equipamento: auth.equipamentoId, unidade: null });
  }

  if (req.method === "POST") {
    let corpo: Record<string, unknown> = {};
    try {
      corpo = await req.json();
    } catch {
      return json(400, { mensagem: "JSON inválido" });
    }
    const heartbeat = {
      versao_integrador: String(corpo.versao_integrador ?? "").slice(0, 32),
      computador: String(corpo.computador ?? "").slice(0, 64),
      pendentes: Number(corpo.pendentes ?? 0),
      rejeitados: Number(corpo.rejeitados ?? 0),
      ultimo_envio: corpo.ultimo_envio ?? null,
      recebido_em: new Date().toISOString(),
    };
    await db.from("integrador_tokens").update({ ultimo_heartbeat: heartbeat }).eq("id", auth.tokenId);
    return json(200, { ok: true });
  }

  return json(405, { mensagem: "Use GET ou POST" });
});
