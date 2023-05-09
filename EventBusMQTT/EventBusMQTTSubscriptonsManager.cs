using EventBusAbstractions;

namespace EventBusMQTT
{
    public class EventBusMQTTSubscriptonsManager : IEventBusSubscriptionsManager
    {
        private readonly Dictionary<string, List<Type>> _handlers;

        public EventBusMQTTSubscriptonsManager()
        {
            _handlers = new Dictionary<string, List<Type>>();
        }


        public void AddSubscription<T, TH>(string topic) where T : IntegrationEvent where TH : IIntegrationEventHandler<T>
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
            _handlers.Keys.Where(x => topic.Contains(x.Replace("/#", "")))?.ToList().ForEach(key =>
            {
                handlers.AddRange(_handlers[key]);
            });
            return handlers;
        }

        public bool HasSubscriptionsForEvent(string topic) => _handlers.Keys.Any(x => topic.Contains(x.Replace("/#", "")));

        public void Clear() => _handlers.Clear();

        public List<string> GetAllSubscriptions() => _handlers.Keys.ToList();
    }


}