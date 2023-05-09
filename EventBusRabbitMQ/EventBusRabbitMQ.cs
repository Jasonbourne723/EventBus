using EventBusAbstractions;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using Polly;
using Polly.Retry;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using RabbitMQ.Client.Exceptions;
using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Reflection;
using System.Text;

namespace EventBusRabbitMQ
{
    public class EventBusRabbitMQ : IEventBus, IDisposable
    {

        private IConnection connection;
        private bool disposed;
        private readonly static object locker = new object();
        private readonly int retryCount = 3;


        private readonly IServiceScopeFactory serviceScopeFactory;
        private readonly ConnectionFactory connectionFactory;
        private readonly EventBusRabbitMQSubscriptonsManager eventBusRabbitMQSubscriptonsManager;
        private readonly RabbitMqConfigOptions configOptions;
        private ConcurrentDictionary<string, IModel> consumerChannels;
        private ConcurrentDictionary<string, Type> consumerModels;
        private ConcurrentDictionary<string, bool> topics;

        public EventBusRabbitMQ(IServiceScopeFactory serviceScopeFactory,
                                RabbitMqConfigOptions configOptions,
                                EventBusRabbitMQSubscriptonsManager eventBusRabbitMQSubscriptonsManager,
                                string MQName = "defult")
        {
            this.configOptions = configOptions;
            Name = MQName;
            this.serviceScopeFactory = serviceScopeFactory;
            connectionFactory = new ConnectionFactory()
            {
                HostName = configOptions.HostName,
                Port = configOptions.Port,
                UserName = configOptions.UserName,
                Password = configOptions.Password
            };
            connectionFactory.DispatchConsumersAsync = true;
            this.eventBusRabbitMQSubscriptonsManager = eventBusRabbitMQSubscriptonsManager;
            consumerChannels = new ConcurrentDictionary<string, IModel>();
            consumerModels = new ConcurrentDictionary<string, Type>();
            topics = new ConcurrentDictionary<string, bool>();
        }


        #region 私有方法

        private bool IsConnected => connection != null && connection.IsOpen && !disposed;

        private bool TryConnect()
        {
            lock (locker)
            {
                if (!IsConnected)
                {
                    var policy = RetryPolicy.Handle<SocketException>().Or<BrokerUnreachableException>()
                    .WaitAndRetryForever(retryAttempt => { return TimeSpan.FromSeconds(3); }, (ex, time) =>
                    {
                        Console.WriteLine($"RabbitMQ Client could not connect after {time.TotalSeconds}s ({ex.Message})");
                    });
                    policy.Execute(() =>
                    {
                        connection = connectionFactory.CreateConnection();
                    });
                    if (IsConnected)
                    {
                        Console.WriteLine($"RabbitMQ was Connected Successed");
                        connection.ConnectionShutdown += Connection_ConnectionShutdown;
                        connection.CallbackException += Connection_CallbackException;
                        connection.ConnectionBlocked += Connection_ConnectionBlocked;
                        return true;
                    }
                    else
                    {
                        Console.WriteLine($"RabbitMQ Client was connected failed");
                        return false;
                    }
                }
                else
                {
                    return true;
                }
            }
        }

        private bool TryConnect_1()
        {
            lock (locker)
            {
                if (!IsConnected)
                {
                    var policy = RetryPolicy.Handle<SocketException>().Or<BrokerUnreachableException>()
                    .WaitAndRetry(3, retryAttempt => { return TimeSpan.FromSeconds(1); }, (ex, time) =>
                    {
                        Console.WriteLine($"RabbitMQ Client could not connect after {time.TotalSeconds}s ({ex.Message})");
                    });
                    policy.Execute(() =>
                    {
                        connection = connectionFactory.CreateConnection();
                    });
                    if (IsConnected)
                    {
                        Console.WriteLine($"RabbitMQ was Connected Successed");
                        connection.ConnectionShutdown += Connection_ConnectionShutdown;
                        connection.CallbackException += Connection_CallbackException;
                        connection.ConnectionBlocked += Connection_ConnectionBlocked;
                        return true;
                    }
                    else
                    {
                        Console.WriteLine($"RabbitMQ Client was connected failed");
                        return false;
                    }
                }
                else
                {
                    return true;
                }
            }
        }

        private IModel CreateModel()
        {
            if (!IsConnected)
            {
                if (!TryConnect())
                {
                    throw new Exception("RabbitMQ  was Connected failed");
                }
            }
            return connection.CreateModel();
        }

        private IModel CreateConsumerChannel<T>() where T : IntegrationEvent
        {
            if (!IsConnected)
            {
                TryConnect();
            }

            var channel = CreateModel();

            var routeConfig = GetRouteConfig<T>();

            QueueDeclareAndBind<T>(channel, routeConfig);

            consumerModels.TryAdd(GetTopic<T>(), typeof(T));
            return channel;
        }

        private void StartBasicConsume<T>(IModel channel) where T : IntegrationEvent
        {
            var routeConfig = GetRouteConfig<T>();
            var consumer = new AsyncEventingBasicConsumer(channel);
            consumer.Received += Consumer_Received;
            channel.BasicConsume(routeConfig.Queue, false, consumer);
        }

        private async Task Consumer_Received(object sender, BasicDeliverEventArgs @event)
        {
            var consumer = sender as AsyncEventingBasicConsumer;
            if (consumer == null) return;
            var channel = consumer.Model;

            string topic = consumerChannels.FirstOrDefault(x => x.Value.GetHashCode() == channel.GetHashCode()).Key;
            var message = Encoding.UTF8.GetString(@event.Body.Span);
            try
            {
                if (configOptions.IsPrinted)
                {
                    Console.WriteLine($"Rabbitmq接收消息：{JsonConvert.SerializeObject(@event)}");

                }

                if (message.ToLowerInvariant().Contains("throw-fake-exception"))
                {
                    channel.BasicReject(@event.DeliveryTag, false);
                    throw new InvalidOperationException($"Fake exception requested: \"{message}\"");
                }

                if (await ProcessEvent(topic, message))
                {
                    channel.BasicAck(@event.DeliveryTag, multiple: false);
                }
                else
                {
                    channel.BasicReject(@event.DeliveryTag, false);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"----- ERROR Processing message ; {ex.Message}");
            }
        }

        private string GetTopic(BasicDeliverEventArgs @event)
        {
            return string.IsNullOrWhiteSpace(@event.Exchange) ? @event.RoutingKey : $"{@event.Exchange}_{@event.RoutingKey}";
        }

        private async Task<bool> ProcessEvent(string topic, string message)
        {
            var isAllSuccessed = true;
            if (!eventBusRabbitMQSubscriptonsManager.HasSubscriptionsForEvent(topic)) return !isAllSuccessed;

            using var scope = serviceScopeFactory.CreateScope();
            var subscriptions = eventBusRabbitMQSubscriptonsManager.GetHandlersForTopic(topic);

            foreach (var subscription in subscriptions)
            {
                if (consumerModels.TryGetValue(topic, out var eventType))
                {
                    var handlerType = typeof(IIntegrationEventHandler<>).MakeGenericType(eventType);
                    var handler = scope.ServiceProvider.GetServices(handlerType).FirstOrDefault(x => x.GetType() == subscription);
                    if (handler == null) continue;
                    try
                    {
                        var paramEventType = GetRealEventType(message, eventType);
                        var @event = JsonConvert.DeserializeObject(message, paramEventType);
                        await (Task)handlerType.GetMethod("Handle", new Type[] { paramEventType }).Invoke(handler, new object[] { @event });
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"RabbitMQ消息处理异常 topic：{topic} payload:{message} exceptionMessage:{ex.Message}");
                        isAllSuccessed = false;
                    }
                }
                else
                {
                    Console.WriteLine($"RabbitMQ消息异常  topic：{topic} 获取类型失败 ");
                    isAllSuccessed = false;
                }
            }
            return isAllSuccessed;
        }

        private Type GetRealEventType(string message, Type eventType)
        {
            return message.StartsWith('[') ? typeof(List<>).MakeGenericType(eventType) : eventType;
        }

        private void Connection_ConnectionBlocked(object? sender, RabbitMQ.Client.Events.ConnectionBlockedEventArgs e)
        {
            Console.WriteLine($"RabbitMQ ConnectionBlocked  ---- IsDisposed:{disposed}");

            if (disposed) return;

            TryConnect();
        }

        private void Connection_CallbackException(object? sender, RabbitMQ.Client.Events.CallbackExceptionEventArgs e)
        {
            Console.WriteLine($"RabbitMQ CallbackException  ---- IsDisposed:{disposed}");

            if (disposed) return;

            TryConnect();
        }

        private void Connection_ConnectionShutdown(object? sender, ShutdownEventArgs e)
        {
            Console.WriteLine($"RabbitMQ ConnectionShutdown  ---- IsDisposed:{disposed}");
            if (disposed) return;
            TryConnect();
        }

        private RabbitMQConfigAttribute GetRouteConfig<T>()
        {
            return typeof(T).GetCustomAttribute<RabbitMQConfigAttribute>(false) ?? throw new InvalidOperationException("type of event is not configured RabbitMQConfigAttribute ");
        }

        private RabbitMQConfigAttribute GetRouteConfig(Type T)
        {
            return T.GetCustomAttribute<RabbitMQConfigAttribute>(false) ?? throw new InvalidOperationException("type of event is not configured RabbitMQConfigAttribute ");
        }
        private RetryPolicy GetPolicy()
        {
            return RetryPolicy.Handle<BrokerUnreachableException>()
                             .Or<SocketException>()
                             .WaitAndRetry(retryCount, retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)), (ex, time) =>
                             {
                                 Console.WriteLine($"Could not publish event after {time.TotalSeconds:n1}s ({ex.Message})");
                             });
        }

        private void QueueDeclareAndBind<T>(IModel channel, RabbitMQConfigAttribute routeConfig) where T : IntegrationEvent
        {
            var topic = GetTopic<T>();
            if (!topics.TryGetValue(topic, out var value))
            {
                channel.ExchangeDeclare(exchange: routeConfig.Exchange, type: routeConfig.Type, true);
                Dictionary<string, object> arg = new Dictionary<string, object>();
                #region  Dead-Letter-Exchange
                if (routeConfig.HasDlx)
                {
                    var dlxName = $"{routeConfig.Exchange}.dlx";
                    var dlqName = $"{routeConfig.Queue}.dlq";
                    channel.ExchangeDeclare(dlxName, routeConfig.Type, true);
                    arg.Add("x-dead-letter-exchange", dlxName);
                    arg.Add("x-dead-letter-routing-key", routeConfig.RoutingKey);
                    channel.QueueDeclare(dlqName, true, false, false);
                    channel.QueueBind(dlqName, dlxName, routeConfig.RoutingKey);
                }
                #endregion
                channel.QueueDeclare(queue: routeConfig.Queue, durable: true, exclusive: false, autoDelete: false, arguments: arg);
                channel.QueueBind(routeConfig.Queue, routeConfig.Exchange, routeConfig.RoutingKey);
                topics.TryAdd(topic, true);
            }
        }

        private async Task PublishTo<T>(object @event, RabbitMQConfigAttribute routeConfig) where T : IntegrationEvent
        {
            await Task.Run(() =>
            {
                if (!IsConnected)
                {
                    if (!TryConnect_1()) return;
                }
                using var channel = CreateModel();
                QueueDeclareAndBind<T>(channel, routeConfig);
                var body = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(@event));
                var polly = GetPolicy();
                polly.Execute(() =>
                {
                    var properties = channel.CreateBasicProperties();
                    properties.DeliveryMode = 2;
                    channel.BasicPublish(
                        exchange: routeConfig.Exchange,
                        routingKey: routeConfig.RoutingKey,
                        mandatory: false,
                        basicProperties: properties,
                        body: body);
                });
            });
        }

        private void ReConsumerChannels<T>() where T : IntegrationEvent
        {
            string topic = GetTopic<T>();
            if (consumerChannels[topic].IsOpen)
            {
                consumerChannels[topic].Close();
                consumerChannels[topic].Dispose();
            }
            consumerChannels[topic] = CreateConsumerChannel<T>();
            StartBasicConsume<T>(consumerChannels[topic]);
        }

        private string GetTopic<T>() where T : IntegrationEvent
        {
            return typeof(T).FullName ?? throw new Exception($"rabbitmq subscribe type {typeof(T)} is null");
        }
        #endregion

        public string Name { get; set; }
        public async Task Publish<T>(List<T> @event) where T : IntegrationEvent
        {
            var routeConfig = GetRouteConfig<T>();
            await PublishTo<T>(@event, routeConfig);
        }

        public async Task Publish<T>(T @event, Type eventType) where T : IntegrationEvent
        {
            var routeConfig = GetRouteConfig(eventType);
            await PublishTo<T>(@event, routeConfig);
        }
        public async Task Publish<T>(List<T> @event, Type eventType) where T : IntegrationEvent
        {
            var routeConfig = GetRouteConfig(eventType);
            await PublishTo<T>(@event, routeConfig);
        }
        /// <summary>
        /// 发布消息
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="event"></param>
        /// <returns></returns>
        public async Task Publish<T>(T @event) where T : IntegrationEvent
        {
            var routeConfig = GetRouteConfig<T>();
            await PublishTo<T>(@event, routeConfig);
        }
        /// <summary>
        /// 订阅消息
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <typeparam name="TH"></typeparam>
        public void Subscribe<T, TH>() where T : IntegrationEvent where TH : IIntegrationEventHandler<T>
        {
            var topic = GetTopic<T>();
            var routeConfig = GetRouteConfig<T>();
            eventBusRabbitMQSubscriptonsManager.AddSubscription<T, TH>(topic);

            consumerChannels.GetOrAdd(topic, x =>
            {
                var channel = CreateConsumerChannel<T>();
                channel.CallbackException += (sender, ea) =>
                {
                    ReConsumerChannels<T>();
                };
                channel.ModelShutdown += (sender, ea) =>
                {
                    ReConsumerChannels<T>();
                };
                StartBasicConsume<T>(channel);
                return channel;
            });
        }
        /// <summary>
        /// Dispose
        /// </summary>
        public void Dispose()
        {
            if (disposed) return;

            disposed = true;

            try
            {
                connection.ConnectionShutdown -= Connection_ConnectionShutdown;
                connection.CallbackException -= Connection_CallbackException;
                connection.ConnectionBlocked -= Connection_ConnectionBlocked;
                connection.Close();
                connection.Dispose();
            }
            catch (IOException ex)
            {
                Console.WriteLine($"Rabbitmq Dispose Errors:{ex.Message}");
            }
        }
    }
}