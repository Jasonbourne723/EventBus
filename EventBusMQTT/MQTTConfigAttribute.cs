namespace EventBusMQTT
{
    public class MQTTConfigAttribute : Attribute
    {
        public MQTTConfigAttribute(string topic)
        {
            Topic = topic;
        }

        public string Topic { get; set; }
    }


}