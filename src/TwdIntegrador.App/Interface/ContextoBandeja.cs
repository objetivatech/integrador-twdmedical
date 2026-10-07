using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using TwdIntegrador.App.Sistema;
using TwdIntegrador.Core.Configuracao;
using TwdIntegrador.Core.Envio;
using TwdIntegrador.Core.Fila;
using TwdIntegrador.Core.Servicos;

namespace TwdIntegrador.App.Interface;

/// <summary>O integrador vive no ícone ao lado do relógio. Não há janela principal.</summary>
internal sealed class ContextoBandeja : ApplicationContext
{
    private readonly IServiceProvider _sp;
    private readonly EventosIntegrador _eventos;
    private readonly IntegradorWorker _worker;
    private readonly ConfiguracaoStore _store;
    private readonly NotifyIcon _icone;
    private readonly ToolStripMenuItem _itemEstado;
    private readonly ToolStripMenuItem _itemPausar;
    private readonly SynchronizationContext _ui;
    private readonly System.Windows.Forms.Timer _atualizacoes = new() { Interval = (int)TimeSpan.FromHours(6).TotalMilliseconds };
    private HistoricoForm? _historico;
    private ConfiguracoesForm? _configuracoes;

    public ContextoBandeja(IServiceProvider sp)
    {
        _sp = sp;
        _ui = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
        _eventos = sp.GetRequiredService<EventosIntegrador>();
        _worker = sp.GetRequiredService<IntegradorWorker>();
        _store = sp.GetRequiredService<ConfiguracaoStore>();

        _itemEstado = new ToolStripMenuItem { Enabled = false };
        _itemPausar = new ToolStripMenuItem("Pausar captura", null, (_, _) => AlternarPausa());
        var menu = new ContextMenuStrip();
        menu.Items.AddRange(
        [
            _itemEstado,
            new ToolStripSeparator(),
            new ToolStripMenuItem("Exames enviados…", null, (_, _) => AbrirHistorico()),
            new ToolStripMenuItem("Enviar pendentes agora", null, (_, _) => _worker.SolicitarEnvio()),
            _itemPausar,
            new ToolStripSeparator(),
            new ToolStripMenuItem("Configurações…", null, (_, _) => AbrirConfiguracoes()),
            new ToolStripMenuItem("Abrir pasta de logs", null, (_, _) => AbrirPasta(_store.PastaLogs)),
            new ToolStripMenuItem($"Versão {VersaoApp.Atual}") { Enabled = false },
            new ToolStripSeparator(),
            new ToolStripMenuItem("Sair", null, (_, _) => Sair()),
        ]);

        _icone = new NotifyIcon { ContextMenuStrip = menu, Visible = true };
        _icone.DoubleClick += (_, _) => AbrirHistorico();

        _eventos.EstadoAlterado += (e, d) => _ui.Post(_ => AtualizarEstado(e, d), null);
        _eventos.ExameCapturado += item => _ui.Post(_ => PopupExame.Mostrar(item, "Enviando para a plataforma…", Tema.TextoSuave), null);
        _eventos.ExameEnviado += item => _ui.Post(_ => PopupExame.Mostrar(item, "Exame enviado para a plataforma com sucesso.", Tema.Sucesso), null);
        _eventos.ExameRejeitado += (item, motivo) => _ui.Post(_ => PopupExame.Mostrar(item, $"Exame recusado pela plataforma: {motivo}", Tema.Erro), null);

        AtualizarEstado(_eventos.Estado, _eventos.DetalheEstado);

        // Primeira execução: abre direto a configuração para o técnico.
        if (string.IsNullOrWhiteSpace(_store.Carregar().PlataformaUrl))
            _ui.Post(_ => AbrirConfiguracoes(), null);

        var atualizador = sp.GetRequiredService<Atualizador>();
        _atualizacoes.Tick += async (_, _) => await atualizador.VerificarAsync();
        _atualizacoes.Start();
        _ = atualizador.VerificarAsync();
    }

    private void AtualizarEstado(EstadoIntegrador estado, string? detalhe)
    {
        var texto = estado switch
        {
            EstadoIntegrador.Ativo => "Ativo — monitorando exames",
            EstadoIntegrador.SemConexao => "Sem conexão — exames guardados na fila",
            EstadoIntegrador.TokenInvalido => "Token recusado pela plataforma",
            EstadoIntegrador.NaoConfigurado => detalhe ?? "Não configurado",
            EstadoIntegrador.Pausado => "Pausado",
            _ => "Iniciando…",
        };
        _itemEstado.Text = texto;
        _itemPausar.Text = estado == EstadoIntegrador.Pausado ? "Retomar captura" : "Pausar captura";

        var antigo = _icone.Icon;
        _icone.Icon = Tema.IconeEstado(estado);
        antigo?.Dispose();
        // Tooltip do NotifyIcon é limitado a 127 caracteres.
        var dica = $"TWD Integrador\n{texto}";
        _icone.Text = dica.Length > 127 ? dica[..127] : dica;

        if (estado is EstadoIntegrador.TokenInvalido)
            _icone.ShowBalloonTip(10_000, "TWD Integrador", "A plataforma recusou o token deste equipamento. Os exames estão guardados e serão enviados assim que o token for corrigido.", ToolTipIcon.Error);
    }

    private void AlternarPausa() => _worker.Pausar(!_worker.Pausado);

    private void AbrirHistorico()
    {
        if (_historico is { IsDisposed: false }) { _historico.Activate(); return; }
        _historico = new HistoricoForm(_sp.GetRequiredService<FilaEnvio>(), _worker, _eventos);
        _historico.Show();
    }

    private void AbrirConfiguracoes()
    {
        if (_configuracoes is { IsDisposed: false }) { _configuracoes.Activate(); return; }
        if (!ConfiguracoesForm.Autorizar(_store.Carregar())) return;
        _configuracoes = new ConfiguracoesForm(_store, _sp.GetRequiredService<ClientePlataforma>());
        _configuracoes.Show();
    }

    private static void AbrirPasta(string pasta)
    {
        Directory.CreateDirectory(pasta);
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{pasta}\"") { UseShellExecute = true });
    }

    private void Sair()
    {
        var c = _sp.GetRequiredService<FilaEnvio>().Contar();
        var aviso = c.Pendentes > 0
            ? $"Há {c.Pendentes} exame(s) aguardando envio. Eles ficam guardados e serão enviados quando o integrador for aberto de novo.\n\n"
            : "";
        if (MessageBox.Show($"{aviso}Enquanto o integrador estiver fechado, novos exames NÃO serão enviados. Deseja sair?",
                "TWD Integrador", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
            return;
        ExitThread();
    }

    protected override void ExitThreadCore()
    {
        _atualizacoes.Stop();
        _icone.Visible = false;
        _icone.Dispose();
        base.ExitThreadCore();
    }
}
