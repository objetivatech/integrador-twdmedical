using TwdIntegrador.Core.Fila;
using TwdIntegrador.Core.Servicos;

namespace TwdIntegrador.App.Interface;

/// <summary>Últimos exames capturados e o status de cada um. Permite reenviar os recusados.</summary>
internal sealed class HistoricoForm : Form
{
    private readonly FilaEnvio _fila;
    private readonly IntegradorWorker _worker;
    private readonly DataGridView _grade = new()
    {
        Dock = DockStyle.Fill,
        ReadOnly = true,
        AllowUserToAddRows = false,
        AllowUserToDeleteRows = false,
        RowHeadersVisible = false,
        SelectionMode = DataGridViewSelectionMode.FullRowSelect,
        AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
        BackgroundColor = Color.White,
        BorderStyle = BorderStyle.None,
    };
    private readonly Label _resumo = new() { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Font = Tema.Normal };

    public HistoricoForm(FilaEnvio fila, IntegradorWorker worker, EventosIntegrador eventos)
    {
        _fila = fila;
        _worker = worker;
        Text = "TWD Integrador — Exames";
        Size = new Size(900, 520);
        StartPosition = FormStartPosition.CenterScreen;
        Font = Tema.Normal;
        Icon = Tema.IconeEstado(eventos.Estado);

        _grade.Columns.Add("capturado", "Capturado");
        _grade.Columns.Add("exame", "Exame");
        _grade.Columns.Add("paciente", "Paciente");
        _grade.Columns.Add("arquivo", "Arquivo");
        _grade.Columns.Add("status", "Status");
        _grade.Columns.Add("detalhe", "Detalhe");
        _grade.Columns["paciente"]!.FillWeight = 160;
        _grade.Columns["detalhe"]!.FillWeight = 200;

        var reenviar = Tema.BotaoSecundario("Reenviar selecionado");
        reenviar.Click += (_, _) => Reenviar();
        var enviarAgora = Tema.BotaoPrimario("Enviar pendentes agora");
        enviarAgora.Click += (_, _) => { _worker.SolicitarEnvio(); Recarregar(); };
        var atualizar = Tema.BotaoSecundario("Atualizar");
        atualizar.Click += (_, _) => Recarregar();

        var barra = new TableLayoutPanel { Dock = DockStyle.Bottom, Height = 48, ColumnCount = 4, Padding = new Padding(8) };
        barra.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (var i = 0; i < 3; i++) barra.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        barra.Controls.AddRange([_resumo, atualizar, reenviar, enviarAgora]);

        Controls.Add(_grade);
        Controls.Add(barra);

        // Atualiza sozinho quando algo acontece no serviço.
        void Agendar(ItemFila _) { if (IsHandleCreated) BeginInvoke(Recarregar); }
        void AgendarRej(ItemFila _, string __) { if (IsHandleCreated) BeginInvoke(Recarregar); }
        eventos.ExameCapturado += Agendar;
        eventos.ExameEnviado += Agendar;
        eventos.ExameRejeitado += AgendarRej;
        FormClosed += (_, _) =>
        {
            eventos.ExameCapturado -= Agendar;
            eventos.ExameEnviado -= Agendar;
            eventos.ExameRejeitado -= AgendarRej;
        };

        Load += (_, _) => Recarregar();
    }

    private void Recarregar()
    {
        _grade.Rows.Clear();
        foreach (var item in _fila.ListarRecentes())
        {
            var (status, cor) = item.Status switch
            {
                StatusItem.Enviado => ("Enviado", Tema.Sucesso),
                StatusItem.Rejeitado => ("Recusado", Tema.Erro),
                _ => (item.Tentativas == 0 ? "Aguardando envio" : $"Nova tentativa {item.ProximaTentativa.ToLocalTime():HH:mm}", Tema.Alerta),
            };
            var linha = _grade.Rows.Add(
                item.CapturadoEm.ToLocalTime().ToString("dd/MM/yyyy HH:mm"),
                item.NumeroExame ?? "",
                item.Paciente ?? (item.Status == StatusItem.Enviado ? "(dados removidos após 30 dias)" : ""),
                item.NomeArquivo,
                status,
                (item.Status == StatusItem.Enviado ? item.EnviadoEm?.ToLocalTime().ToString("'em' dd/MM HH:mm") : item.UltimoErro) ?? "");
            _grade.Rows[linha].Cells["status"].Style.ForeColor = cor;
            _grade.Rows[linha].Tag = item;
        }
        var c = _fila.Contar();
        _resumo.Text = $"Pendentes: {c.Pendentes}    Enviados: {c.Enviados}    Recusados: {c.Rejeitados}";
    }

    private void Reenviar()
    {
        if (_grade.SelectedRows.Count == 0 || _grade.SelectedRows[0].Tag is not ItemFila item) return;
        if (item.Status != StatusItem.Rejeitado)
        {
            MessageBox.Show(this, "Somente exames recusados podem ser reenviados manualmente.", Text);
            return;
        }
        _fila.Reenfileirar(item.Id, DateTimeOffset.UtcNow);
        _worker.SolicitarEnvio();
        Recarregar();
    }
}
