using EventBusAbstractions;
using Microsoft.Extensions.DependencyInjection;
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Protocol;
using Newtonsoft.Json;
using System.Text;

namespace EventBusMQTT
{
    public class EventBusMQTT : IEventBus, IDisposable
    {
        MqttFactory factory;
        IMqttClient client;
        private readonly IEventBusSubscriptionsManager eventBusSubscriptionsManager;
        private readonly IServiceScopeFactory serviceScopeFactory;
        private readonly MQTTConfigOptions configOptions;
        private object locker = new object();
        private Dictionary<string, Type> consumerModels;

        public EventBusMQTT(IEventBusSubscriptionsManager eventBusSubscriptionsManager, IServiceScopeFactory serviceScopeFactory, MQTTConfigOptions configOptions)
        {
            this.eventBusSubscriptionsManager = eventBusSubscriptionsManager;
            this.serviceScopeFactory = serviceScopeFactory;
            this.configOptions = configOptions;
            consumerModels = new Dictionary<string, Type>();
            MQTTClientInit();
        }

        private async Task<bool> TryConnect()
        {
            lock (locker)
            {
                if (client.IsConnected) return true;

                client.ConnectAsync(RefreshClientOption()).GetAwaiter().GetResult();
                if (!client.IsConnected)
                {
                    Console.WriteLine("Fatal Error : MQTT connections could not be created and opened");
                    return false;
                }
                return true;
            }
        }

        private MQTTConfigAttribute GetRouteConfig(Type T)
        {
            var attribute = T.GetCustomAttributes(typeof(MQTTConfigAttribute), false)?.ToList()?.FirstOrDefault();
            if (attribute == null)
            {
                throw new InvalidOperationException("type of event is not configured MQTTConfigAttribute ");
            }
            return attribute as MQTTConfigAttribute;
        }

        private async void MQTTClientInit()
        {
            factory = new MqttFactory();
            client = factory.CreateMqttClient();
            client.ConnectedAsync += Client_ConnectedAsync;
            client.DisconnectedAsync += Client_DisconnectedAsync;
            client.ApplicationMessageReceivedAsync += Client_ApplicationMessageReceivedAsync;
        }

        private async Task Client_ApplicationMessageReceivedAsync(MqttApplicationMessageReceivedEventArgs arg)
        {
            await Task.Run(() =>
            {
                if (configOptions.IsPrinted)
                {
                    Console.WriteLine($"接收消息：topic ：{arg.ApplicationMessage.Topic}  message: {Encoding.UTF8.GetString(arg.ApplicationMessage.Payload)}");
                }
                if (eventBusSubscriptionsManager.HasSubscriptionsForEvent(arg.ApplicationMessage.Topic))
                {
                    MessageHandler(arg);
                }
            });
        }

        private async Task Client_DisconnectedAsync(MqttClientDisconnectedEventArgs arg)
        {
            Console.WriteLine("MQTT 连接已断开");
            Thread.Sleep(2000);
            await TryConnect();
        }

        private async Task Client_ConnectedAsync(MqttClientConnectedEventArgs arg)
        {
            await Task.Run(() =>
            {
                Console.WriteLine("MQTT connections already be created");
                eventBusSubscriptionsManager.GetAllSubscriptions()?.ForEach(topic =>
                {
                    client.SubscribeAsync(topic).Wait();
                });
            });
        }

        private void MessageHandler(MqttApplicationMessageReceivedEventArgs arg)
        {
            using (var scope = serviceScopeFactory.CreateScope())
            {
                var subscriptions = eventBusSubscriptionsManager.GetHandlersForTopic(arg.ApplicationMessage.Topic);

                foreach (var subscription in subscriptions)
                {
                    var topic = consumerModels.Keys.FirstOrDefault(x => arg.ApplicationMessage.Topic.Contains(x.Replace("/#", "")));

                    if (consumerModels.TryGetValue(topic, out var eventType))
                    {
                        var handlerType = typeof(IIntegrationEventHandler<>).MakeGenericType(eventType);
                        var handler = scope.ServiceProvider.GetServices(handlerType).FirstOrDefault(x => x.GetType() == subscription);
                        if (handler == null) continue;
                        try
                        {
                            var message = Encoding.UTF8.GetString(arg.ApplicationMessage.Payload);

                            var paramEventType = GetRealEventType(message, eventType);
                            var @event = JsonConvert.DeserializeObject(message, paramEventType);
                            handlerType.GetMethod("Handle", new Type[] { paramEventType }).Invoke(handler, new object[] { @event });

                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"MQTT消息处理异常 topic：{arg.ApplicationMessage.Topic} payload:{arg.ApplicationMessage.Payload} exceptionMessage:{ex.Message}");
                        }
                    }
                    else
                    {
                        Console.WriteLine($"MQTT消息处理异常  topic：{topic} 获取类型失败 ");
                    }
                }
            }
        }

        private Type GetRealEventType(string message, Type eventType)
        {
            return message.StartsWith('[') ? typeof(List<>).MakeGenericType(eventType) : eventType;
        }

        private MqttClientOptions RefreshClientOption()
        {
            return new MqttClientOptionsBuilder()
                .WithClientId(configOptions.ClientId)
                .WithTcpServer(configOptions.Ip, configOptions.Port)
                .WithCredentials(configOptions.UserName,/* MD5Generater.md5(configuration["MQTT:Credentials"])*/ configOptions.Password)
                .WithCleanSession()
                .Build();
        }


        public void Dispose()
        {
            if (client != null)
            {
                client.Dispose();
            }

        }

        private async Task PublishAsync(string topic, string message)
        {
            if (!client.IsConnected)
            {
                await TryConnect();
            }
            var msg = new MqttApplicationMessageBuilder()
                    .WithTopic(topic)
                    .WithPayload(message)
                    .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
                    .WithRetainFlag(false)
                    .Build();
            await client.PublishAsync(msg);
        }

        public void Subscribe<T, TH>()
            where T : IntegrationEvent
            where TH : IIntegrationEventHandler<T>
        {
            try
            {
                if (!client.IsConnected)
                {
                    TryConnect();
                }

                var routeConfig = GetRouteConfig(typeof(T));
                eventBusSubscriptionsManager.AddSubscription<T, TH>(routeConfig.Topic);
                consumerModels.TryAdd(routeConfig.Topic, typeof(T));
                client.SubscribeAsync(routeConfig.Topic).Wait();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"MQTT message subscribe failed ,{ex.Message}");
            }
        }

        public async Task Publish<T>(T @event) where T : IntegrationEvent
        {
            var routeConfig = GetRouteConfig(typeof(T));

            await PublishAsync(routeConfig.Topic, JsonConvert.SerializeObject(@event));
        }

        public async Task Publish<T>(List<T> @event) where T : IntegrationEvent
        {
            var routeConfig = GetRouteConfig(typeof(T));

            await PublishAsync(routeConfig.Topic, JsonConvert.SerializeObject(@event));
        }


        public async Task Publish<T>(List<T> @event, Type eventType) where T : IntegrationEvent
        {
            var routeConfig = GetRouteConfig(eventType);

            await PublishAsync(routeConfig.Topic, JsonConvert.SerializeObject(@event));
        }

        public async Task Publish<T>(T @event, Type eventType) where T : IntegrationEvent
        {
            var routeConfig = GetRouteConfig(eventType);

            await PublishAsync(routeConfig.Topic, JsonConvert.SerializeObject(@event));
        }
    }


}