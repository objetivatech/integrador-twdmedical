using System.Text;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace TwdIntegrador.Core.Pdf;

/// <summary>Texto de um PDF reorganizado em linhas, na ordem visual (de cima para baixo).</summary>
public sealed record TextoPdf(IReadOnlyList<string> Linhas, int NumeroPaginas)
{
    public string TextoCompleto => string.Join('\n', Linhas);

    public bool Vazio => Linhas.Count == 0;
}

public static class ExtratorTextoPdf
{
    /// <summary>
    /// Extrai o texto agrupando as palavras pela altura na página. Laudos de ECG costumam
    /// posicionar rótulo e valor lado a lado ("Nome: FULANO"), então a ordem visual é a
    /// que importa, não a ordem em que o programa gravou o texto no arquivo.
    /// </summary>
    public static TextoPdf Extrair(byte[] pdf)
    {
        using var documento = PdfDocument.Open(pdf);
        var linhas = new List<string>();
        foreach (var pagina in documento.GetPages())
        {
            linhas.AddRange(AgruparEmLinhas(pagina.GetWords()));
        }
        return new TextoPdf(linhas, documento.NumberOfPages);
    }

    internal static IEnumerable<string> AgruparEmLinhas(IEnumerable<Word> palavras)
    {
        // Origem do PDF é no canto inferior esquerdo: maior Y = mais acima.
        var ordenadas = palavras
            .Where(p => !string.IsNullOrWhiteSpace(p.Text))
            .OrderByDescending(p => p.BoundingBox.Bottom)
            .ToList();

        var grupos = new List<List<Word>>();
        foreach (var palavra in ordenadas)
        {
            var tolerancia = Math.Max(palavra.BoundingBox.Height * 0.5, 1.5);
            var grupo = grupos.LastOrDefault();
            if (grupo != null && Math.Abs(grupo[0].BoundingBox.Bottom - palavra.BoundingBox.Bottom) <= tolerancia)
            {
                grupo.Add(palavra);
            }
            else
            {
                grupos.Add([palavra]);
            }
        }

        foreach (var grupo in grupos)
        {
            var sb = new StringBuilder();
            foreach (var palavra in grupo.OrderBy(p => p.BoundingBox.Left))
            {
                if (sb.Length > 0) sb.Append(' ');
                sb.Append(palavra.Text);
            }
            yield return sb.ToString();
        }
    }
}
