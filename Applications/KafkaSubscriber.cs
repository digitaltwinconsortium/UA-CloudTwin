
namespace UACloudTwin
{
    using Confluent.Kafka;
    using Microsoft.Extensions.Logging;
    using System;
    using System.Collections.Generic;
    using System.Text;
    using System.Threading;
    using UACloudTwin.Interfaces;
    using static Confluent.Kafka.ConfigPropertyNames;

    public class KafkaSubscriber : ISubscriber
    {
        private IConsumer<Ignore, byte[]> _consumer = null;

        private readonly ILogger<KafkaSubscriber> _logger;
        private readonly IMessageProcessor _uaMessageProcessor;

        public KafkaSubscriber(IMessageProcessor uaMessageProcessor, ILogger<KafkaSubscriber> logger)
        {
            _logger = logger;
            _uaMessageProcessor = uaMessageProcessor;
        }

        public void Run()
        {
            while (true)
            {
                try
                {
                    if ((_consumer == null) || (_consumer.Subscription == null))
                    {
                        // we're not connected yet, try to (re)-connect in 5 seconds
                        Thread.Sleep(5000);

                        Connect();

                        continue;
                    }

                    ConsumeResult<Ignore, byte[]> result = _consumer.Consume();

                    if (result.Message != null)
                    {
                        string contentType = "application/json";
                        if (result.Message.Headers != null && result.Message.Headers.Count > 0)
                        {
                            foreach (var header in result.Message.Headers)
                            {
                                if (header.Key.Equals("Content-Type"))
                                {
                                    contentType = Encoding.UTF8.GetString(header.GetValueBytes());
                                }
                            }
                        }

                        _uaMessageProcessor.ProcessMessage(result.Message.Value, result.Message.Timestamp.UtcDateTime, contentType);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex.Message);
                    Stop();
                }
            }
        }

        public void Stop()
        {
            try
            {
                // disconnect if still connected
                if (_consumer != null)
                {
                    _consumer.Close();
                    _consumer.Dispose();
                    _consumer = null;
                }
            }
            catch (Exception)
            {
                // do nothing
            }
        }

        public void Connect()
        {
            try
            {
                // disconnect if still connected
                if (_consumer != null)
                {
                    _consumer.Close();
                    _consumer.Dispose();
                    _consumer = null;
                }

                // create Kafka client
                var conf = new ConsumerConfig
                {
                    GroupId = Environment.GetEnvironmentVariable("CLIENT_NAME"),
                    BootstrapServers = Environment.GetEnvironmentVariable("BROKER_NAME") + ":" + Environment.GetEnvironmentVariable("BROKER_PORT"),
                    AutoOffsetReset = AutoOffsetReset.Earliest,
                    SecurityProtocol = SecurityProtocol.SaslSsl,
                    SaslMechanism = SaslMechanism.Plain,
                    SaslUsername = Environment.GetEnvironmentVariable("BROKER_USERNAME"),
                    SaslPassword = Environment.GetEnvironmentVariable("BROKER_PASSWORD")
                };

                _consumer = new ConsumerBuilder<Ignore, byte[]>(conf).Build();

                _consumer.Subscribe(Environment.GetEnvironmentVariable("TOPIC"));

                if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("METADATA_TOPIC")))
                {
                    _consumer.Subscribe(new List<string>() {
                        Environment.GetEnvironmentVariable("TOPIC"),
                        Environment.GetEnvironmentVariable("METADATA_TOPIC")
                    });

                    // read all metadata messages stored in the broker (i.e. set the offset to te beginning)
                    List<TopicPartitionOffset> offsets = _consumer.Committed(TimeSpan.FromSeconds(5));
                    for (int i = 0; i < _consumer.Assignment.Count; i++)
                    {
                        if (_consumer.Assignment[i].Topic == Environment.GetEnvironmentVariable("METADATA_TOPIC"))
                        {
                            offsets[i] = new TopicPartitionOffset(_consumer.Assignment[i], new Offset(0));
                            break;
                        }
                    }
                    _consumer.Assign(offsets);
                }

                _logger.LogInformation("Connected to Kafka broker.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message);
            }
        }
    }
}