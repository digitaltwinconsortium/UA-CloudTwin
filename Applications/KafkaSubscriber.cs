
namespace UACloudTwin
{
    using Confluent.Kafka;
    using Microsoft.Extensions.Logging;
    using System;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using UACloudTwin.Interfaces;

    public class KafkaSubscriber : ISubscriber
    {
        private IConsumer<Ignore, byte[]> _dataConsumer = null;
        private IConsumer<Ignore, byte[]> _metadataConsumer = null;

        private readonly ILogger<KafkaSubscriber> _logger;
        private readonly IMessageProcessor _uaMessageProcessor;
        private readonly IDigitalTwinClient _digitalTwinClient;

        public KafkaSubscriber(IMessageProcessor uaMessageProcessor, ILogger<KafkaSubscriber> logger, IDigitalTwinClient digitalTwinClient)
        {
            _logger = logger;
            _uaMessageProcessor = uaMessageProcessor;
            _digitalTwinClient = digitalTwinClient;
        }

        public void Run()
        {
            while (true)
            {
                try
                {
                    if ((_dataConsumer == null) || (_dataConsumer.Subscription == null)
                    || (_metadataConsumer == null) || (_metadataConsumer.Subscription == null))
                    {
                        // we're not connected yet, try to (re)-connect in 5 seconds
                        Thread.Sleep(5000);

                        Connect();
                        continue;
                    }

                    _logger.LogInformation("Connected to Kafka broker.");

                    // wait for our digital twin models to be uploaded
                    while (!_digitalTwinClient.Ready)
                    {
                        _logger.LogInformation("Waiting or digital twin models to be uploaded...");

                        Thread.Sleep(3000);
                    }

                    // kick off metadata reading thread, if we have a metadata consumer
                    if (_metadataConsumer != null)
                    {
                        _logger.LogInformation("Reading metadata from broker...");

                        _ = Task.Run(() => ReadMessageFromBroker(_metadataConsumer));
                    }

                    // wait 30 seconds to first read all metadata, then start reading the actual data
                    Thread.Sleep(30 * 1000);

                    _logger.LogInformation("Starting processing of telemetry data...");

                    ReadMessageFromBroker(_dataConsumer);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex.Message);
                    Stop();
                }
            }
        }

        private void ReadMessageFromBroker(IConsumer<Ignore, byte[]> consumer)
        {
            while (true)
            {
                ConsumeResult<Ignore, byte[]> result = consumer.Consume();
                if (result.Message != null)
                {
                    lock (_uaMessageProcessor)
                    {
                        _uaMessageProcessor.ProcessMessage(result.Message.Value, result.Message.Timestamp.UtcDateTime, ReadContentTypeFromHeader(result));
                    }
                }
            }
        }

        private string ReadContentTypeFromHeader(ConsumeResult<Ignore, byte[]> result)
        {
            // read content type from header, if available
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

            return contentType;
        }

        public void Stop()
        {
            try
            {
                // disconnect if still connected
                if (_dataConsumer != null)
                {
                    _dataConsumer.Close();
                    _dataConsumer.Dispose();
                    _dataConsumer = null;
                }

                if (_metadataConsumer != null)
                {
                    _metadataConsumer.Close();
                    _metadataConsumer.Dispose();
                    _metadataConsumer = null;
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
               Stop();

                // create Kafka data topic client
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
                _dataConsumer = new ConsumerBuilder<Ignore, byte[]>(conf).Build();

                _dataConsumer.Subscribe(Environment.GetEnvironmentVariable("TOPIC"));

                if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("METADATA_TOPIC")))
                {
                    // create Kafka meatadata topic client (with a new groupId to make sure we read from the start)
                    conf = new ConsumerConfig
                    {
                        GroupId = Environment.GetEnvironmentVariable("CLIENT_NAME") + "." + Guid.NewGuid().ToString(),
                        BootstrapServers = Environment.GetEnvironmentVariable("BROKER_NAME") + ":" + Environment.GetEnvironmentVariable("BROKER_PORT"),
                        AutoOffsetReset = AutoOffsetReset.Earliest,
                        SecurityProtocol = SecurityProtocol.SaslSsl,
                        SaslMechanism = SaslMechanism.Plain,
                        SaslUsername = Environment.GetEnvironmentVariable("BROKER_USERNAME"),
                        SaslPassword = Environment.GetEnvironmentVariable("BROKER_PASSWORD")
                    };
                    _metadataConsumer = new ConsumerBuilder<Ignore, byte[]>(conf).Build();

                    _metadataConsumer.Subscribe(Environment.GetEnvironmentVariable("METADATA_TOPIC"));
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message);
            }
        }
    }
}