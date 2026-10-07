namespace TwdIntegrador.Core.Seguranca;

/// <summary>
/// Criptografa dados em repouso (token e cópias dos PDFs na fila).
/// No Windows a implementação usa DPAPI, vinculada ao usuário do computador.
/// </summary>
public interface IProtetorDados
{
    byte[] Proteger(byte[] dados);
    byte[] Desproteger(byte[] dados);
}

/// <summary>Sem criptografia. Apenas para testes e ferramentas de diagnóstico.</summary>
public sealed class ProtetorSemCriptografia : IProtetorDados
{
    public byte[] Proteger(byte[] dados) => dados;
    public byte[] Desproteger(byte[] dados) => dados;
}
