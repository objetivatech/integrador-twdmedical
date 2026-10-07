using Microsoft.Extensions.DependencyInjection;
using TwdIntegrador.Core.Conectores;
using TwdIntegrador.Core.Configuracao;
using TwdIntegrador.Core.Envio;
using TwdIntegrador.Core.Fila;
using TwdIntegrador.Core.Seguranca;
using TwdIntegrador.Core.Servicos;

namespace TwdIntegrador.Core;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddTwdIntegrador(this IServiceCollection services, string pastaDados, IProtetorDados protetor)
    {
        var store = new ConfiguracaoStore(pastaDados, protetor);
        services.AddSingleton(protetor);
        services.AddSingleton(store);
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(_ => new FilaEnvio(store.CaminhoBanco));
        services.AddSingleton(_ => RegistroConectores.Padrao());
        services.AddSingleton<EventosIntegrador>();
        services.AddSingleton(_ => new ClientePlataforma(new HttpClient { Timeout = TimeSpan.FromMinutes(2) }));
        services.AddSingleton<ProcessadorCaptura>();
        services.AddSingleton<EnviadorFila>();
        services.AddSingleton<IntegradorWorker>();
        services.AddHostedService(sp => sp.GetRequiredService<IntegradorWorker>());
        return services;
    }
}
