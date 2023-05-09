namespace EventBusAbstractions
{
    public interface IIntegrationEventHandler<T> where T : IntegrationEvent
    {
        Task Handle(T message);

        Task Handle(List<T> message);
    }



}