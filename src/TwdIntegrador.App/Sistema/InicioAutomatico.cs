using Microsoft.Win32;

namespace TwdIntegrador.App.Sistema;

/// <summary>Inicia o integrador junto com o Windows (chave Run do usuário — sem precisar de administrador).</summary>
internal static class InicioAutomatico
{
    private const string Chave = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string Nome = "TwdIntegrador";

    public static bool Ativo
    {
        get
        {
            using var k = Registry.CurrentUser.OpenSubKey(Chave);
            return k?.GetValue(Nome) is string;
        }
    }

    public static void Definir(bool ativo)
    {
        using var k = Registry.CurrentUser.CreateSubKey(Chave);
        if (ativo) k.SetValue(Nome, $"\"{Environment.ProcessPath}\"");
        else k.DeleteValue(Nome, throwOnMissingValue: false);
    }
}
