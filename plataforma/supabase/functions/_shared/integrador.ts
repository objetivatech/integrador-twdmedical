import { createClient, type SupabaseClient } from "jsr:@supabase/supabase-js@2";

export type TokenValido = {
  tokenId: string;
  equipamentoId: string;
};

export function clienteAdmin(): SupabaseClient {
  return createClient(
    Deno.env.get("SUPABASE_URL")!,
    Deno.env.get("SUPABASE_SERVICE_ROLE_KEY")!,
    { auth: { persistSession: false } },
  );
}

export function json(status: number, corpo: unknown): Response {
  return new Response(JSON.stringify(corpo), {
    status,
    headers: { "Content-Type": "application/json; charset=utf-8" },
  });
}

export async function sha256Hex(dados: Uint8Array<ArrayBuffer> | string): Promise<string> {
  const bytes = typeof dados === "string" ? new TextEncoder().encode(dados) : dados;
  const hash = await crypto.subtle.digest("SHA-256", bytes);
  return Array.from(new Uint8Array(hash)).map((b) => b.toString(16).padStart(2, "0")).join("");
}

/** Valida "Authorization: Bearer twd_..." contra o hash guardado. */
export async function autenticar(req: Request, db: SupabaseClient): Promise<TokenValido | null> {
  const cabecalho = req.headers.get("Authorization") ?? "";
  const token = cabecalho.startsWith("Bearer ") ? cabecalho.slice(7).trim() : "";
  if (!token.startsWith("twd_") || token.length > 200) return null;

  const { data, error } = await db
    .from("integrador_tokens")
    .select("id, equipamento_id")
    .eq("token_hash", await sha256Hex(token))
    .is("revogado_em", null)
    .maybeSingle();

  if (error || !data) return null;

  await db.from("integrador_tokens").update({ ultimo_uso_em: new Date().toISOString() }).eq("id", data.id);
  return { tokenId: data.id, equipamentoId: data.equipamento_id };
}
