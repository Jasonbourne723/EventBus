namespace EventBusAbstractions
{
    public interface IEventBus
    {
        Task Publish<T>(T @event) where T : IntegrationEvent;

        Task Publish<T>(List<T> @event) where T : IntegrationEvent;

        Task Publish<T>(List<T> @event, Type eventType) where T : IntegrationEvent;
        Task Publish<T>(T @event, Type eventType) where T : IntegrationEvent;

        void Subscribe<T, TH>() where T : IntegrationEvent where TH : IIntegrationEventHandler<T>;
    }


}