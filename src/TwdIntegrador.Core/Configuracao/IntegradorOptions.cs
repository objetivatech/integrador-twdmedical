namespace TwdIntegrador.Core.Configuracao;

/// <summary>
/// Configuração persistida do integrador (config.json). O token do equipamento
/// NÃO fica aqui: é guardado criptografado à parte (ver <see cref="ConfiguracaoStore"/>).
/// </summary>
public sealed class IntegradorOptions
{
    /// <summary>URL base da plataforma, ex.: https://xyz.supabase.co/functions/v1</summary>
    public string PlataformaUrl { get; set; } = "";

    /// <summary>Pastas monitoradas. Cada fonte usa um conector (ex.: "wincardio").</summary>
    public List<FonteOptions> Fontes { get; set; } = [];

    /// <summary>O que fazer com o PDF original depois do envio confirmado.</summary>
    public AcaoAposEnvio AposEnvio { get; set; } = AcaoAposEnvio.Excluir;

    /// <summary>Usado quando <see cref="AposEnvio"/> = Mover.</summary>
    public string? PastaEnviados { get; set; }

    /// <summary>Intervalo da varredura de segurança (além do monitoramento em tempo real).</summary>
    public int IntervaloVarreduraSegundos { get; set; } = 30;

    /// <summary>Intervalo do sinal de "estou vivo" para a plataforma.</summary>
    public int IntervaloHeartbeatSegundos { get; set; } = 300;

    /// <summary>Senha (hash PBKDF2) exigida para abrir as configurações. Vazio = sem senha.</summary>
    public string? SenhaConfiguracaoHash { get; set; }
}

public sealed class FonteOptions
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Nome { get; set; } = "WinCardio";
    public string Conector { get; set; } = "wincardio";
    public string Pasta { get; set; } = "";
    public string Filtro { get; set; } = "*.pdf";
    public bool IncluirSubpastas { get; set; }

    /// <summary>
    /// Arquivos modificados antes deste instante são ignorados. Evita que, na instalação,
    /// o integrador envie todo o histórico antigo da pasta. Nulo = enviar tudo.
    /// </summary>
    public DateTimeOffset? IgnorarArquivosAnterioresA { get; set; }

    public bool Ativa { get; set; } = true;
}

public enum AcaoAposEnvio
{
    Excluir,
    Mover,
    Manter,
}
