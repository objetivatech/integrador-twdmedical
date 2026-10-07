using TwdIntegrador.Core.Fila;

namespace TwdIntegrador.Core.Servicos;

public enum EstadoIntegrador
{
    Iniciando,
    /// <summary>Falta URL, token ou pasta monitorada.</summary>
    NaoConfigurado,
    Ativo,
    /// <summary>Há exames na fila e a plataforma não está respondendo.</summary>
    SemConexao,
    TokenInvalido,
    Pausado,
}

/// <summary>Ponte entre o serviço de fundo e a interface (ícone na bandeja, popups).</summary>
public sealed class EventosIntegrador
{
    private EstadoIntegrador _estado = EstadoIntegrador.Iniciando;

    public EstadoIntegrador Estado => _estado;
    public string? DetalheEstado { get; private set; }

    public event Action<EstadoIntegrador, string?>? EstadoAlterado;
    public event Action<ItemFila>? ExameCapturado;
    public event Action<ItemFila>? ExameEnviado;
    public event Action<ItemFila, string>? ExameRejeitado;

    internal void DefinirEstado(EstadoIntegrador estado, string? detalhe = null)
    {
        if (_estado == estado && DetalheEstado == detalhe) return;
        _estado = estado;
        DetalheEstado = detalhe;
        EstadoAlterado?.Invoke(estado, detalhe);
    }

    internal void NotificarCapturado(ItemFila item) => ExameCapturado?.Invoke(item);
    internal void NotificarEnviado(ItemFila item) => ExameEnviado?.Invoke(item);
    internal void NotificarRejeitado(ItemFila item, string motivo) => ExameRejeitado?.Invoke(item, motivo);
}
