// Ferramenta de diagnóstico: mostra o texto que o integrador enxerga num PDF e o que
// cada conector consegue extrair dele. Use para validar PDFs de um novo programa/clínica:
//   twd-diag exame.pdf [conector]
using TwdIntegrador.Core.Conectores;
using TwdIntegrador.Core.Envio;
using TwdIntegrador.Core.Pdf;

if (args.Length == 0)
{
    Console.Error.WriteLine("Uso: twd-diag <arquivo.pdf> [conector]");
    Console.Error.WriteLine("Conectores: " + string.Join(", ", RegistroConectores.Padrao().Todos.Select(c => c.Id)));
    return 1;
}

var texto = ExtratorTextoPdf.Extrair(File.ReadAllBytes(args[0]));
Console.WriteLine($"=== Texto extraído ({texto.NumeroPaginas} página(s), {texto.Linhas.Count} linhas) ===");
for (var i = 0; i < texto.Linhas.Count; i++) Console.WriteLine($"{i + 1,3}: {texto.Linhas[i]}");

var conector = RegistroConectores.Padrao().Obter(args.Length > 1 ? args[1] : "wincardio");
Console.WriteLine();
Console.WriteLine($"=== Dados interpretados pelo conector '{conector.Id}' ===");
var json = System.Text.Json.JsonSerializer.Serialize(MetadadosEnvio.DeDados(conector.Interpretar(texto)),
    new System.Text.Json.JsonSerializerOptions(MetadadosEnvio.Json) { WriteIndented = true });
Console.WriteLine(json);
return 0;
