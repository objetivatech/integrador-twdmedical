using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace TwdIntegrador.Core.Envio;

public enum TipoResultado
{
    /// <summary>Exame registrado na plataforma.</summary>
    Criado,
    /// <summary>A plataforma já tinha este exame. Para o integrador, equivale a sucesso.</summary>
    Duplicado,
    /// <summary>Token inválido ou revogado. Nada será enviado até o token ser corrigido.</summary>
    NaoAutorizado,
    /// <summary>A plataforma recusou o conteúdo (4xx). Não adianta reenviar igual.</summary>
    Rejeitado,
    /// <summary>Rede, timeout ou erro 5xx/429. Tentar de novo mais tarde.</summary>
    FalhaTemporaria,
}

public sealed record ResultadoEnvio(TipoResultado Tipo, string? IdRemoto = null, string? Mensagem = null)
{
    public bool Sucesso => Tipo is TipoResultado.Criado or TipoResultado.Duplicado;
}

public sealed record DadosHeartbeat(string VersaoIntegrador, string Computador, int Pendentes, int Rejeitados, DateTimeOffset? UltimoEnvio);

/// <summary>Comunicação HTTPS com a plataforma. Contrato em docs/API.md.</summary>
public sealed class ClientePlataforma(HttpClient http)
{
    public async Task<ResultadoEnvio> EnviarExameAsync(
        string urlBase, string token, byte[] pdf, string nomeArquivo, string sha256, string metadadosJson, CancellationToken ct)
    {
        using var conteudo = new MultipartFormDataContent();
        var arquivo = new ByteArrayContent(pdf);
        arquivo.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        conteudo.Add(arquivo, "arquivo", nomeArquivo);
        conteudo.Add(new StringContent(metadadosJson, Encoding.UTF8, "application/json"), "metadados");

        using var req = new HttpRequestMessage(HttpMethod.Post, Url(urlBase, "integrador-exames")) { Content = conteudo };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        req.Headers.Add("Idempotency-Key", sha256);

        try
        {
            using var resp = await http.SendAsync(req, ct);
            var corpo = await resp.Content.ReadAsStringAsync(ct);
            var (id, msg) = LerResposta(corpo);

            return resp.StatusCode switch
            {
                HttpStatusCode.Created => new(TipoResultado.Criado, id, msg),
                HttpStatusCode.OK => new(TipoResultado.Duplicado, id, msg),
                HttpStatusCode.Conflict => new(TipoResultado.Duplicado, id, msg),
                HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => new(TipoResultado.NaoAutorizado, null, msg ?? "Token inválido ou revogado"),
                HttpStatusCode.TooManyRequests or HttpStatusCode.RequestTimeout => new(TipoResultado.FalhaTemporaria, null, msg ?? resp.ReasonPhrase),
                _ when (int)resp.StatusCode >= 500 => new(TipoResultado.FalhaTemporaria, null, $"Servidor {(int)resp.StatusCode}: {msg ?? resp.ReasonPhrase}"),
                _ => new(TipoResultado.Rejeitado, null, $"{(int)resp.StatusCode}: {msg ?? resp.ReasonPhrase}"),
            };
        }
        catch (HttpRequestException ex)
        {
            return new(TipoResultado.FalhaTemporaria, null, ex.Message);
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            return new(TipoResultado.FalhaTemporaria, null, "Tempo de resposta esgotado");
        }
    }

    /// <summary>Valida o token e devolve o nome do equipamento/unidade cadastrado na plataforma.</summary>
    public async Task<(bool Ok, string Mensagem)> TestarConexaoAsync(string urlBase, string token, CancellationToken ct)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, Url(urlBase, "integrador-status"));
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var resp = await http.SendAsync(req, ct);
            var corpo = await resp.Content.ReadAsStringAsync(ct);
            if (!resp.IsSuccessStatusCode)
                return (false, resp.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden
                    ? "Token inválido ou revogado."
                    : $"A plataforma respondeu {(int)resp.StatusCode}.");

            using var doc = JsonDocument.Parse(corpo);
            var equipamento = doc.RootElement.TryGetProperty("equipamento", out var e) ? e.GetString() : null;
            var unidade = doc.RootElement.TryGetProperty("unidade", out var u) ? u.GetString() : null;
            return (true, $"Conectado: {equipamento ?? "equipamento"} — {unidade ?? "unidade"}");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or UriFormatException)
        {
            return (false, $"Não foi possível conectar: {ex.Message}");
        }
    }

    public async Task<bool> EnviarHeartbeatAsync(string urlBase, string token, DadosHeartbeat dados, CancellationToken ct)
    {
        try
        {
            var json = JsonSerializer.Serialize(new
            {
                versao_integrador = dados.VersaoIntegrador,
                computador = dados.Computador,
                pendentes = dados.Pendentes,
                rejeitados = dados.Rejeitados,
                ultimo_envio = dados.UltimoEnvio,
            });
            using var req = new HttpRequestMessage(HttpMethod.Post, Url(urlBase, "integrador-status"))
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            };
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var resp = await http.SendAsync(req, ct);
            return resp.IsSuccessStatusCode;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return false;
        }
    }

    private static Uri Url(string urlBase, string rota) => new($"{urlBase.TrimEnd('/')}/{rota}");

    private static (string? Id, string? Mensagem) LerResposta(string corpo)
    {
        if (string.IsNullOrWhiteSpace(corpo)) return (null, null);
        try
        {
            using var doc = JsonDocument.Parse(corpo);
            var raiz = doc.RootElement;
            if (raiz.ValueKind != JsonValueKind.Object) return (null, null);
            var id = raiz.TryGetProperty("id", out var i) ? i.ToString() : null;
            var msg = raiz.TryGetProperty("mensagem", out var m) ? m.GetString()
                    : raiz.TryGetProperty("error", out var er) ? er.ToString() : null;
            return (id, msg);
        }
        catch (JsonException)
        {
            return (null, corpo.Length > 200 ? corpo[..200] : corpo);
        }
    }
}
