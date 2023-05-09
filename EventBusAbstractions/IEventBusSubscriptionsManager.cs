namespace EventBusAbstractions
{
    public interface IEventBusSubscriptionsManager
    {
        void AddSubscription<T, TH>(string topic) where T : IntegrationEvent where TH : IIntegrationEventHandler<T>;

        List<Type> GetHandlersForTopic(string topic);


        bool HasSubscriptionsForEvent(string topic);

        List<string> GetAllSubscriptions();

    }


}