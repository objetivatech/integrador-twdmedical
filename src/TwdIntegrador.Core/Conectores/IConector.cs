using TwdIntegrador.Core.Pdf;

namespace TwdIntegrador.Core.Conectores;

/// <summary>
/// Um conector sabe interpretar os PDFs de um programa específico (WinCardio, Holter, MAPA...).
/// Para suportar um novo programa basta implementar esta interface e registrá-la em
/// <see cref="RegistroConectores"/>.
/// </summary>
public interface IConector
{
    /// <summary>Identificador usado no config.json, ex.: "wincardio".</summary>
    string Id { get; }

    string Nome { get; }

    DadosExame Interpretar(TextoPdf texto);
}

public sealed class RegistroConectores
{
    private readonly Dictionary<string, IConector> _conectores;

    public RegistroConectores(IEnumerable<IConector> conectores)
    {
        _conectores = conectores.ToDictionary(c => c.Id, StringComparer.OrdinalIgnoreCase);
    }

    public static RegistroConectores Padrao() => new([new WinCardioConector(), new GenericoConector()]);

    public IReadOnlyCollection<IConector> Todos => _conectores.Values;

    /// <summary>Conector pelo id; desconhecido cai no genérico (o PDF é enviado mesmo assim).</summary>
    public IConector Obter(string id) =>
        _conectores.TryGetValue(id, out var c) ? c : _conectores[GenericoConector.IdConector];
}

/// <summary>Não interpreta nada: envia o PDF e o texto extraído para a plataforma tratar.</summary>
public sealed class GenericoConector : IConector
{
    public const string IdConector = "generico";
    public string Id => IdConector;
    public string Nome => "Genérico (somente PDF)";
    public DadosExame Interpretar(TextoPdf texto) => new();
}
