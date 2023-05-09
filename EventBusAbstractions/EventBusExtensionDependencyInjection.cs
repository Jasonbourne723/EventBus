using Microsoft.Extensions.DependencyInjection;

namespace EventBusAbstractions
{
    public static class EventBusExtensionDependencyInjection
    {
        public static IServiceCollection AddIntegrationEventHandler(this IServiceCollection services, System.Reflection.Assembly assembly)
        {
            //var entities = assembly.GetExportedTypes().Where(x => x.IsGenericType && x.GetGenericTypeDefinition() == typeof(IIntegrationEventHandler<>))?.ToList();

            assembly.GetExportedTypes()?.ToList().ForEach(ea =>
            {
                var integrationEventHandler = ea.GetInterfaces().FirstOrDefault(x => x.IsGenericType && x.GetGenericTypeDefinition() == typeof(IIntegrationEventHandler<>));
                if (integrationEventHandler != null)
                {
                    services.AddTransient(integrationEventHandler, ea);
                }
            });
            return services;
        }
    }


}