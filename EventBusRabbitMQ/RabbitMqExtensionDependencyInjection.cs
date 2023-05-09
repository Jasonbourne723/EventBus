using EventBusAbstractions;
using Microsoft.Extensions.DependencyInjection;

namespace EventBusRabbitMQ
{
    public static class RabbitMqExtensionDependencyInjection
    {
        public static IServiceCollection AddRabbitMQ(this IServiceCollection services, Action<RabbitMqConfigOptions> action)
        {
            services.AddSingleton<IEventBus>(provider =>
            {
                var rabbitMqConfigOptions = new RabbitMqConfigOptions();
                action(rabbitMqConfigOptions);
                return new EventBusRabbitMQ(
                   provider.GetRequiredService<IServiceScopeFactory>(),
                   rabbitMqConfigOptions,
                    new EventBusRabbitMQSubscriptonsManager());
            });
            return services;
        }
    }
}