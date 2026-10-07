using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using TwdIntegrador.Core.Conectores;
using TwdIntegrador.Core.Configuracao;
using TwdIntegrador.Core.Envio;
using TwdIntegrador.Core.Fila;
using TwdIntegrador.Core.Seguranca;
using TwdIntegrador.Core.Servicos;

namespace TwdIntegrador.Core.Tests;

public sealed class FluxoCapturaEnvioTests : IDisposable
{
    private readonly string _raiz = Path.Combine(Path.GetTempPath(), "twd-testes-" + Guid.NewGuid().ToString("N"));
    private readonly string _pastaWinCardio;
    private readonly ConfiguracaoStore _store;
    private readonly FilaEnvio _fila;
    private readonly EventosIntegrador _eventos = new();
    private readonly ServidorFalso _servidor = new();
    private readonly ProcessadorCaptura _processador;
    private readonly EnviadorFila _enviador;
    private readonly IntegradorOptions _opcoes;
    private readonly FonteOptions _fonte;

    public FluxoCapturaEnvioTests()
    {
        _pastaWinCardio = Directory.CreateDirectory(Path.Combine(_raiz, "wincardio")).FullName;
        var protetor = new ProtetorXorDeTeste();
        _store = new ConfiguracaoStore(Path.Combine(_raiz, "dados"), protetor);
        _store.SalvarToken("twd_eq_teste");
        _fila = new FilaEnvio(_store.CaminhoBanco);
        _fonte = new FonteOptions { Pasta = _pastaWinCardio };
        _opcoes = new IntegradorOptions { PlataformaUrl = "https://plataforma.teste/functions/v1", Fontes = [_fonte] };

        _processador = new ProcessadorCaptura(_fila, RegistroConectores.Padrao(), protetor, _store, _eventos,
            TimeProvider.System, NullLogger<ProcessadorCaptura>.Instance)
        {
            IntervaloEstabilidade = TimeSpan.FromMilliseconds(20),
            LimiteEstabilidade = TimeSpan.FromSeconds(5),
        };
        _enviador = new EnviadorFila(_fila, new ClientePlataforma(new HttpClient(_servidor)), protetor, _store, _eventos,
            TimeProvider.System, NullLogger<EnviadorFila>.Instance);
    }

    private string GravarPdf(string nome, byte[] conteudo)
    {
        var caminho = Path.Combine(_pastaWinCardio, nome);
        File.WriteAllBytes(caminho, conteudo);
        return caminho;
    }

    [Fact]
    public async Task Captura_envia_e_exclui_o_original_e_a_copia()
    {
        var pdf = PdfSintetico.WinCardio();
        var caminho = GravarPdf("exame-1.pdf", pdf);

        Assert.Equal(ResultadoCaptura.Enfileirado, await _processador.CapturarAsync(caminho, _fonte, _opcoes, default));
        var item = Assert.Single(_fila.ListarRecentes());
        Assert.True(File.Exists(item.CaminhoCopia));
        Assert.NotEqual(File.ReadAllBytes(caminho), File.ReadAllBytes(item.CaminhoCopia)); // cópia criptografada

        Assert.Equal(1, await _enviador.EnviarPendentesAsync(_opcoes, default));

        var req = Assert.Single(_servidor.Requisicoes);
        Assert.Equal("Bearer twd_eq_teste", req.Autorizacao);
        Assert.Equal(item.Sha256, req.IdempotencyKey);
        Assert.Equal(pdf, req.Pdf);
        using var meta = JsonDocument.Parse(req.Metadados);
        Assert.Equal("12142", meta.RootElement.GetProperty("exame").GetProperty("numero").GetString());
        Assert.Equal("MARIA FICTICIA DA SILVA", meta.RootElement.GetProperty("exame").GetProperty("paciente").GetProperty("nome").GetString());
        Assert.Equal("wincardio", meta.RootElement.GetProperty("origem").GetProperty("conector").GetString());

        Assert.False(File.Exists(caminho), "PDF original deve sair do computador após envio confirmado");
        Assert.False(File.Exists(item.CaminhoCopia));
        Assert.Equal(StatusItem.Enviado, _fila.ObterPorSha(item.Sha256)!.Status);
    }

    [Fact]
    public async Task Mesmo_arquivo_capturado_de_novo_nao_e_enviado_duas_vezes()
    {
        var pdf = PdfSintetico.WinCardio();
        var c1 = GravarPdf("exame-1.pdf", pdf);
        await _processador.CapturarAsync(c1, _fonte, _opcoes, default);
        await _enviador.EnviarPendentesAsync(_opcoes, default);

        // O técnico copiou o mesmo PDF de volta para a pasta, com outro nome.
        var c2 = GravarPdf("copia.pdf", pdf);
        Assert.Equal(ResultadoCaptura.JaConhecido, await _processador.CapturarAsync(c2, _fonte, _opcoes, default));
        Assert.Equal(0, await _enviador.EnviarPendentesAsync(_opcoes, default));
        Assert.Single(_servidor.Requisicoes);
        Assert.False(File.Exists(c2), "cópia de exame já enviado é removida");
    }

    [Fact]
    public async Task Sem_internet_mantem_na_fila_e_reenvia_depois()
    {
        var caminho = GravarPdf("exame-1.pdf", PdfSintetico.WinCardio());
        await _processador.CapturarAsync(caminho, _fonte, _opcoes, default);

        _servidor.Falhar = true;
        Assert.Equal(0, await _enviador.EnviarPendentesAsync(_opcoes, default));
        Assert.Equal(EstadoIntegrador.SemConexao, _eventos.Estado);
        var item = Assert.Single(_fila.ListarRecentes());
        Assert.Equal(StatusItem.Pendente, item.Status);
        Assert.Equal(1, item.Tentativas);
        Assert.True(File.Exists(caminho), "original não pode ser apagado antes do envio");

        _servidor.Falhar = false;
        _fila.AnteciparPendentes(DateTimeOffset.UtcNow);
        Assert.Equal(1, await _enviador.EnviarPendentesAsync(_opcoes, default));
        Assert.False(File.Exists(caminho));
    }

    [Fact]
    public async Task Duplicado_na_plataforma_conta_como_sucesso()
    {
        _servidor.Status = HttpStatusCode.OK;
        var caminho = GravarPdf("exame-1.pdf", PdfSintetico.WinCardio());
        await _processador.CapturarAsync(caminho, _fonte, _opcoes, default);
        Assert.Equal(1, await _enviador.EnviarPendentesAsync(_opcoes, default));
        Assert.False(File.Exists(caminho));
    }

    [Fact]
    public async Task Token_invalido_para_o_envio_e_avisa()
    {
        _servidor.Status = HttpStatusCode.Unauthorized;
        await _processador.CapturarAsync(GravarPdf("a.pdf", PdfSintetico.WinCardio(exame: "1")), _fonte, _opcoes, default);
        await _processador.CapturarAsync(GravarPdf("b.pdf", PdfSintetico.WinCardio(exame: "2")), _fonte, _opcoes, default);

        Assert.Equal(0, await _enviador.EnviarPendentesAsync(_opcoes, default));
        Assert.Equal(EstadoIntegrador.TokenInvalido, _eventos.Estado);
        Assert.Single(_servidor.Requisicoes); // não insiste com o segundo exame
        Assert.All(_fila.ListarRecentes(), i => Assert.Equal(StatusItem.Pendente, i.Status));
    }

    [Fact]
    public async Task Nome_reaproveitado_pelo_wincardio_nao_apaga_exame_novo()
    {
        var caminho = GravarPdf("exame-1.pdf", PdfSintetico.WinCardio(exame: "100"));
        await _processador.CapturarAsync(caminho, _fonte, _opcoes, default);

        // Antes do envio terminar, o WinCardio sobrescreve exame-1.pdf com outro exame.
        GravarPdf("exame-1.pdf", PdfSintetico.WinCardio(exame: "101"));
        await _enviador.EnviarPendentesAsync(_opcoes, default);

        Assert.True(File.Exists(caminho), "o exame 101 ainda não foi enviado e não pode ser apagado");
    }

    [Fact]
    public async Task Arquivos_anteriores_a_instalacao_sao_ignorados()
    {
        var caminho = GravarPdf("antigo.pdf", PdfSintetico.WinCardio());
        File.SetLastWriteTimeUtc(caminho, DateTime.UtcNow.AddDays(-10));
        _fonte.IgnorarArquivosAnterioresA = DateTimeOffset.UtcNow.AddDays(-1);

        Assert.Equal(ResultadoCaptura.Ignorado, await _processador.CapturarAsync(caminho, _fonte, _opcoes, default));
        Assert.Empty(_fila.ListarRecentes());
    }

    [Fact]
    public async Task Pdf_incompleto_nao_e_capturado()
    {
        var pdf = PdfSintetico.WinCardio();
        var caminho = GravarPdf("gravando.pdf", pdf[..(pdf.Length / 2)]);
        var processador = new ProcessadorCaptura(_fila, RegistroConectores.Padrao(), new ProtetorSemCriptografia(), _store,
            _eventos, TimeProvider.System, NullLogger<ProcessadorCaptura>.Instance)
        {
            IntervaloEstabilidade = TimeSpan.FromMilliseconds(20),
            LimiteEstabilidade = TimeSpan.FromMilliseconds(200),
        };
        Assert.Equal(ResultadoCaptura.Falhou, await processador.CapturarAsync(caminho, _fonte, _opcoes, default));
        Assert.Empty(_fila.ListarRecentes());
    }

    [Theory]
    [InlineData(0, 30)]
    [InlineData(1, 60)]
    [InlineData(3, 240)]
    [InlineData(20, 3600)]
    public void Espera_entre_tentativas_cresce_ate_uma_hora(int tentativas, int segundos) =>
        Assert.Equal(TimeSpan.FromSeconds(segundos), EnviadorFila.CalcularEspera(tentativas));

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_raiz, recursive: true); } catch (IOException) { }
    }

    private sealed class ProtetorXorDeTeste : IProtetorDados
    {
        public byte[] Proteger(byte[] dados) => dados.Select(b => (byte)(b ^ 0x5A)).ToArray();
        public byte[] Desproteger(byte[] dados) => Proteger(dados);
    }

    private sealed record Requisicao(string? Autorizacao, string? IdempotencyKey, byte[] Pdf, string Metadados);

    private sealed class ServidorFalso : HttpMessageHandler
    {
        public List<Requisicao> Requisicoes { get; } = [];
        public HttpStatusCode Status { get; set; } = HttpStatusCode.Created;
        public bool Falhar { get; set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (Falhar) throw new HttpRequestException("Sem conexão (simulado)");

            var multipart = (MultipartFormDataContent)request.Content!;
            byte[] pdf = [];
            var meta = "";
            foreach (var parte in multipart)
            {
                var nome = parte.Headers.ContentDisposition?.Name?.Trim('"');
                if (nome == "arquivo") pdf = await parte.ReadAsByteArrayAsync(ct);
                if (nome == "metadados") meta = await parte.ReadAsStringAsync(ct);
            }
            Requisicoes.Add(new Requisicao(
                request.Headers.Authorization?.ToString(),
                request.Headers.TryGetValues("Idempotency-Key", out var v) ? v.Single() : null,
                pdf, meta));

            return new HttpResponseMessage(Status)
            {
                Content = new StringContent("""{"id":"3f0c1a2e-0000-4000-8000-000000000001","mensagem":"ok"}"""),
            };
        }
    }
}
