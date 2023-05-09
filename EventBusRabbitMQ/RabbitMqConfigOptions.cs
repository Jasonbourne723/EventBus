namespace EventBusRabbitMQ
{
    public class RabbitMqConfigOptions
    {
        public string HostName { get; set; }

        public int Port { get; set; }

        public string UserName { get; set; }

        public string Password { get; set; }

        public bool IsPrinted { get; set; }
    }
}