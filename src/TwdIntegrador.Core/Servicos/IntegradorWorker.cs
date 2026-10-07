using System.Threading.Channels;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TwdIntegrador.Core.Configuracao;
using TwdIntegrador.Core.Envio;
using TwdIntegrador.Core.Fila;

namespace TwdIntegrador.Core.Servicos;

/// <summary>
/// Serviço de fundo. Três laços independentes:
///  1. Captura: FileSystemWatcher (tempo real) + varredura periódica (rede de segurança).
///  2. Envio: esvazia a fila sempre que algo é capturado ou a cada 15s.
///  3. Heartbeat: avisa a plataforma que este computador está online.
/// </summary>
public sealed class IntegradorWorker(
    ConfiguracaoStore store,
    ProcessadorCaptura processador,
    EnviadorFila enviador,
    FilaEnvio fila,
    ClientePlataforma cliente,
    EventosIntegrador eventos,
    TimeProvider relogio,
    ILogger<IntegradorWorker> log) : BackgroundService
{
    private readonly Channel<(string Caminho, FonteOptions Fonte)> _capturas =
        Channel.CreateUnbounded<(string, FonteOptions)>(new UnboundedChannelOptions { SingleReader = true });
    private readonly SemaphoreSlim _sinalEnvio = new(0);
    private readonly List<FileSystemWatcher> _watchers = [];
    // Evita reler/rehashear o mesmo arquivo a cada varredura (caminho -> data+tamanho já processados).
    private readonly Dictionary<string, (DateTime, long)> _jaVistos = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _lockVistos = new();

    private volatile IntegradorOptions _opcoes = new();
    private volatile bool _pausado;

    public bool Pausado => _pausado;

    public void Pausar(bool pausar)
    {
        _pausado = pausar;
        if (pausar) eventos.DefinirEstado(EstadoIntegrador.Pausado);
        else { AtualizarEstado(); SolicitarEnvio(); }
    }

    /// <summary>Pede um ciclo de envio imediato (ex.: após corrigir o token).</summary>
    public void SolicitarEnvio()
    {
        fila.AnteciparPendentes(relogio.GetUtcNow());
        _sinalEnvio.Release();
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        Aplicar(store.Carregar());
        store.Alterada += opcoes => { Aplicar(opcoes); SolicitarEnvio(); };

        await Task.WhenAll(
            LacoCapturaAsync(ct),
            LacoVarreduraAsync(ct),
            LacoEnvioAsync(ct),
            LacoHeartbeatAsync(ct),
            LacoManutencaoAsync(ct));
    }

    private void Aplicar(IntegradorOptions opcoes)
    {
        _opcoes = opcoes;
        lock (_watchers)
        {
            foreach (var w in _watchers) w.Dispose();
            _watchers.Clear();

            foreach (var fonte in opcoes.Fontes.Where(f => f.Ativa))
            {
                if (!Directory.Exists(fonte.Pasta))
                {
                    log.LogWarning("Pasta monitorada não existe: {Pasta}", fonte.Pasta);
                    continue;
                }
                var w = new FileSystemWatcher(fonte.Pasta, fonte.Filtro)
                {
                    IncludeSubdirectories = fonte.IncluirSubpastas,
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
                    InternalBufferSize = 64 * 1024,
                };
                var f = fonte;
                w.Created += (_, e) => _capturas.Writer.TryWrite((e.FullPath, f));
                w.Changed += (_, e) => _capturas.Writer.TryWrite((e.FullPath, f));
                w.Renamed += (_, e) => _capturas.Writer.TryWrite((e.FullPath, f));
                w.Error += (_, e) => log.LogWarning(e.GetException(), "Monitor de pasta perdeu eventos; a varredura cobre");
                w.EnableRaisingEvents = true;
                _watchers.Add(w);
                log.LogInformation("Monitorando {Pasta} ({Filtro}) com o conector {Conector}", fonte.Pasta, fonte.Filtro, fonte.Conector);
            }
        }
        AtualizarEstado();
    }

    private void AtualizarEstado()
    {
        if (_pausado) { eventos.DefinirEstado(EstadoIntegrador.Pausado); return; }
        var o = _opcoes;
        string? falta =
            string.IsNullOrWhiteSpace(o.PlataformaUrl) ? "Endereço da plataforma não configurado" :
            string.IsNullOrWhiteSpace(store.LerToken()) ? "Token do equipamento não configurado" :
            !o.Fontes.Any(f => f.Ativa && Directory.Exists(f.Pasta)) ? "Nenhuma pasta válida para monitorar" :
            null;
        if (falta is not null) eventos.DefinirEstado(EstadoIntegrador.NaoConfigurado, falta);
        else if (eventos.Estado is EstadoIntegrador.Iniciando or EstadoIntegrador.NaoConfigurado or EstadoIntegrador.Pausado or EstadoIntegrador.TokenInvalido)
            eventos.DefinirEstado(EstadoIntegrador.Ativo);
    }

    private async Task LacoCapturaAsync(CancellationToken ct)
    {
        await foreach (var (caminho, fonte) in _capturas.Reader.ReadAllAsync(ct))
        {
            if (_pausado) continue; // a varredura pega de novo quando despausar
            FileInfo info;
            try { info = new FileInfo(caminho); if (!info.Exists) continue; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { continue; }

            var assinatura = (info.LastWriteTimeUtc, info.Length);
            lock (_lockVistos)
            {
                if (_jaVistos.TryGetValue(caminho, out var vista) && vista == assinatura) continue;
            }

            try
            {
                var r = await processador.CapturarAsync(caminho, fonte, _opcoes, ct);
                if (r != ResultadoCaptura.Falhou)
                {
                    lock (_lockVistos) _jaVistos[caminho] = assinatura;
                }
                if (r == ResultadoCaptura.Enfileirado) _sinalEnvio.Release();
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                log.LogError(ex, "Erro ao capturar {Arquivo}", caminho);
            }
        }
    }

    private async Task LacoVarreduraAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            if (!_pausado)
            {
                foreach (var fonte in _opcoes.Fontes.Where(f => f.Ativa && Directory.Exists(f.Pasta)))
                {
                    try
                    {
                        var opcao = fonte.IncluirSubpastas ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
                        foreach (var arquivo in Directory.EnumerateFiles(fonte.Pasta, fonte.Filtro, opcao))
                            _capturas.Writer.TryWrite((arquivo, fonte));
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        log.LogWarning(ex, "Falha ao varrer {Pasta}", fonte.Pasta);
                    }
                }
            }
            await Task.Delay(TimeSpan.FromSeconds(Math.Max(5, _opcoes.IntervaloVarreduraSegundos)), relogio, ct);
        }
    }

    private async Task LacoEnvioAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            if (!_pausado)
            {
                try
                {
                    await enviador.EnviarPendentesAsync(_opcoes, ct);
                    if (eventos.Estado == EstadoIntegrador.SemConexao && fila.Contar().Pendentes == 0)
                        eventos.DefinirEstado(EstadoIntegrador.Ativo);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    log.LogError(ex, "Erro no ciclo de envio");
                }
            }
            await _sinalEnvio.WaitAsync(TimeSpan.FromSeconds(15), ct);
        }
    }

    private async Task LacoHeartbeatAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var token = store.LerToken();
            if (!string.IsNullOrWhiteSpace(_opcoes.PlataformaUrl) && !string.IsNullOrWhiteSpace(token))
            {
                var c = fila.Contar();
                await cliente.EnviarHeartbeatAsync(_opcoes.PlataformaUrl, token,
                    new DadosHeartbeat(VersaoApp.Atual, Environment.MachineName, c.Pendentes, c.Rejeitados, enviador.UltimoEnvio), ct);
            }
            await Task.Delay(TimeSpan.FromSeconds(Math.Max(60, _opcoes.IntervaloHeartbeatSegundos)), relogio, ct);
        }
    }

    private async Task LacoManutencaoAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                // LGPD: o histórico local guarda dados do paciente só por 30 dias após o envio.
                fila.AnonimizarEnviadosAntigos(relogio.GetUtcNow(), TimeSpan.FromDays(30));
                lock (_lockVistos) if (_jaVistos.Count > 10_000) _jaVistos.Clear();
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                log.LogError(ex, "Erro na manutenção da fila");
            }
            await Task.Delay(TimeSpan.FromHours(6), relogio, ct);
        }
    }

    public override void Dispose()
    {
        lock (_watchers) foreach (var w in _watchers) w.Dispose();
        _sinalEnvio.Dispose();
        base.Dispose();
    }
}
