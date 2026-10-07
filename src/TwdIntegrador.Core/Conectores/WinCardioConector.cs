using System.Globalization;
using System.Text.RegularExpressions;
using TwdIntegrador.Core.Pdf;

namespace TwdIntegrador.Core.Conectores;

/// <summary>
/// Interpreta o PDF "ECG de Repouso" gerado pelo WinCardio (Micromed). Cabeçalho típico:
/// <code>
/// ECG de Repouso
/// Exame: 12142  Nome: IDENE MARCONATO          Reg.Clin.:            Nasc.: 17/12/1953
/// FC: 56 bpm  25 mm/s  10mm/mV  60Hz  Musc  RG:      CPF:            Data: 31/08/2026-20:44:53
/// ...derivações...
/// UNIMED                         &lt;- caixa de observação/convênio, sem rótulo
/// Ecg# 0002187791                                                     © MICROMED
/// </code>
/// Campos vazios no WinCardio simplesmente não são enviados.
/// </summary>
public sealed partial class WinCardioConector : IConector
{
    public string Id => "wincardio";
    public string Nome => "WinCardio (Micromed)";

    // Rótulo seguido de ":" — o valor é tudo até o próximo rótulo.
    [GeneratedRegex(@"\b(?<rotulo>Exame|Nome|Reg\.?\s*Cl[ií]n\.?|Nasc\.?|FC|RG|CPF|Data)\s*:", RegexOptions.IgnoreCase)]
    private static partial Regex RotuloRegex();

    [GeneratedRegex(@"^(?:D?I{1,3}|aV[RLF]|V[1-6])(?:\s+(?:D?I{1,3}|aV[RLF]|V[1-6]))*$", RegexOptions.IgnoreCase)]
    private static partial Regex LinhaDerivacaoRegex();

    [GeneratedRegex(@"Ecg#\s*(?<serial>\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex SerialRegex();

    [GeneratedRegex(@"©\s*(?<fabricante>[A-Za-z][\w\- ]*)")]
    private static partial Regex FabricanteRegex();

    [GeneratedRegex(@"(?<bpm>\d{1,3})\s*bpm", RegexOptions.IgnoreCase)]
    private static partial Regex BpmRegex();

    [GeneratedRegex(@"^(?:ECG|Holter|MAPA|Teste Ergom)", RegexOptions.IgnoreCase)]
    private static partial Regex TituloRegex();

    public DadosExame Interpretar(TextoPdf texto)
    {
        var campos = ExtrairCamposRotulados(texto.Linhas);
        var avisos = new List<string>();

        string? Campo(string chave) => campos.TryGetValue(chave, out var v) && v.Length > 0 ? v : null;

        var numero = Campo("exame");
        var nome = Campo("nome");
        if (numero is null) avisos.Add("Número do exame não encontrado");
        if (nome is null) avisos.Add("Nome do paciente não encontrado");

        var fc = Campo("fc");
        int? bpm = fc is not null && BpmRegex().Match(fc) is { Success: true } m ? int.Parse(m.Groups["bpm"].Value) : null;

        var serial = texto.Linhas.Select(l => SerialRegex().Match(l)).FirstOrDefault(r => r.Success)?.Groups["serial"].Value;
        var fabricante = texto.Linhas.Select(l => FabricanteRegex().Match(l)).FirstOrDefault(r => r.Success)?.Groups["fabricante"].Value.Trim();

        return new DadosExame
        {
            TipoExame = texto.Linhas.FirstOrDefault(l => TituloRegex().IsMatch(l))?.Trim() ?? "ECG",
            NumeroExame = numero,
            Paciente = nome,
            RegistroClinico = Campo("regclin"),
            DataNascimento = Normalizacao.Data(Campo("nasc")),
            Rg = Campo("rg"),
            Cpf = Normalizacao.Cpf(Campo("cpf")),
            DataHoraExame = Normalizacao.DataHora(Campo("data")),
            FrequenciaCardiaca = bpm,
            Convenio = ExtrairConvenio(texto.Linhas),
            SerialEquipamento = serial,
            Fabricante = fabricante,
            Avisos = avisos,
        };
    }

    private static Dictionary<string, string> ExtrairCamposRotulados(IEnumerable<string> linhas)
    {
        var campos = new Dictionary<string, string>();
        foreach (var linha in linhas)
        {
            var achados = RotuloRegex().Matches(linha);
            for (var i = 0; i < achados.Count; i++)
            {
                var inicio = achados[i].Index + achados[i].Length;
                var fim = i + 1 < achados.Count ? achados[i + 1].Index : linha.Length;
                var chave = ChaveCanonica(achados[i].Groups["rotulo"].Value);
                // Primeira ocorrência vence (o cabeçalho vem antes de qualquer outro texto).
                campos.TryAdd(chave, linha[inicio..fim].Trim());
            }
        }
        return campos;
    }

    private static string ChaveCanonica(string rotulo) =>
        rotulo.ToLowerInvariant().Replace(".", "").Replace(" ", "").Replace("í", "i");

    /// <summary>
    /// O convênio aparece numa caixa sem rótulo entre a última derivação (DII longo)
    /// e o rodapé "Ecg#". Pega o texto que estiver nesse intervalo.
    /// </summary>
    private static string? ExtrairConvenio(IReadOnlyList<string> linhas)
    {
        var rodape = -1;
        for (var i = 0; i < linhas.Count; i++)
        {
            if (SerialRegex().IsMatch(linhas[i])) { rodape = i; break; }
        }
        if (rodape <= 0) return null;

        var partes = new List<string>();
        for (var i = rodape - 1; i >= 0; i--)
        {
            var linha = linhas[i].Trim();
            if (LinhaDerivacaoRegex().IsMatch(linha) || RotuloRegex().IsMatch(linha)) break;
            partes.Insert(0, linha);
        }
        var convenio = string.Join(' ', partes).Trim();
        return convenio.Length > 0 ? convenio : null;
    }
}

internal static class Normalizacao
{
    private static readonly string[] FormatosData = ["dd/MM/yyyy", "d/M/yyyy", "dd-MM-yyyy", "dd/MM/yy"];
    private static readonly string[] FormatosDataHora =
    [
        "dd/MM/yyyy-HH:mm:ss", "dd/MM/yyyy HH:mm:ss", "dd/MM/yyyy-HH:mm", "dd/MM/yyyy HH:mm",
        "dd-MM-yyyy HH:mm:ss", "dd-MM-yyyy-HH:mm:ss",
    ];

    public static DateOnly? Data(string? valor) =>
        valor is not null && DateOnly.TryParseExact(valor.Trim(), FormatosData, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? d : null;

    /// <summary>Data/hora local do computador do consultório, com o fuso do Windows.</summary>
    public static DateTimeOffset? DataHora(string? valor)
    {
        if (valor is null) return null;
        if (!DateTime.TryParseExact(valor.Trim(), FormatosDataHora, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
            return null;
        return new DateTimeOffset(dt, TimeZoneInfo.Local.GetUtcOffset(dt));
    }

    /// <summary>Somente dígitos quando parecer um CPF; caso contrário mantém o texto original.</summary>
    public static string? Cpf(string? valor)
    {
        if (string.IsNullOrWhiteSpace(valor)) return null;
        var digitos = new string(valor.Where(char.IsDigit).ToArray());
        return digitos.Length == 11 ? digitos : valor.Trim();
    }
}
