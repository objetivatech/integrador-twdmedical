using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using TwdIntegrador.Core.Seguranca;

namespace TwdIntegrador.Core.Configuracao;

/// <summary>
/// Lê e grava a configuração em disco. Layout da pasta de dados:
///   config.json   configuração (sem segredos)
///   token.bin     token do equipamento, criptografado
///   fila.db       fila de envio (SQLite)
///   fila/         cópias criptografadas dos PDFs aguardando envio
///   logs/         logs diários
/// </summary>
public sealed class ConfiguracaoStore
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly IProtetorDados _protetor;
    private readonly object _lock = new();

    public ConfiguracaoStore(string pastaDados, IProtetorDados protetor)
    {
        PastaDados = pastaDados;
        _protetor = protetor;
        Directory.CreateDirectory(pastaDados);
    }

    public string PastaDados { get; }
    public string CaminhoConfig => Path.Combine(PastaDados, "config.json");
    public string CaminhoToken => Path.Combine(PastaDados, "token.bin");
    public string CaminhoBanco => Path.Combine(PastaDados, "fila.db");
    public string PastaFila => Path.Combine(PastaDados, "fila");
    public string PastaLogs => Path.Combine(PastaDados, "logs");

    public static string PastaDadosPadrao() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TWD", "Integrador");

    public event Action<IntegradorOptions>? Alterada;

    public IntegradorOptions Carregar()
    {
        lock (_lock)
        {
            if (!File.Exists(CaminhoConfig)) return new IntegradorOptions();
            return JsonSerializer.Deserialize<IntegradorOptions>(File.ReadAllText(CaminhoConfig), Json)
                   ?? new IntegradorOptions();
        }
    }

    public void Salvar(IntegradorOptions opcoes)
    {
        lock (_lock)
        {
            var temp = CaminhoConfig + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(opcoes, Json));
            File.Move(temp, CaminhoConfig, overwrite: true);
        }
        Alterada?.Invoke(opcoes);
    }

    public string? LerToken()
    {
        lock (_lock)
        {
            if (!File.Exists(CaminhoToken)) return null;
            return Encoding.UTF8.GetString(_protetor.Desproteger(File.ReadAllBytes(CaminhoToken)));
        }
    }

    public void SalvarToken(string token)
    {
        lock (_lock)
        {
            File.WriteAllBytes(CaminhoToken, _protetor.Proteger(Encoding.UTF8.GetBytes(token.Trim())));
        }
    }
}
