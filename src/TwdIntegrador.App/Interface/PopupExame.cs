using System.Text.Json;
using TwdIntegrador.Core.Fila;

namespace TwdIntegrador.App.Interface;

/// <summary>
/// Cartão que aparece no canto da tela quando um exame é capturado/enviado — o mesmo
/// retorno visual que o operador já conhece do TWD Monitor, mas sem travar o WinCardio:
/// não rouba o foco e fecha sozinho.
/// </summary>
internal sealed class PopupExame : Form
{
    private static PopupExame? _atual;
    private readonly System.Windows.Forms.Timer _fechar = new() { Interval = 7000 };
    private readonly Label _status;

    private PopupExame(ItemFila item, string mensagem, Color cor)
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        BackColor = Color.White;
        Size = new Size(340, 300);
        Padding = new Padding(1);

        var conteudo = new Panel { Dock = DockStyle.Fill, BackColor = Color.White, Padding = new Padding(20, 14, 20, 14) };
        Controls.Add(conteudo);
        Paint += (_, e) =>
        {
            using var borda = new Pen(Tema.Turquesa, 1);
            e.Graphics.DrawRectangle(borda, 0, 0, Width - 1, Height - 1);
        };

        var (tipo, data) = LerTipoEData(item.MetadadosJson);
        var tabela = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, Padding = new Padding(0, 6, 0, 6) };
        tabela.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 80));
        tabela.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        void Linha(string rotulo, string? valor)
        {
            tabela.Controls.Add(new Label { Text = rotulo, Font = Tema.Normal, ForeColor = Tema.TextoSuave, AutoSize = true, Anchor = AnchorStyles.Right });
            tabela.Controls.Add(new Label { Text = string.IsNullOrWhiteSpace(valor) ? "—" : valor, Font = Tema.Negrito, ForeColor = Tema.Texto, AutoSize = true, MaximumSize = new Size(210, 0) });
        }
        Linha("Exame:", item.NumeroExame);
        Linha("Paciente:", item.Paciente);
        Linha("Tipo:", tipo);
        Linha("Data:", data);

        var titulo = new Label { Text = "Dados do Exame", Font = Tema.Titulo, ForeColor = Tema.Texto, Dock = DockStyle.Top, Height = 30, TextAlign = ContentAlignment.MiddleCenter };
        _status = new Label { Text = mensagem, Font = Tema.Negrito, ForeColor = cor, Dock = DockStyle.Top, Height = 40, TextAlign = ContentAlignment.MiddleCenter };

        // Ordem inversa por causa do DockStyle.Top.
        conteudo.Controls.Add(_status);
        conteudo.Controls.Add(tabela);
        conteudo.Controls.Add(titulo);
        conteudo.Controls.Add(Tema.Logo());

        Click += (_, _) => Close();
        foreach (Control c in conteudo.Controls) c.Click += (_, _) => Close();
        _fechar.Tick += (_, _) => Close();
    }

    // Não ativa a janela ao aparecer: o operador continua digitando no WinCardio.
    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            const int WS_EX_NOACTIVATE = 0x08000000, WS_EX_TOOLWINDOW = 0x80;
            var cp = base.CreateParams;
            cp.ExStyle |= WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW;
            return cp;
        }
    }

    public static void Mostrar(ItemFila item, string mensagem, Color cor)
    {
        // Mesmo exame já na tela (capturado -> enviado): só atualiza a mensagem.
        if (_atual is { IsDisposed: false } && _atual.Tag is string sha && sha == item.Sha256)
        {
            _atual._status.Text = mensagem;
            _atual._status.ForeColor = cor;
            _atual._fechar.Stop();
            _atual._fechar.Start();
            return;
        }

        _atual?.Close();
        var popup = new PopupExame(item, mensagem, cor) { Tag = item.Sha256 };
        var area = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1280, 720);
        popup.Location = new Point(area.Right - popup.Width - 16, area.Bottom - popup.Height - 16);
        popup.Show();
        popup._fechar.Start();
        _atual = popup;
    }

    private static (string? Tipo, string? Data) LerTipoEData(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("exame", out var exame)) return (null, null);
            var tipo = exame.TryGetProperty("tipo", out var t) ? t.GetString() : null;
            var data = exame.TryGetProperty("data_hora", out var d) && d.TryGetDateTimeOffset(out var dt)
                ? dt.ToString("dd/MM/yyyy HH:mm") : null;
            return (tipo, data);
        }
        catch (JsonException)
        {
            return (null, null);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _fechar.Dispose();
        base.Dispose(disposing);
    }
}
