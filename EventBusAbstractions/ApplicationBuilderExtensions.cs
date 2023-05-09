using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace EventBusAbstractions
{
    public static class ApplicationBuilderExtensions
    {
        public static IApplicationBuilder UseSubscribe<T, TH>(this IApplicationBuilder app) where T : IntegrationEvent where TH : IIntegrationEventHandler<T>
        {
            var eventBus = app.ApplicationServices.GetRequiredService<IEventBus>();
            eventBus.Subscribe<T, TH>();
            return app;
        }

    }


}