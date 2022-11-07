
namespace UACloudTwin
{
    using Microsoft.Extensions.Logging;
    using MQTTnet;
    using MQTTnet.Adapter;
    using MQTTnet.Client;
    using MQTTnet.Packets;
    using MQTTnet.Protocol;
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using UACloudTwin.Interfaces;

    public class MQTTSubscriber : ISubscriber
    {
        private IMqttClient _client = null;
        private readonly ILogger<MQTTSubscriber> _logger;
        private IMessageProcessor _uaMessageProcessor;

        public MQTTSubscriber(IMessageProcessor uaMessageProcessor, ILogger<MQTTSubscriber> logger)
        {
            _logger = logger;
            _uaMessageProcessor = uaMessageProcessor;
        }

        public void Run()
        {
            // try to connect every 5 seconds
            while (!Connect())
            {
                Thread.Sleep(5000);
            }
        }

        private bool Connect()
        {
            try
            {
                // disconnect if still connected
                if ((_client != null) && _client.IsConnected)
                {
                    _client.DisconnectAsync().GetAwaiter().GetResult();
                    _client.Dispose();
                    _client = null;

                    Thread.Sleep(3000);
                }

                // create MQTT client
                _client = new MqttFactory().CreateMqttClient();
                _client.ApplicationMessageReceivedAsync += msg => HandleMessageAsync(msg);
                var clientOptions = new MqttClientOptionsBuilder()
                    .WithTcpServer(opt => opt.NoDelay = true)
                    .WithClientId(Environment.GetEnvironmentVariable("CLIENT_NAME"))
                    .WithTcpServer(Environment.GetEnvironmentVariable("BROKER_NAME"), int.Parse(Environment.GetEnvironmentVariable("BROKER_PORT")))
                    .WithTls(new MqttClientOptionsBuilderTlsParameters { UseTls = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("USE_TLS")) })
                    .WithProtocolVersion(MQTTnet.Formatter.MqttProtocolVersion.V311)
                    .WithTimeout(TimeSpan.FromSeconds(10))
                    .WithKeepAlivePeriod(TimeSpan.FromSeconds(100))
                    .WithCleanSession(true) // clear existing subscriptions
                    .WithCredentials(Environment.GetEnvironmentVariable("BROKER_USERNAME"), Environment.GetEnvironmentVariable("BROKER_PASSWORD"));

                // setup disconnection handling
                _client.DisconnectedAsync += disconnectArgs =>
                {
                    _logger.LogInformation($"Disconnected from MQTT broker: {disconnectArgs.Reason}");

                    // wait a 5 seconds, then simply reconnect again, if needed
                    Thread.Sleep(5000);

                    if ((_client == null) || !_client.IsConnected)
                    {
                        Connect();
                    }

                    return Task.CompletedTask;
                };

                try
                {
                    var connectResult = _client.ConnectAsync(clientOptions.Build(), CancellationToken.None).GetAwaiter().GetResult();
                    if (connectResult.ResultCode != MqttClientConnectResultCode.Success)
                    {
                        var status = GetStatus(connectResult.UserProperties)?.ToString("x4");
                        throw new Exception($"Connection to MQTT broker failed. Status: {connectResult.ResultCode}; status: {status}");
                    }

                    var subscribeResult = _client.SubscribeAsync(
                        new MqttTopicFilter
                        {
                            Topic = Environment.GetEnvironmentVariable("TOPIC"),
                            QualityOfServiceLevel = MqttQualityOfServiceLevel.AtMostOnce
                        }).GetAwaiter().GetResult();

                    // make sure subscriptions were successful
                    if (subscribeResult.Items.Count != 1 || subscribeResult.Items.ElementAt(0).ResultCode != MqttClientSubscribeResultCode.GrantedQoS0)
                    {
                        throw new ApplicationException("Failed to subscribe");
                    }

                    _logger.LogInformation("Connected to MQTT broker.");

                    return true;
                }
                catch (MqttConnectingFailedException ex)
                {
                    _logger.LogError($"Failed to connect with reason {ex.ResultCode} and message: {ex.Message}");
                    if (ex.Result?.UserProperties != null)
                    {
                        foreach (var prop in ex.Result.UserProperties)
                        {
                            _logger.LogError($"{prop.Name}: {prop.Value}");
                        }
                    }

                    return false;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError("Failed to connect to MQTT broker: " + ex.Message);

                return false;
            }
        }

        private MqttApplicationMessage BuildResponse(string status, string id, string responseTopic, byte[] payload)
        {
            return new MqttApplicationMessageBuilder()
                .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
                .WithTopic($"{responseTopic}/{status}/{id}")
                .WithPayload(payload)
                .Build();
        }

        // parses status from packet properties
        private int? GetStatus(List<MqttUserProperty> properties)
        {
            var status = properties.FirstOrDefault(up => up.Name == "status");
            if (status == null)
            {
                return null;
            }

            return int.Parse(status.Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        }

        // handles all incoming messages
        private async Task HandleMessageAsync(MqttApplicationMessageReceivedEventArgs args)
        {
            _logger.LogInformation($"Received message from topic: {args.ApplicationMessage.Topic}");

            try
            {
                _uaMessageProcessor.ProcessMessage(args.ApplicationMessage.Payload, DateTime.UtcNow, args.ApplicationMessage.ContentType);

                // send reponse to MQTT broker, if required
                if (args.ApplicationMessage.ResponseTopic != null)
                {
                    byte[] responsePayload = Encoding.UTF8.GetBytes("Success");
                    await _client.PublishAsync(BuildResponse("200", string.Empty, args.ApplicationMessage.ResponseTopic, responsePayload)).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message);

                // send error to MQTT broker, if required
                if (args.ApplicationMessage.ResponseTopic != null)
                {
                    await _client.PublishAsync(BuildResponse("500", string.Empty, args.ApplicationMessage.ResponseTopic, Encoding.UTF8.GetBytes(ex.Message))).ConfigureAwait(false);
                }
            }
        }
    }
}