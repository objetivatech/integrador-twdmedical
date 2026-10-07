using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;

namespace TwdIntegrador.Core.Tests;

/// <summary>
/// Gera um PDF imitando o layout do "ECG de Repouso" do WinCardio (A4 paisagem), com os
/// textos nas mesmas posições relativas do laudo real. Dados 100% fictícios.
/// </summary>
internal static class PdfSintetico
{
    public static byte[] WinCardio(
        string exame = "12142",
        string nome = "MARIA FICTICIA DA SILVA",
        string nasc = "17/12/1953",
        string cpf = "",
        string data = "31/08/2026-20:44:53",
        string convenio = "UNIMED",
        string serial = "0002187791")
    {
        var builder = new PdfDocumentBuilder();
        var pagina = builder.AddPage(PageSize.A4, isPortrait: false); // 842 x 595
        var normal = builder.AddStandard14Font(Standard14Font.Helvetica);
        var negrito = builder.AddStandard14Font(Standard14Font.HelveticaBold);

        void Texto(string t, double x, double y, bool bold = false, double tam = 9)
        {
            if (!string.IsNullOrEmpty(t)) pagina.AddText(t, tam, new PdfPoint(x, y), bold ? negrito : normal);
        }

        Texto("ECG de Repouso", 380, 565, bold: true, tam: 10);

        // Linha 1 do cabeçalho
        Texto("Exame:", 100, 548, bold: true); Texto(exame, 132, 548);
        Texto("Nome:", 165, 548, bold: true); Texto(nome, 194, 548);
        Texto("Reg.Clin.:", 525, 548, bold: true);
        Texto("Nasc.:", 668, 548, bold: true); Texto(nasc, 696, 548);

        // Linha 2 do cabeçalho
        Texto("FC:", 100, 535, bold: true); Texto("56 bpm", 117, 535);
        Texto("25 mm/s", 162, 535); Texto("10mm/mV", 210, 535); Texto("60Hz", 262, 535); Texto("Musc", 297, 535);
        Texto("RG:", 382, 535, bold: true);
        Texto("CPF:", 525, 535, bold: true); Texto(cpf, 550, 535);
        Texto("Data:", 640, 535, bold: true); Texto(data, 668, 535);

        // Derivações (duas colunas) + DII longo
        string[] esquerda = ["DI", "DII", "DIII", "aVR", "aVL", "aVF"];
        string[] direita = ["V1", "V2", "V3", "V4", "V5", "V6"];
        for (var i = 0; i < 6; i++)
        {
            Texto(esquerda[i], 105, 520 - i * 62, tam: 7);
            Texto(direita[i], 432, 520 - i * 62, tam: 7);
        }
        Texto("DII", 105, 140, tam: 7);

        Texto(convenio, 100, 75);
        Texto("Ecg#", 100, 62, bold: true, tam: 5); Texto(serial, 115, 62, tam: 5);
        Texto("MICROMED", 735, 62, tam: 5);

        return builder.Build();
    }
}
