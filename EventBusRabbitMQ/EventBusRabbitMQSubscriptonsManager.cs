using EventBusAbstractions;

namespace EventBusRabbitMQ
{
    public class EventBusRabbitMQSubscriptonsManager : IEventBusSubscriptionsManager
    {
        private readonly Dictionary<string, List<Type>> _handlers;


        public EventBusRabbitMQSubscriptonsManager()
        {
            _handlers = new Dictionary<string, List<Type>>();
        }


        public void AddSubscription<T, TH>(string topic) where T : IntegrationEvent  where TH : IIntegrationEventHandler<T>
        {
            if (!_handlers.ContainsKey(topic))
            {
                _handlers.Add(topic, new List<Type>());
            }
            var handlerType = typeof(TH);
            if (_handlers[topic].Any(s => s == typeof(TH)))
            {
                throw new ArgumentException($"Handler Type {handlerType.Name} already registered for '{topic}'", nameof(handlerType));
            }
            _handlers[topic].Add(handlerType);
        }

        public List<Type> GetHandlersForTopic(string topic)
        {
            var handlers = new List<Type>();
            _handlers.Keys.Where(x => x == topic)?.ToList().ForEach(key =>
            {
                handlers.AddRange(_handlers[key]);
            });
            return handlers;
        }

        public bool HasSubscriptionsForEvent(string topic) => _handlers.Keys.Any(x => x == topic);


        public List<string> GetAllSubscriptions() => _handlers.Keys.ToList();
    }


}