using Microsoft.Extensions.Logging;
using TwdIntegrador.Core.Configuracao;
using TwdIntegrador.Core.Envio;
using TwdIntegrador.Core.Fila;
using TwdIntegrador.Core.Seguranca;

namespace TwdIntegrador.Core.Servicos;

/// <summary>Esvazia a fila: envia cada item e decide o que fazer conforme a resposta.</summary>
public sealed class EnviadorFila(
    FilaEnvio fila,
    ClientePlataforma cliente,
    IProtetorDados protetor,
    ConfiguracaoStore store,
    EventosIntegrador eventos,
    TimeProvider relogio,
    ILogger<EnviadorFila> log)
{
    private static readonly TimeSpan EsperaMinima = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan EsperaMaxima = TimeSpan.FromHours(1);

    public DateTimeOffset? UltimoEnvio { get; private set; }

    /// <summary>Envia o que estiver pronto. Retorna quantos itens foram concluídos com sucesso.</summary>
    public async Task<int> EnviarPendentesAsync(IntegradorOptions opcoes, CancellationToken ct)
    {
        var token = store.LerToken();
        if (string.IsNullOrWhiteSpace(opcoes.PlataformaUrl) || string.IsNullOrWhiteSpace(token)) return 0;

        var enviados = 0;
        foreach (var item in fila.ObterProntosParaEnvio(relogio.GetUtcNow()))
        {
            ct.ThrowIfCancellationRequested();

            byte[] pdf;
            try
            {
                pdf = protetor.Desproteger(await File.ReadAllBytesAsync(item.CaminhoCopia, ct));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.Cryptography.CryptographicException)
            {
                fila.MarcarRejeitado(item.Id, $"Cópia local ilegível: {ex.Message}");
                eventos.NotificarRejeitado(item, "Cópia local do exame não pôde ser lida");
                continue;
            }

            var resultado = await cliente.EnviarExameAsync(
                opcoes.PlataformaUrl, token, pdf, item.NomeArquivo, item.Sha256, item.MetadadosJson, ct);

            switch (resultado.Tipo)
            {
                case TipoResultado.Criado:
                case TipoResultado.Duplicado:
                    var agora = relogio.GetUtcNow();
                    fila.MarcarEnviado(item.Id, resultado.IdRemoto, agora);
                    TentarExcluir(item.CaminhoCopia);
                    AcaoAposEnvio.Aplicar(item.CaminhoOriginal, item.Sha256, opcoes, log);
                    UltimoEnvio = agora;
                    enviados++;
                    log.LogInformation("Exame {Arquivo} enviado ({Tipo}, id {Id})", item.NomeArquivo, resultado.Tipo, resultado.IdRemoto);
                    eventos.DefinirEstado(EstadoIntegrador.Ativo);
                    eventos.NotificarEnviado(fila.ObterPorSha(item.Sha256) ?? item);
                    break;

                case TipoResultado.NaoAutorizado:
                    fila.MarcarFalhaTemporaria(item.Id, resultado.Mensagem ?? "Não autorizado", relogio.GetUtcNow() + TimeSpan.FromMinutes(5));
                    log.LogError("Token recusado pela plataforma: {Msg}", resultado.Mensagem);
                    eventos.DefinirEstado(EstadoIntegrador.TokenInvalido, resultado.Mensagem);
                    return enviados; // Nenhum outro item vai passar com o mesmo token.

                case TipoResultado.Rejeitado:
                    fila.MarcarRejeitado(item.Id, resultado.Mensagem ?? "Rejeitado");
                    log.LogError("Exame {Arquivo} rejeitado: {Msg}", item.NomeArquivo, resultado.Mensagem);
                    eventos.NotificarRejeitado(item, resultado.Mensagem ?? "Rejeitado pela plataforma");
                    break;

                case TipoResultado.FalhaTemporaria:
                    var espera = CalcularEspera(item.Tentativas);
                    fila.MarcarFalhaTemporaria(item.Id, resultado.Mensagem ?? "Falha temporária", relogio.GetUtcNow() + espera);
                    log.LogWarning("Falha ao enviar {Arquivo} (tentativa {N}): {Msg}. Nova tentativa em {Espera}",
                        item.NomeArquivo, item.Tentativas + 1, resultado.Mensagem, espera);
                    eventos.DefinirEstado(EstadoIntegrador.SemConexao, resultado.Mensagem);
                    return enviados; // Provavelmente sem internet: não insiste nos demais agora.
            }
        }
        return enviados;
    }

    /// <summary>30s, 1min, 2min, 4min... até 1h.</summary>
    internal static TimeSpan CalcularEspera(int tentativasAnteriores)
    {
        var segundos = EsperaMinima.TotalSeconds * Math.Pow(2, Math.Min(tentativasAnteriores, 10));
        return TimeSpan.FromSeconds(Math.Min(segundos, EsperaMaxima.TotalSeconds));
    }

    private void TentarExcluir(string caminho)
    {
        try { File.Delete(caminho); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            log.LogWarning(ex, "Não foi possível excluir a cópia local {Copia}", caminho);
        }
    }
}
