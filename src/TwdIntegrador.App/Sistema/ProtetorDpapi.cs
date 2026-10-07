using System.Security.Cryptography;
using TwdIntegrador.Core.Seguranca;

namespace TwdIntegrador.App.Sistema;

/// <summary>
/// DPAPI do Windows: só o mesmo usuário, no mesmo computador, consegue descriptografar.
/// Copiar a pasta de dados para outra máquina não expõe o token nem os PDFs da fila.
/// </summary>
internal sealed class ProtetorDpapi : IProtetorDados
{
    private static readonly byte[] Entropia = "TWD-Integrador-v1"u8.ToArray();

    public byte[] Proteger(byte[] dados) => ProtectedData.Protect(dados, Entropia, DataProtectionScope.CurrentUser);

    public byte[] Desproteger(byte[] dados) => ProtectedData.Unprotect(dados, Entropia, DataProtectionScope.CurrentUser);
}
