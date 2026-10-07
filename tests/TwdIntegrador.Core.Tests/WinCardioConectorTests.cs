using TwdIntegrador.Core.Conectores;
using TwdIntegrador.Core.Pdf;

namespace TwdIntegrador.Core.Tests;

public class WinCardioConectorTests
{
    private readonly WinCardioConector _conector = new();

    [Fact]
    public void Le_campos_do_pdf_sintetico_no_layout_do_wincardio()
    {
        var texto = ExtratorTextoPdf.Extrair(PdfSintetico.WinCardio());
        var dados = _conector.Interpretar(texto);

        Assert.Equal("ECG de Repouso", dados.TipoExame);
        Assert.Equal("12142", dados.NumeroExame);
        Assert.Equal("MARIA FICTICIA DA SILVA", dados.Paciente);
        Assert.Equal(new DateOnly(1953, 12, 17), dados.DataNascimento);
        Assert.Equal(new DateTime(2026, 8, 31, 20, 44, 53), dados.DataHoraExame?.DateTime);
        Assert.Equal(56, dados.FrequenciaCardiaca);
        Assert.Equal("UNIMED", dados.Convenio);
        Assert.Equal("0002187791", dados.SerialEquipamento);
        Assert.Null(dados.Cpf);
        Assert.Null(dados.Rg);
        Assert.Null(dados.RegistroClinico);
        Assert.Empty(dados.Avisos);
    }

    [Fact]
    public void Normaliza_cpf_formatado_para_somente_digitos()
    {
        var texto = ExtratorTextoPdf.Extrair(PdfSintetico.WinCardio(cpf: "123.456.789-09"));
        Assert.Equal("12345678909", _conector.Interpretar(texto).Cpf);
    }

    [Fact]
    public void Convenio_vazio_nao_captura_rotulo_de_derivacao()
    {
        var texto = ExtratorTextoPdf.Extrair(PdfSintetico.WinCardio(convenio: ""));
        Assert.Null(_conector.Interpretar(texto).Convenio);
    }

    [Fact]
    public void Interpreta_texto_em_linhas_com_campos_vazios()
    {
        var texto = new TextoPdf(
        [
            "ECG de Repouso",
            "Exame: 2175 Nome: TESTE Reg.Clin.: Nasc.:01/01/1999",
            "FC: 0 bpm 25 mm/s 10mm/mV 60Hz Musc RG: CPF: Data: 13/07/2026-15:42:12",
            "DI V1",
            "DII",
            "Ecg# 0002187788 © MICROMED",
        ], 1);

        var dados = _conector.Interpretar(texto);

        Assert.Equal("2175", dados.NumeroExame);
        Assert.Equal("TESTE", dados.Paciente);
        Assert.Equal(new DateOnly(1999, 1, 1), dados.DataNascimento);
        Assert.Null(dados.RegistroClinico);
        Assert.Null(dados.Convenio);
        Assert.Equal("MICROMED", dados.Fabricante);
        Assert.Equal("0002187788", dados.SerialEquipamento);
    }

    [Fact]
    public void Pdf_sem_cabecalho_gera_avisos_em_vez_de_falhar()
    {
        var dados = _conector.Interpretar(new TextoPdf(["Relatório qualquer"], 1));
        Assert.Contains(dados.Avisos, a => a.Contains("Número do exame"));
        Assert.Contains(dados.Avisos, a => a.Contains("Nome do paciente"));
    }
}
