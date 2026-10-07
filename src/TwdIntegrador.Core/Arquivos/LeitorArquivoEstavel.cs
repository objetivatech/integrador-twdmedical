namespace TwdIntegrador.Core.Arquivos;

/// <summary>
/// O WinCardio grava o PDF aos poucos. Antes de ler, esperamos o arquivo parar de crescer
/// e ficar livre para leitura exclusiva (sem outro programa escrevendo nele).
/// </summary>
public static class LeitorArquivoEstavel
{
    public static async Task<byte[]?> LerQuandoEstavelAsync(
        string caminho,
        TimeSpan intervalo,
        TimeSpan limite,
        CancellationToken ct)
    {
        var inicio = DateTime.UtcNow;
        long tamanhoAnterior = -1;

        while (DateTime.UtcNow - inicio < limite)
        {
            ct.ThrowIfCancellationRequested();
            if (!File.Exists(caminho)) return null;

            long tamanho;
            try { tamanho = new FileInfo(caminho).Length; }
            catch (IOException) { tamanho = -1; }

            if (tamanho > 0 && tamanho == tamanhoAnterior)
            {
                try
                {
                    await using var fs = new FileStream(caminho, FileMode.Open, FileAccess.Read, FileShare.None);
                    var buffer = new byte[fs.Length];
                    await fs.ReadExactlyAsync(buffer, ct);
                    if (PareceCompleto(buffer)) return buffer;
                }
                catch (IOException)
                {
                    // Ainda em uso por outro processo; tenta de novo.
                }
                catch (UnauthorizedAccessException)
                {
                    // Idem (o Windows às vezes reporta arquivo bloqueado assim).
                }
            }

            tamanhoAnterior = tamanho;
            await Task.Delay(intervalo, ct);
        }
        return null;
    }

    /// <summary>Um PDF completo termina com o marcador %%EOF (eventualmente seguido de espaços).</summary>
    internal static bool PareceCompleto(byte[] pdf)
    {
        if (pdf.Length < 8 || pdf[0] != '%' || pdf[1] != 'P' || pdf[2] != 'D' || pdf[3] != 'F') return false;
        var cauda = System.Text.Encoding.ASCII.GetString(pdf, Math.Max(0, pdf.Length - 1024), Math.Min(1024, pdf.Length));
        return cauda.Contains("%%EOF", StringComparison.Ordinal);
    }
}
