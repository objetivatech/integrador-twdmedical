using System.Text.Json;
using System.Text.Json.Serialization;
using TwdIntegrador.Core.Conectores;

namespace TwdIntegrador.Core.Envio;

/// <summary>
/// Corpo JSON enviado junto com o PDF (campo "metadados" do multipart).
/// Contrato documentado em docs/API.md — mantenha os dois em sincronia.
/// </summary>
public sealed record MetadadosEnvio
{
    public const int VersaoContrato = 1;

    [JsonPropertyName("versao_contrato")] public int Versao { get; init; } = VersaoContrato;
    [JsonPropertyName("arquivo")] public required ArquivoInfo Arquivo { get; init; }
    [JsonPropertyName("origem")] public required OrigemInfo Origem { get; init; }
    [JsonPropertyName("exame")] public required ExameInfo Exame { get; init; }
    [JsonPropertyName("texto_extraido")] public string? TextoExtraido { get; init; }

    public sealed record ArquivoInfo(
        [property: JsonPropertyName("nome")] string Nome,
        [property: JsonPropertyName("sha256")] string Sha256,
        [property: JsonPropertyName("tamanho")] long Tamanho);

    public sealed record OrigemInfo(
        [property: JsonPropertyName("conector")] string Conector,
        [property: JsonPropertyName("fonte")] string Fonte,
        [property: JsonPropertyName("computador")] string Computador,
        [property: JsonPropertyName("versao_integrador")] string VersaoIntegrador,
        [property: JsonPropertyName("capturado_em")] DateTimeOffset CapturadoEm);

    public sealed record ExameInfo
    {
        [JsonPropertyName("tipo")] public string? Tipo { get; init; }
        [JsonPropertyName("numero")] public string? Numero { get; init; }
        [JsonPropertyName("data_hora")] public DateTimeOffset? DataHora { get; init; }
        [JsonPropertyName("paciente")] public PacienteInfo Paciente { get; init; } = new();
        [JsonPropertyName("convenio")] public string? Convenio { get; init; }
        [JsonPropertyName("frequencia_cardiaca")] public int? FrequenciaCardiaca { get; init; }
        [JsonPropertyName("serial_equipamento")] public string? SerialEquipamento { get; init; }
        [JsonPropertyName("fabricante")] public string? Fabricante { get; init; }
        [JsonPropertyName("extras")] public Dictionary<string, string>? Extras { get; init; }
        [JsonPropertyName("avisos")] public List<string>? Avisos { get; init; }
    }

    public sealed record PacienteInfo
    {
        [JsonPropertyName("nome")] public string? Nome { get; init; }
        [JsonPropertyName("data_nascimento")] public DateOnly? DataNascimento { get; init; }
        [JsonPropertyName("idade")] public int? Idade { get; init; }
        [JsonPropertyName("sexo")] public string? Sexo { get; init; }
        [JsonPropertyName("cpf")] public string? Cpf { get; init; }
        [JsonPropertyName("rg")] public string? Rg { get; init; }
        [JsonPropertyName("registro_clinico")] public string? RegistroClinico { get; init; }
    }

    public static ExameInfo DeDados(DadosExame d) => new()
    {
        Tipo = d.TipoExame,
        Numero = d.NumeroExame,
        DataHora = d.DataHoraExame,
        Paciente = new PacienteInfo
        {
            Nome = d.Paciente,
            DataNascimento = d.DataNascimento,
            Idade = d.Idade,
            Sexo = d.Sexo,
            Cpf = d.Cpf,
            Rg = d.Rg,
            RegistroClinico = d.RegistroClinico,
        },
        Convenio = d.Convenio,
        FrequenciaCardiaca = d.FrequenciaCardiaca,
        SerialEquipamento = d.SerialEquipamento,
        Fabricante = d.Fabricante,
        Extras = d.Extras.Count > 0 ? d.Extras : null,
        Avisos = d.Avisos.Count > 0 ? d.Avisos : null,
    };

    public static readonly JsonSerializerOptions Json = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public string ParaJson() => JsonSerializer.Serialize(this, Json);
}
