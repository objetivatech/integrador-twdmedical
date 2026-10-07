using System.Security.Cryptography;

namespace TwdIntegrador.Core.Seguranca;

/// <summary>Hash PBKDF2 para a senha que protege a tela de configurações.</summary>
public static class Senha
{
    private const int Iteracoes = 100_000;

    public static string GerarHash(string senha)
    {
        var sal = RandomNumberGenerator.GetBytes(16);
        var hash = Rfc2898DeriveBytes.Pbkdf2(senha, sal, Iteracoes, HashAlgorithmName.SHA256, 32);
        return $"{Iteracoes}.{Convert.ToBase64String(sal)}.{Convert.ToBase64String(hash)}";
    }

    public static bool Verificar(string senha, string? hashArmazenado)
    {
        if (string.IsNullOrEmpty(hashArmazenado)) return true;
        var partes = hashArmazenado.Split('.');
        if (partes.Length != 3 || !int.TryParse(partes[0], out var iteracoes)) return false;
        var sal = Convert.FromBase64String(partes[1]);
        var esperado = Convert.FromBase64String(partes[2]);
        var obtido = Rfc2898DeriveBytes.Pbkdf2(senha, sal, iteracoes, HashAlgorithmName.SHA256, esperado.Length);
        return CryptographicOperations.FixedTimeEquals(obtido, esperado);
    }
}
