using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using TwdIntegrador.Core.Arquivos;
using TwdIntegrador.Core.Conectores;
using TwdIntegrador.Core.Configuracao;
using TwdIntegrador.Core.Envio;
using TwdIntegrador.Core.Fila;
using TwdIntegrador.Core.Pdf;
using TwdIntegrador.Core.Seguranca;

namespace TwdIntegrador.Core.Servicos;

public enum ResultadoCaptura
{
    Enfileirado,
    JaConhecido,
    Ignorado,
    Falhou,
}

/// <summary>
/// Transforma um PDF que apareceu na pasta em um item da fila: espera o arquivo ficar
/// completo, calcula o SHA-256, lê os dados com o conector e guarda uma cópia criptografada.
/// </summary>
public sealed class ProcessadorCaptura(
    FilaEnvio fila,
    RegistroConectores conectores,
    IProtetorDados protetor,
    ConfiguracaoStore store,
    EventosIntegrador eventos,
    TimeProvider relogio,
    ILogger<ProcessadorCaptura> log)
{
    public TimeSpan IntervaloEstabilidade { get; init; } = TimeSpan.FromSeconds(1);
    public TimeSpan LimiteEstabilidade { get; init; } = TimeSpan.FromMinutes(2);

    public async Task<ResultadoCaptura> CapturarAsync(string caminho, FonteOptions fonte, IntegradorOptions opcoes, CancellationToken ct)
    {
        var info = new FileInfo(caminho);
        if (!info.Exists) return ResultadoCaptura.Ignorado;
        if (fonte.IgnorarArquivosAnterioresA is { } corte && info.LastWriteTimeUtc < corte.UtcDateTime)
        {
            return ResultadoCaptura.Ignorado;
        }

        var pdf = await LeitorArquivoEstavel.LerQuandoEstavelAsync(caminho, IntervaloEstabilidade, LimiteEstabilidade, ct);
        if (pdf is null)
        {
            log.LogWarning("Arquivo {Arquivo} não ficou completo/legível a tempo; será tentado na próxima varredura", caminho);
            return ResultadoCaptura.Falhou;
        }

        var sha = Convert.ToHexStringLower(SHA256.HashData(pdf));
        var existente = fila.ObterPorSha(sha);
        if (existente is not null)
        {
            // Já capturado antes. Se já foi enviado e o original ainda está aí (ex.: falhou ao excluir), finaliza agora.
            if (existente.Status == StatusItem.Enviado)
                AcaoAposEnvio.Aplicar(caminho, sha, opcoes, log);
            return ResultadoCaptura.JaConhecido;
        }

        var conector = conectores.Obter(fonte.Conector);
        TextoPdf texto;
        DadosExame dados;
        try
        {
            texto = ExtratorTextoPdf.Extrair(pdf);
            dados = conector.Interpretar(texto);
        }
        catch (Exception ex)
        {
            // PDF que não conseguimos ler ainda é enviado: a plataforma guarda o arquivo e alguém confere.
            log.LogWarning(ex, "Não foi possível ler o texto de {Arquivo}; enviando somente o PDF", caminho);
            texto = new TextoPdf([], 0);
            dados = new DadosExame { Avisos = ["Texto do PDF não pôde ser lido"] };
        }

        Directory.CreateDirectory(store.PastaFila);
        var copia = Path.Combine(store.PastaFila, sha + ".bin");
        await File.WriteAllBytesAsync(copia, protetor.Proteger(pdf), ct);

        var agora = relogio.GetLocalNow();
        var metadados = new MetadadosEnvio
        {
            Arquivo = new(info.Name, sha, pdf.LongLength),
            Origem = new(conector.Id, fonte.Nome, Environment.MachineName, VersaoApp.Atual, agora),
            Exame = MetadadosEnvio.DeDados(dados),
            TextoExtraido = texto.Vazio ? null : texto.TextoCompleto,
        };

        var item = new ItemFila
        {
            Sha256 = sha,
            FonteId = fonte.Id,
            Conector = conector.Id,
            CaminhoOriginal = info.FullName,
            NomeArquivo = info.Name,
            Tamanho = pdf.LongLength,
            CaminhoCopia = copia,
            MetadadosJson = metadados.ParaJson(),
            Paciente = dados.Paciente,
            NumeroExame = dados.NumeroExame,
            CapturadoEm = agora,
        };

        if (!fila.Enfileirar(item)) return ResultadoCaptura.JaConhecido;

        log.LogInformation("Exame capturado: {Arquivo} (exame {Numero}, {Avisos} aviso(s))",
            info.Name, dados.NumeroExame ?? "?", dados.Avisos.Count);
        eventos.NotificarCapturado(fila.ObterPorSha(sha) ?? item);
        return ResultadoCaptura.Enfileirado;
    }
}

public static class AcaoAposEnvio
{
    /// <summary>
    /// Exclui/move o PDF original — mas só se o arquivo no caminho ainda for o mesmo que foi enviado.
    /// O WinCardio pode reaproveitar nomes (exame-1.pdf), e nunca podemos apagar um exame não enviado.
    /// </summary>
    public static void Aplicar(string caminho, string sha256, IntegradorOptions opcoes, ILogger log)
    {
        if (opcoes.AposEnvio == Configuracao.AcaoAposEnvio.Manter || string.IsNullOrEmpty(caminho) || !File.Exists(caminho))
            return;
        try
        {
            using (var fs = new FileStream(caminho, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                if (Convert.ToHexStringLower(SHA256.HashData(fs)) != sha256) return;
            }

            if (opcoes.AposEnvio == Configuracao.AcaoAposEnvio.Mover && !string.IsNullOrWhiteSpace(opcoes.PastaEnviados))
            {
                Directory.CreateDirectory(opcoes.PastaEnviados);
                var destino = Path.Combine(opcoes.PastaEnviados, Path.GetFileName(caminho));
                if (File.Exists(destino))
                    destino = Path.Combine(opcoes.PastaEnviados, $"{Path.GetFileNameWithoutExtension(caminho)}-{sha256[..8]}.pdf");
                File.Move(caminho, destino);
            }
            else
            {
                File.Delete(caminho);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            log.LogWarning(ex, "Não foi possível remover {Arquivo} após o envio; nova tentativa na próxima varredura", caminho);
        }
    }
}

public static class VersaoApp
{
    public static string Atual { get; } =
        typeof(VersaoApp).Assembly
            .GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
            .OfType<System.Reflection.AssemblyInformationalVersionAttribute>()
            .FirstOrDefault()?.InformationalVersion.Split('+')[0] ?? "0.0.0";
}
