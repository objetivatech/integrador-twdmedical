using TwdIntegrador.App.Sistema;
using TwdIntegrador.Core.Conectores;
using TwdIntegrador.Core.Configuracao;
using TwdIntegrador.Core.Envio;
using TwdIntegrador.Core.Seguranca;

namespace TwdIntegrador.App.Interface;

/// <summary>
/// Tela usada pelo técnico na instalação: endereço da plataforma, token do equipamento
/// (gerado no cadastro do equipamento na plataforma), pasta do WinCardio e o que fazer
/// com o PDF depois do envio.
/// </summary>
internal sealed class ConfiguracoesForm : Form
{
    private readonly ConfiguracaoStore _store;
    private readonly ClientePlataforma _cliente;
    private readonly IntegradorOptions _opcoes;

    private readonly TextBox _url = new() { Width = 380, PlaceholderText = "https://<projeto>.supabase.co/functions/v1" };
    private readonly TextBox _token = new() { Width = 380, UseSystemPasswordChar = true };
    private readonly Label _resultadoTeste = new() { AutoSize = true, MaximumSize = new Size(380, 0), Font = Tema.Pequena };
    private readonly TextBox _pasta = new() { Width = 300 };
    private readonly ComboBox _conector = new() { Width = 220, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly CheckBox _enviarExistentes = new() { Text = "Enviar também os PDFs que já estão na pasta", AutoSize = true };
    private readonly RadioButton _excluir = new() { Text = "Excluir do computador (recomendado — o exame fica só na plataforma)", AutoSize = true };
    private readonly RadioButton _mover = new() { Text = "Mover para a pasta:", AutoSize = true };
    private readonly RadioButton _manter = new() { Text = "Manter na pasta", AutoSize = true };
    private readonly TextBox _pastaEnviados = new() { Width = 260 };
    private readonly CheckBox _iniciarComWindows = new() { Text = "Iniciar junto com o Windows", AutoSize = true };
    private readonly TextBox _senha = new() { Width = 200, UseSystemPasswordChar = true, PlaceholderText = "deixe vazio para não alterar" };

    public ConfiguracoesForm(ConfiguracaoStore store, ClientePlataforma cliente)
    {
        _store = store;
        _cliente = cliente;
        _opcoes = store.Carregar();

        Text = "TWD Integrador — Configurações";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        Font = Tema.Normal;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(16);

        var layout = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false };
        Controls.Add(layout);

        // Plataforma
        layout.Controls.Add(Secao("Plataforma"));
        layout.Controls.Add(Rotulo("Endereço da plataforma"));
        layout.Controls.Add(_url);
        layout.Controls.Add(Rotulo("Token do equipamento (gerado na plataforma, no cadastro do equipamento)"));
        var linhaToken = Linha(_token);
        var testar = Tema.BotaoSecundario("Testar conexão");
        testar.Click += async (_, _) => await TestarAsync();
        linhaToken.Controls.Add(testar);
        layout.Controls.Add(linhaToken);
        layout.Controls.Add(_resultadoTeste);

        // Fonte
        var fonte = _opcoes.Fontes.FirstOrDefault();
        layout.Controls.Add(Secao("Programa de exames"));
        layout.Controls.Add(Rotulo("Programa"));
        foreach (var c in RegistroConectores.Padrao().Todos) _conector.Items.Add(new ItemConector(c));
        _conector.SelectedItem = _conector.Items.Cast<ItemConector>().FirstOrDefault(i => i.Conector.Id == (fonte?.Conector ?? "wincardio"))
                                 ?? _conector.Items[0];
        layout.Controls.Add(_conector);
        layout.Controls.Add(Rotulo("Pasta onde o programa salva os PDFs"));
        var procurar = Tema.BotaoSecundario("Procurar…");
        procurar.Click += (_, _) => EscolherPasta(_pasta);
        layout.Controls.Add(Linha(_pasta, procurar));
        layout.Controls.Add(_enviarExistentes);
        _enviarExistentes.Visible = fonte is null;

        // Após envio
        layout.Controls.Add(Secao("Depois que o exame for enviado"));
        layout.Controls.Add(_excluir);
        var procurarEnviados = Tema.BotaoSecundario("…");
        procurarEnviados.Click += (_, _) => EscolherPasta(_pastaEnviados);
        layout.Controls.Add(Linha(_mover, _pastaEnviados, procurarEnviados));
        layout.Controls.Add(_manter);

        // Sistema
        layout.Controls.Add(Secao("Sistema"));
        layout.Controls.Add(_iniciarComWindows);
        layout.Controls.Add(Rotulo("Senha para abrir esta tela (opcional)"));
        layout.Controls.Add(_senha);

        var salvar = Tema.BotaoPrimario("Salvar");
        salvar.Click += (_, _) => Salvar();
        var cancelar = Tema.BotaoSecundario("Cancelar");
        cancelar.Click += (_, _) => Close();
        var botoes = Linha(salvar, cancelar);
        botoes.Margin = new Padding(0, 16, 0, 0);
        layout.Controls.Add(botoes);
        AcceptButton = salvar;
        CancelButton = cancelar;

        // Valores atuais
        _url.Text = _opcoes.PlataformaUrl;
        _token.Text = store.LerToken() ?? "";
        _pasta.Text = fonte?.Pasta ?? (Directory.Exists(@"C:\TWD") ? @"C:\TWD" : "");
        _pastaEnviados.Text = _opcoes.PastaEnviados ?? "";
        _excluir.Checked = _opcoes.AposEnvio == AcaoAposEnvio.Excluir;
        _mover.Checked = _opcoes.AposEnvio == AcaoAposEnvio.Mover;
        _manter.Checked = _opcoes.AposEnvio == AcaoAposEnvio.Manter;
        _iniciarComWindows.Checked = _store.LerToken() is null || InicioAutomatico.Ativo;
    }

    private async Task TestarAsync()
    {
        _resultadoTeste.ForeColor = Tema.TextoSuave;
        _resultadoTeste.Text = "Testando…";
        var (ok, msg) = await _cliente.TestarConexaoAsync(_url.Text.Trim(), _token.Text.Trim(), CancellationToken.None);
        _resultadoTeste.ForeColor = ok ? Tema.Sucesso : Tema.Erro;
        _resultadoTeste.Text = msg;
    }

    private void Salvar()
    {
        if (!Uri.TryCreate(_url.Text.Trim(), UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
        {
            Avisar("Informe o endereço da plataforma começando com https://");
            return;
        }
        if (string.IsNullOrWhiteSpace(_token.Text))
        {
            Avisar("Informe o token do equipamento.");
            return;
        }
        if (!Directory.Exists(_pasta.Text))
        {
            Avisar("A pasta dos PDFs não existe.");
            return;
        }
        if (_mover.Checked && string.IsNullOrWhiteSpace(_pastaEnviados.Text))
        {
            Avisar("Escolha a pasta para onde mover os exames enviados.");
            return;
        }

        var conector = ((ItemConector)_conector.SelectedItem!).Conector;
        var fonte = _opcoes.Fontes.FirstOrDefault();
        if (fonte is null)
        {
            fonte = new FonteOptions
            {
                IgnorarArquivosAnterioresA = _enviarExistentes.Checked ? null : DateTimeOffset.Now,
            };
            _opcoes.Fontes.Add(fonte);
        }
        else if (!string.Equals(fonte.Pasta, _pasta.Text, StringComparison.OrdinalIgnoreCase))
        {
            // Pasta trocada: não despejar o histórico antigo da nova pasta na plataforma.
            fonte.IgnorarArquivosAnterioresA = DateTimeOffset.Now;
        }
        fonte.Pasta = _pasta.Text;
        fonte.Conector = conector.Id;
        fonte.Nome = conector.Nome;

        _opcoes.PlataformaUrl = _url.Text.Trim().TrimEnd('/');
        _opcoes.AposEnvio = _excluir.Checked ? AcaoAposEnvio.Excluir : _mover.Checked ? AcaoAposEnvio.Mover : AcaoAposEnvio.Manter;
        _opcoes.PastaEnviados = string.IsNullOrWhiteSpace(_pastaEnviados.Text) ? null : _pastaEnviados.Text;
        if (!string.IsNullOrEmpty(_senha.Text)) _opcoes.SenhaConfiguracaoHash = Senha.GerarHash(_senha.Text);

        _store.SalvarToken(_token.Text);
        _store.Salvar(_opcoes); // dispara a recarga do serviço
        InicioAutomatico.Definir(_iniciarComWindows.Checked);

        DialogResult = DialogResult.OK;
        Close();
    }

    private void Avisar(string msg) => MessageBox.Show(this, msg, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);

    private void EscolherPasta(TextBox destino)
    {
        using var dlg = new FolderBrowserDialog { SelectedPath = destino.Text, UseDescriptionForTitle = true, Description = "Escolha a pasta" };
        if (dlg.ShowDialog(this) == DialogResult.OK) destino.Text = dlg.SelectedPath;
    }

    private static Label Secao(string texto) => new()
    {
        Text = texto,
        Font = Tema.Titulo,
        ForeColor = Tema.TurquesaEscuro,
        AutoSize = true,
        Margin = new Padding(0, 14, 0, 4),
    };

    private static Label Rotulo(string texto) => new()
    {
        Text = texto,
        AutoSize = true,
        ForeColor = Tema.TextoSuave,
        Margin = new Padding(0, 6, 0, 2),
    };

    private static FlowLayoutPanel Linha(params Control[] controles)
    {
        var p = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty };
        p.Controls.AddRange(controles);
        return p;
    }

    private sealed record ItemConector(IConector Conector)
    {
        public override string ToString() => Conector.Nome;
    }

    /// <summary>Pede a senha (se houver) antes de abrir a tela.</summary>
    public static bool Autorizar(IntegradorOptions opcoes)
    {
        if (string.IsNullOrEmpty(opcoes.SenhaConfiguracaoHash)) return true;
        using var dlg = new Form
        {
            Text = "TWD Integrador",
            FormBorderStyle = FormBorderStyle.FixedDialog,
            StartPosition = FormStartPosition.CenterScreen,
            MaximizeBox = false,
            MinimizeBox = false,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(16),
            Font = Tema.Normal,
        };
        var senha = new TextBox { Width = 220, UseSystemPasswordChar = true };
        var ok = Tema.BotaoPrimario("Entrar");
        ok.DialogResult = DialogResult.OK;
        var painel = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true };
        painel.Controls.AddRange([new Label { Text = "Senha das configurações:", AutoSize = true }, senha, ok]);
        dlg.Controls.Add(painel);
        dlg.AcceptButton = ok;
        if (dlg.ShowDialog() != DialogResult.OK) return false;
        if (Senha.Verificar(senha.Text, opcoes.SenhaConfiguracaoHash)) return true;
        MessageBox.Show("Senha incorreta.", "TWD Integrador", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        return false;
    }
}
