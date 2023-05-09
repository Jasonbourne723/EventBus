using EventBusAbstractions;
using Microsoft.Extensions.DependencyInjection;

namespace EventBusMQTT
{
    public static class MQTTExtensionDependencyInjection
    {
        public static IServiceCollection AddMQTT(this IServiceCollection services, Action<MQTTConfigOptions> action)
        {
            var options = new MQTTConfigOptions();
            action(options);
            services.AddSingleton<IEventBus>(ea =>
            {
                return new EventBusMQTT(new EventBusMQTTSubscriptonsManager(), ea.GetRequiredService<IServiceScopeFactory>(), options);
            });
            return services;
        }
    }


}