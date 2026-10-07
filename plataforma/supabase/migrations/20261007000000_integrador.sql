-- =====================================================================
-- TWD Integrador — estrutura de recebimento de exames (referência)
--
-- Pensado para ser incorporado ao projeto Supabase da plataforma TWD Core.
-- Ajuste as referências a "equipamentos" para a tabela real de equipamentos
-- (cadastro que já vincula hospital/clínica e médicos laudadores).
-- =====================================================================

create extension if not exists pgcrypto with schema extensions;

-- ---------------------------------------------------------------------
-- Tokens dos integradores. Um token por equipamento (computador do
-- consultório onde o WinCardio está instalado). Guardamos só o hash.
-- ---------------------------------------------------------------------
create table if not exists public.integrador_tokens (
    id               uuid primary key default gen_random_uuid(),
    equipamento_id   uuid not null,          -- TODO: references public.equipamentos(id)
    prefixo          text not null,          -- primeiros caracteres, para identificar o token na tela
    token_hash       text not null unique,   -- sha256 hex do token
    criado_em        timestamptz not null default now(),
    criado_por       uuid,                   -- auth.users(id) de quem gerou
    revogado_em      timestamptz,
    ultimo_uso_em    timestamptz,
    ultimo_heartbeat jsonb
);

create index if not exists integrador_tokens_equipamento_idx on public.integrador_tokens (equipamento_id);

-- ---------------------------------------------------------------------
-- Exames recebidos. Duplicidade barrada em dois níveis:
--   1. mesmo arquivo (sha256) — reenvio do mesmo PDF;
--   2. mesmo equipamento + nº do exame + data/hora — mesmo exame exportado duas vezes.
-- ---------------------------------------------------------------------
create table if not exists public.integrador_exames (
    id                   uuid primary key default gen_random_uuid(),
    equipamento_id       uuid not null,       -- TODO: references public.equipamentos(id)
    token_id             uuid not null references public.integrador_tokens(id),
    sha256               text not null unique,
    arquivo_path         text not null,       -- caminho no bucket privado "exames"
    nome_arquivo         text not null,
    tamanho_bytes        bigint not null,
    conector             text not null,       -- wincardio, holter, mapa...
    tipo_exame           text,
    numero_exame         text,
    data_hora_exame      timestamptz,
    paciente_nome        text,
    paciente_cpf         text,
    paciente_nascimento  date,
    paciente_sexo        text,
    paciente_idade       int,
    convenio             text,
    serial_equipamento   text,
    metadados            jsonb not null,      -- corpo completo enviado pelo integrador
    texto_extraido       text,
    status               text not null default 'pendente', -- pendente | em_laudo | finalizado
    recebido_em          timestamptz not null default now()
);

create unique index if not exists integrador_exames_numero_uidx
    on public.integrador_exames (equipamento_id, numero_exame, data_hora_exame)
    where numero_exame is not null and data_hora_exame is not null;

create index if not exists integrador_exames_recebido_idx on public.integrador_exames (equipamento_id, recebido_em desc);

-- ---------------------------------------------------------------------
-- Segurança: RLS ligado e sem políticas = só o service_role (Edge Functions)
-- acessa. As telas da plataforma devem expor estes dados por políticas próprias,
-- conforme o vínculo usuário ↔ hospital ↔ equipamento.
-- ---------------------------------------------------------------------
alter table public.integrador_tokens enable row level security;
alter table public.integrador_exames enable row level security;

-- Bucket privado para os PDFs (sem acesso público; download só via URL assinada).
insert into storage.buckets (id, name, public, file_size_limit, allowed_mime_types)
values ('exames', 'exames', false, 20971520, array['application/pdf'])
on conflict (id) do nothing;

-- ---------------------------------------------------------------------
-- Gera um token para um equipamento. Retorna o token em texto UMA única vez
-- (a plataforma mostra para o técnico copiar). Gerar um novo revoga os anteriores.
-- ---------------------------------------------------------------------
create or replace function public.integrador_gerar_token(p_equipamento_id uuid)
returns text
language plpgsql
security definer
set search_path = public, extensions
as $$
declare
    v_token text := 'twd_' || encode(extensions.gen_random_bytes(32), 'hex');
begin
    update public.integrador_tokens
       set revogado_em = now()
     where equipamento_id = p_equipamento_id and revogado_em is null;

    insert into public.integrador_tokens (equipamento_id, prefixo, token_hash, criado_por)
    values (p_equipamento_id, left(v_token, 12), encode(extensions.digest(v_token, 'sha256'), 'hex'), auth.uid());

    return v_token;
end;
$$;

-- Só o backend (service_role) gera tokens. Se a tela de administração chamar via
-- RPC com usuário logado, troque por uma checagem de papel "administrador" aqui dentro.
revoke all on function public.integrador_gerar_token(uuid) from public, anon, authenticated;
grant execute on function public.integrador_gerar_token(uuid) to service_role;
