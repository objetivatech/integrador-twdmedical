namespace TwdIntegrador.Core.Conectores;

/// <summary>
/// Dados lidos do PDF. Todos os campos são opcionais: cada programa (e cada clínica)
/// preenche o que tiver, e o que existir é enviado para a plataforma.
/// </summary>
public sealed record DadosExame
{
    public string? TipoExame { get; init; }
    public string? NumeroExame { get; init; }
    public DateTimeOffset? DataHoraExame { get; init; }

    public string? Paciente { get; init; }
    public DateOnly? DataNascimento { get; init; }
    public int? Idade { get; init; }
    public string? Sexo { get; init; }
    public string? Cpf { get; init; }
    public string? Rg { get; init; }
    public string? RegistroClinico { get; init; }
    public string? Convenio { get; init; }

    public int? FrequenciaCardiaca { get; init; }
    public string? SerialEquipamento { get; init; }
    public string? Fabricante { get; init; }

    /// <summary>Qualquer outro campo reconhecido que não tenha lugar acima.</summary>
    public Dictionary<string, string> Extras { get; init; } = [];

    /// <summary>Campos que o conector esperava e não encontrou (para diagnóstico).</summary>
    public List<string> Avisos { get; init; } = [];
}
