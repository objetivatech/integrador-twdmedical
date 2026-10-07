using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using TwdIntegrador.App.Interface;
using TwdIntegrador.App.Sistema;
using TwdIntegrador.Core;
using TwdIntegrador.Core.Configuracao;
using Velopack;

namespace TwdIntegrador.App;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        // Precisa ser a primeira coisa: trata os ganchos de instalação/atualização do Velopack.
        VelopackApp.Build().Run();

        using var instanciaUnica = new Mutex(true, @"Local\TwdIntegrador", out var primeiraInstancia);
        if (!primeiraInstancia)
        {
            MessageBox.Show("O TWD Integrador já está em execução (ícone ao lado do relógio).",
                "TWD Integrador", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        ApplicationConfiguration.Initialize();

        var pastaDados = ConfiguracaoStore.PastaDadosPadrao();
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.File(Path.Combine(pastaDados, "logs", "integrador-.log"),
                rollingInterval: RollingInterval.Day, retainedFileCountLimit: 30)
            .CreateLogger();

        try
        {
            var host = Host.CreateDefaultBuilder()
                .ConfigureLogging(l => l.ClearProviders().AddSerilog(dispose: true))
                .ConfigureServices(s =>
                {
                    s.AddTwdIntegrador(pastaDados, new ProtetorDpapi());
                    s.AddSingleton<Atualizador>();
                })
                .Build();

            host.Start();
            Application.Run(new ContextoBandeja(host.Services));
            host.StopAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult();
            host.Dispose();
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Falha fatal");
            MessageBox.Show($"O TWD Integrador encontrou um erro e precisa ser fechado.\n\n{ex.Message}",
                "TWD Integrador", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }
}
