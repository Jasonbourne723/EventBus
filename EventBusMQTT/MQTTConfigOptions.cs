namespace EventBusMQTT
{
    public class MQTTConfigOptions
    {
        public string ClientId { get; set; }

        public string Ip { get; set; }

        public int Port { get; set; }

        public string UserName { get; set; }

        public string Password { get; set; }

        public bool IsPrinted { get; set; } = false;
    }


}