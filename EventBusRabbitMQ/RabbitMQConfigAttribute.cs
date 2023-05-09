namespace EventBusRabbitMQ
{
    public class RabbitMQConfigAttribute : Attribute
    {
        public RabbitMQConfigAttribute(string exchange, string queue, string routingKey, string type, bool hasDlx = true)
        {
            Exchange = exchange;
            Queue = queue;
            RoutingKey = routingKey;
            Type = type;
            HasDlx = hasDlx;
        }
        /// <summary>
        /// 交换机名称
        /// </summary>
        public string Exchange { get; set; }
        /// <summary>
        /// 队列名称
        /// </summary>
        public string Queue { get; set; }
        /// <summary>
        /// routekey
        /// </summary>
        public string RoutingKey { get; set; }
        /// <summary>
        /// 交换机类型
        /// </summary>
        public string Type { get; set; }
        /// <summary>
        /// topic
        /// </summary>
        public string Topic { get { return $"{Exchange}_{RoutingKey}"; } }
        /// <summary>
        /// 是否创建死信队列 
        /// exchangeName: *.dlx
        /// queueName : *.dlq
        /// </summary>
        public bool HasDlx { get; set; }
    }
}