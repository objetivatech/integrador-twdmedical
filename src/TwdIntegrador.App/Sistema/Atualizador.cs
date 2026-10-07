using Microsoft.Extensions.Logging;
using Velopack;
using Velopack.Sources;

namespace TwdIntegrador.App.Sistema;

/// <summary>
/// Atualização automática pelo Velopack. As versões são publicadas pelo CI (ver
/// .github/workflows/release.yml) e baixadas em segundo plano; a nova versão entra
/// na próxima vez que o integrador reiniciar — nunca no meio de um envio.
/// </summary>
internal sealed class Atualizador(ILogger<Atualizador> log)
{
    // Releases do GitHub. Pode ser trocado por um bucket (ex.: Cloudflare R2) com SimpleWebSource.
    private const string Repositorio = "https://github.com/objetivatech/integrador-twdmedical";

    public async Task VerificarAsync()
    {
        try
        {
            var gerenciador = new UpdateManager(new GithubSource(Repositorio, accessToken: null, prerelease: false));
            if (!gerenciador.IsInstalled) return; // rodando pelo Visual Studio / sem instalador

            var nova = await gerenciador.CheckForUpdatesAsync();
            if (nova is null) return;

            log.LogInformation("Baixando atualização {Versao}", nova.TargetFullRelease.Version);
            await gerenciador.DownloadUpdatesAsync(nova);
            gerenciador.WaitExitThenApplyUpdates(nova, silent: true, restart: true);
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Falha ao verificar atualizações");
        }
    }
}
