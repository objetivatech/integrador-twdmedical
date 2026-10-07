using System.Drawing.Drawing2D;
using System.Drawing.Text;
using TwdIntegrador.Core.Servicos;

namespace TwdIntegrador.App.Interface;

/// <summary>
/// Identidade visual TWD Medical (cores aproximadas da plataforma TWD Core).
/// Logo oficial: ver assets/README.md — enquanto não chega, desenhamos uma marca provisória.
/// </summary>
internal static class Tema
{
    public static readonly Color Turquesa = Color.FromArgb(0x1E, 0xC8, 0xB4);
    public static readonly Color TurquesaEscuro = Color.FromArgb(0x12, 0x8F, 0x86);
    public static readonly Color Marinho = Color.FromArgb(0x0E, 0x17, 0x26);
    public static readonly Color MarinhoClaro = Color.FromArgb(0x1A, 0x26, 0x3A);
    public static readonly Color Texto = Color.FromArgb(0x1F, 0x29, 0x37);
    public static readonly Color TextoSuave = Color.FromArgb(0x6B, 0x72, 0x80);
    public static readonly Color Sucesso = Color.FromArgb(0x16, 0xA3, 0x4A);
    public static readonly Color Alerta = Color.FromArgb(0xD9, 0x77, 0x06);
    public static readonly Color Erro = Color.FromArgb(0xDC, 0x26, 0x26);

    public static readonly Font Titulo = new("Segoe UI Semibold", 13f);
    public static readonly Font Normal = new("Segoe UI", 9.5f);
    public static readonly Font Negrito = new("Segoe UI Semibold", 9.5f);
    public static readonly Font Pequena = new("Segoe UI", 8f);

    /// <summary>Ícone da bandeja: círculo turquesa com "T", e um ponto colorido indicando o estado.</summary>
    public static Icon IconeEstado(EstadoIntegrador estado)
    {
        var ponto = estado switch
        {
            EstadoIntegrador.Ativo => Sucesso,
            EstadoIntegrador.SemConexao or EstadoIntegrador.Pausado or EstadoIntegrador.Iniciando => Alerta,
            _ => Erro,
        };

        using var bmp = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            using var fundo = new SolidBrush(Turquesa);
            g.FillEllipse(fundo, 1, 1, 30, 30);
            using var fonte = new Font("Segoe UI", 15f, FontStyle.Bold, GraphicsUnit.Pixel);
            TextRenderer.DrawText(g, "T", fonte, new Rectangle(0, 0, 32, 30), Color.White,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            using var borda = new Pen(Color.White, 2);
            using var cor = new SolidBrush(ponto);
            g.FillEllipse(cor, 19, 19, 12, 12);
            g.DrawEllipse(borda, 19, 19, 12, 12);
        }
        var handle = bmp.GetHicon();
        using var temporario = Icon.FromHandle(handle);
        var icone = (Icon)temporario.Clone();
        NativeMethods.DestroyIcon(handle);
        return icone;
    }

    /// <summary>Marca provisória "TWD Medical" desenhada; substituir pelo PNG oficial em assets/.</summary>
    public static Control Logo()
    {
        var caminho = Path.Combine(AppContext.BaseDirectory, "assets", "logo.png");
        if (File.Exists(caminho))
        {
            return new PictureBox
            {
                Image = Image.FromFile(caminho),
                SizeMode = PictureBoxSizeMode.Zoom,
                Height = 64,
                Dock = DockStyle.Top,
            };
        }

        var painel = new Panel { Height = 64, Dock = DockStyle.Top };
        painel.Paint += (_, e) =>
        {
            e.Graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            using var marca = new Font("Segoe UI", 28f, FontStyle.Bold);
            using var sub = new Font("Segoe UI", 9f);
            var area = painel.ClientRectangle;
            TextRenderer.DrawText(e.Graphics, "TWD", marca, new Rectangle(0, 0, area.Width, 46), Turquesa,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.Bottom);
            TextRenderer.DrawText(e.Graphics, "M e d i c a l", sub, new Rectangle(0, 44, area.Width, 18), TextoSuave,
                TextFormatFlags.HorizontalCenter);
        };
        return painel;
    }

    public static Button BotaoPrimario(string texto)
    {
        var b = new Button
        {
            Text = texto,
            AutoSize = true,
            FlatStyle = FlatStyle.Flat,
            BackColor = Turquesa,
            ForeColor = Color.White,
            Font = Negrito,
            Padding = new Padding(10, 3, 10, 3),
            Cursor = Cursors.Hand,
        };
        b.FlatAppearance.BorderSize = 0;
        return b;
    }

    public static Button BotaoSecundario(string texto) => new()
    {
        Text = texto,
        AutoSize = true,
        Font = Normal,
        Padding = new Padding(8, 2, 8, 2),
    };

    private static class NativeMethods
    {
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        public static extern bool DestroyIcon(IntPtr handle);
    }
}
