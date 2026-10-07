using Microsoft.Data.Sqlite;

namespace TwdIntegrador.Core.Fila;

public enum StatusItem
{
    Pendente = 0,
    Enviado = 1,
    /// <summary>A plataforma recusou (dados inválidos etc.). Não é reenviado automaticamente.</summary>
    Rejeitado = 2,
}

public sealed record ItemFila
{
    public long Id { get; init; }
    public required string Sha256 { get; init; }
    public required string FonteId { get; init; }
    public required string Conector { get; init; }
    public required string CaminhoOriginal { get; init; }
    public required string NomeArquivo { get; init; }
    public required long Tamanho { get; init; }
    /// <summary>Cópia criptografada do PDF dentro da pasta de dados.</summary>
    public required string CaminhoCopia { get; init; }
    /// <summary>Metadados prontos para envio (JSON).</summary>
    public required string MetadadosJson { get; init; }
    public string? Paciente { get; init; }
    public string? NumeroExame { get; init; }
    public StatusItem Status { get; init; }
    public int Tentativas { get; init; }
    public DateTimeOffset ProximaTentativa { get; init; }
    public string? UltimoErro { get; init; }
    public string? IdRemoto { get; init; }
    public DateTimeOffset CapturadoEm { get; init; }
    public DateTimeOffset? EnviadoEm { get; init; }
}

public sealed record ContagemFila(int Pendentes, int Enviados, int Rejeitados);

/// <summary>
/// Fila persistente em SQLite. Garante que nenhum exame se perde se a internet cair ou o
/// computador desligar, e que o mesmo arquivo (mesmo SHA-256) nunca é enfileirado duas vezes.
/// </summary>
public sealed class FilaEnvio
{
    private readonly string _conexao;

    public FilaEnvio(string caminhoBanco)
    {
        _conexao = new SqliteConnectionStringBuilder { DataSource = caminhoBanco, Pooling = false }.ToString();
        Executar("""
            PRAGMA journal_mode = WAL;
            CREATE TABLE IF NOT EXISTS itens (
                id                INTEGER PRIMARY KEY AUTOINCREMENT,
                sha256            TEXT    NOT NULL UNIQUE,
                fonte_id          TEXT    NOT NULL,
                conector          TEXT    NOT NULL,
                caminho_original  TEXT    NOT NULL,
                nome_arquivo      TEXT    NOT NULL,
                tamanho           INTEGER NOT NULL,
                caminho_copia     TEXT    NOT NULL,
                metadados_json    TEXT    NOT NULL,
                paciente          TEXT,
                numero_exame      TEXT,
                status            INTEGER NOT NULL DEFAULT 0,
                tentativas        INTEGER NOT NULL DEFAULT 0,
                proxima_tentativa INTEGER NOT NULL,
                ultimo_erro       TEXT,
                id_remoto         TEXT,
                capturado_em      INTEGER NOT NULL,
                enviado_em        INTEGER
            );
            CREATE INDEX IF NOT EXISTS ix_itens_status ON itens(status, proxima_tentativa);
            """);
    }

    /// <summary>Insere o item. Retorna false se o mesmo arquivo (SHA-256) já foi capturado antes.</summary>
    public bool Enfileirar(ItemFila item)
    {
        using var con = Abrir();
        using var cmd = con.CreateCommand();
        cmd.CommandText = """
            INSERT OR IGNORE INTO itens
                (sha256, fonte_id, conector, caminho_original, nome_arquivo, tamanho, caminho_copia,
                 metadados_json, paciente, numero_exame, status, tentativas, proxima_tentativa, capturado_em)
            VALUES ($sha, $fonte, $conector, $orig, $nome, $tam, $copia, $meta, $pac, $num, 0, 0, $prox, $cap)
            """;
        cmd.Parameters.AddWithValue("$sha", item.Sha256);
        cmd.Parameters.AddWithValue("$fonte", item.FonteId);
        cmd.Parameters.AddWithValue("$conector", item.Conector);
        cmd.Parameters.AddWithValue("$orig", item.CaminhoOriginal);
        cmd.Parameters.AddWithValue("$nome", item.NomeArquivo);
        cmd.Parameters.AddWithValue("$tam", item.Tamanho);
        cmd.Parameters.AddWithValue("$copia", item.CaminhoCopia);
        cmd.Parameters.AddWithValue("$meta", item.MetadadosJson);
        cmd.Parameters.AddWithValue("$pac", (object?)item.Paciente ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$num", (object?)item.NumeroExame ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$prox", item.CapturadoEm.ToUnixTimeMilliseconds());
        cmd.Parameters.AddWithValue("$cap", item.CapturadoEm.ToUnixTimeMilliseconds());
        return cmd.ExecuteNonQuery() == 1;
    }

    public ItemFila? ObterPorSha(string sha256) =>
        Consultar("SELECT * FROM itens WHERE sha256 = $p", ("$p", sha256)).FirstOrDefault();

    public IReadOnlyList<ItemFila> ObterProntosParaEnvio(DateTimeOffset agora, int limite = 10) =>
        Consultar(
            "SELECT * FROM itens WHERE status = 0 AND proxima_tentativa <= $agora ORDER BY capturado_em LIMIT $lim",
            ("$agora", agora.ToUnixTimeMilliseconds()), ("$lim", limite));

    public IReadOnlyList<ItemFila> ListarRecentes(int limite = 200) =>
        Consultar("SELECT * FROM itens ORDER BY capturado_em DESC LIMIT $lim", ("$lim", limite));

    public void MarcarEnviado(long id, string? idRemoto, DateTimeOffset quando) =>
        Executar("UPDATE itens SET status = 1, id_remoto = $r, enviado_em = $q, ultimo_erro = NULL WHERE id = $id",
            ("$r", (object?)idRemoto ?? DBNull.Value), ("$q", quando.ToUnixTimeMilliseconds()), ("$id", id));

    /// <summary>Falha temporária (rede, servidor fora): reagenda com espera exponencial.</summary>
    public void MarcarFalhaTemporaria(long id, string erro, DateTimeOffset proximaTentativa) =>
        Executar("UPDATE itens SET tentativas = tentativas + 1, ultimo_erro = $e, proxima_tentativa = $p WHERE id = $id",
            ("$e", erro), ("$p", proximaTentativa.ToUnixTimeMilliseconds()), ("$id", id));

    public void MarcarRejeitado(long id, string erro) =>
        Executar("UPDATE itens SET status = 2, tentativas = tentativas + 1, ultimo_erro = $e WHERE id = $id",
            ("$e", erro), ("$id", id));

    /// <summary>Recoloca um item rejeitado na fila (ação manual do técnico).</summary>
    public void Reenfileirar(long id, DateTimeOffset agora) =>
        Executar("UPDATE itens SET status = 0, proxima_tentativa = $p WHERE id = $id AND status = 2",
            ("$p", agora.ToUnixTimeMilliseconds()), ("$id", id));

    /// <summary>Antecipa as tentativas pendentes (ex.: a internet voltou, ou o token foi corrigido).</summary>
    public void AnteciparPendentes(DateTimeOffset agora) =>
        Executar("UPDATE itens SET proxima_tentativa = $p WHERE status = 0", ("$p", agora.ToUnixTimeMilliseconds()));

    /// <summary>
    /// Remove dados do paciente de itens já enviados há mais de <paramref name="retencao"/>.
    /// Fica só o SHA-256 (para continuar barrando duplicidades) e o id remoto.
    /// </summary>
    public void AnonimizarEnviadosAntigos(DateTimeOffset agora, TimeSpan retencao) =>
        Executar("""
            UPDATE itens SET paciente = NULL, numero_exame = NULL, metadados_json = '{}', caminho_original = ''
            WHERE status = 1 AND enviado_em < $limite AND metadados_json <> '{}'
            """, ("$limite", (agora - retencao).ToUnixTimeMilliseconds()));

    public ContagemFila Contar()
    {
        using var con = Abrir();
        using var cmd = con.CreateCommand();
        cmd.CommandText = "SELECT status, COUNT(*) FROM itens GROUP BY status";
        int p = 0, e = 0, r = 0;
        using var rd = cmd.ExecuteReader();
        while (rd.Read())
        {
            var n = rd.GetInt32(1);
            switch ((StatusItem)rd.GetInt32(0))
            {
                case StatusItem.Pendente: p = n; break;
                case StatusItem.Enviado: e = n; break;
                case StatusItem.Rejeitado: r = n; break;
            }
        }
        return new ContagemFila(p, e, r);
    }

    private SqliteConnection Abrir()
    {
        var con = new SqliteConnection(_conexao);
        con.Open();
        return con;
    }

    private void Executar(string sql, params (string Nome, object Valor)[] parametros)
    {
        using var con = Abrir();
        using var cmd = con.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (nome, valor) in parametros) cmd.Parameters.AddWithValue(nome, valor);
        cmd.ExecuteNonQuery();
    }

    private List<ItemFila> Consultar(string sql, params (string Nome, object Valor)[] parametros)
    {
        using var con = Abrir();
        using var cmd = con.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (nome, valor) in parametros) cmd.Parameters.AddWithValue(nome, valor);
        using var rd = cmd.ExecuteReader();
        var itens = new List<ItemFila>();
        while (rd.Read()) itens.Add(Ler(rd));
        return itens;
    }

    private static ItemFila Ler(SqliteDataReader rd)
    {
        string? Texto(string col) => rd.IsDBNull(rd.GetOrdinal(col)) ? null : rd.GetString(rd.GetOrdinal(col));
        DateTimeOffset Data(string col) => DateTimeOffset.FromUnixTimeMilliseconds(rd.GetInt64(rd.GetOrdinal(col)));

        return new ItemFila
        {
            Id = rd.GetInt64(rd.GetOrdinal("id")),
            Sha256 = rd.GetString(rd.GetOrdinal("sha256")),
            FonteId = rd.GetString(rd.GetOrdinal("fonte_id")),
            Conector = rd.GetString(rd.GetOrdinal("conector")),
            CaminhoOriginal = rd.GetString(rd.GetOrdinal("caminho_original")),
            NomeArquivo = rd.GetString(rd.GetOrdinal("nome_arquivo")),
            Tamanho = rd.GetInt64(rd.GetOrdinal("tamanho")),
            CaminhoCopia = rd.GetString(rd.GetOrdinal("caminho_copia")),
            MetadadosJson = rd.GetString(rd.GetOrdinal("metadados_json")),
            Paciente = Texto("paciente"),
            NumeroExame = Texto("numero_exame"),
            Status = (StatusItem)rd.GetInt32(rd.GetOrdinal("status")),
            Tentativas = rd.GetInt32(rd.GetOrdinal("tentativas")),
            ProximaTentativa = Data("proxima_tentativa"),
            UltimoErro = Texto("ultimo_erro"),
            IdRemoto = Texto("id_remoto"),
            CapturadoEm = Data("capturado_em"),
            EnviadoEm = rd.IsDBNull(rd.GetOrdinal("enviado_em")) ? null : Data("enviado_em"),
        };
    }
}
