# UA Cloud Twin
A cloud-based Digital Twin Definition Language (DTDL) adapter for OPC UA data. It connects to an MQTT or Kafka broker, subscribes to a topic containing OPC UA PubSub telemetry messages, parses these messages and automatically extracts OPC UA-enables asset names from the telemetry stream and then creates digital twins for each asset indentified in Azure Digital Twins service in DTDL format, leveraging the ISA95 ontology. It then proceeds to update telemetry "tags" for each digital twin created with the relevant OPC UA PubSub datasets, all fully automatically.

UA Cloud Twin creates a digital twin for each namespace in each OPC UA server discovered within the OPC UA PubSub telemetry stream it reads from the broker, so for best results give each asset connected to your OPC UA servers its own namespace.

UA Cloud Twin uses username and password authentication by default, but other authentication providers can be added, please let us know what you would like and open a feature request as an issue on GitHub.

## How UA Cloud Twin maps OPC UA Metadata to Digital Twins

UA Cloud Twin creates digital twins for industrial assets by looking at the OPC UA PubSub Metadata messages' name property in the format `OPCUAApplicationURI;OPCUANamespaceURI;NodeID`. Please set your name property within a metadata message appropriately for best results.

Since the OPC UA application URI of your OPC UA servers is supposed to be globally unique, it also makes sense to set it to something meaningful, e.g. the ISA95 hierarchy enterprise->site->area->line->workcell. For instance, an OPC UA server application URI may be urn:assembly.line1.building1.munich.contoso.

When PROCESS_TELEMETRY_MESSAGES is set to 1, UA Cloud Twin also creates digital twins one level below the industrial assets digital twins based on the field names in the OPC UA PubSub DataSet messages and constructs the name in the format `OPCUAApplicationURI;OPCUANamespaceURI;FieldName;NodeID`.

If no OPC UA PubSub Metadata messages were received and the IGNORE_MISSING_METADATA environment variable is defined, UA Cloud Twin creates digital twins for assets in the format `OPCUAPubSubPublisherID` and `OPCUAPubSubPublisherID;DatasetWriterID;DatasetFieldIndex` one level below.

For the Azure Digital Twin service implementation, the OPC UA telemetry fields are assigned using the following pattern:

* OPCUADisplayName = `FieldName`
* OPCUANodeId = `OPCUANamespaceURI;NodeID`
* OPCUANodeValue = `[the Dataset Field value received, while "flattening" any OPC UA complex types received]`

## Installation

The following environment variables **must** be defined:

* ADMIN_USERNAME - the name for the admin of UA Cloud Twin
* ADMIN_PASSWORD - the password of the admin of UA Cloud Twin
* AZURE_TENANT_ID - the Azure tenant ID of your AAD instance. This can be retrieved from the Azure portal under Azure Active Directory -> Overview
* AZURE_CLIENT_ID - the Azure client ID of UA Cloud Twin. A client ID can be created through AAD app registration in the Azure portal under Azure Active Directory -> Overview -> Add -> App Registration
* AZURE_CLIENT_SECRET - the Azure client secret of UA Cloud Twin. A client secret can be added after AAD app registration under Add a certificate or secret -> New client secret

To successfully connect to an Azure Digital Twins service instance, the above AAD app registration must be assigned to the Azure Digital Twins Data Owner role.

The following environment variables **can optionally** be defined:

* BROKER_NAME - the name of the broker to use
* BROKER_PORT - the port number of the broker
* CLIENT_NAME - the client name to use with the broker
* BROKER_USERNAME - the username to use with the broker
* BROKER_PASSWORD - the password to use with the broker
* TOPIC - the broker topic to read messages from
* METADATA_TOPIC - the broker metadata topic to read messages from
* USE_MQTT - Read OPC UA PubSub telementry messages from an MQTT borker instead of a Kafka broker
* USE_TLS - set to 1 to use Transport Layer Security
* IGNORE_MISSING_METADATA - set to 1 to parse messages even if no metadata was sent for the messages
* PROCESS_TELEMETRY_MESSAGES - set to 1 to process OPC UA telemetry messages in addition to processing OPC UA metadata messages
* ADT_HOSTNAME - the hostname of the Azure Digital Twins instance UA Cloud Twin should connect to
* USE_ISA95_EQUIPMENT_MODELS - Use the ISA95 equipment models (equipment and equipment property) for the mapping from OPC UA metadata to ISA95
* USE_ADX - Use Azure Data Explorer for storing the digital twin graph instead of Azure Digital Twins service
* ADX_INSTANCE_URL - URL of ADX cluster for DTDL models
* ADX_DB_NAME - ADX database name for DTDL models
* ADX_TABLE_NAME - ADX table name for DTDL models

Alternatively, if an Azure IoT Hub or Azure Event Hubs are used for the broker, the Azure Event Hub connection string can be specified in the UI to avoid the need to specify the above environment variables. The Azure Event Hub connection string can be read in the Azure Portal for IoT Hub under Built-in Endpoints -> Event Hub-compatible endpoint and for Azure Event Hubs under Shared Access Policies -> RootManageSharedAccessKey -> Connection string-primary key.

## Usage

Run it on a Docker-enabled computer via:

    docker run -e anEnvironmentVariableFromAbove="yourSetting" -p 80:80 ghcr.io/digitaltwinconsortium/ua-cloudtwin:main

Alternatively, you can run it in a Docker-enabled web application in the Cloud.

Then point your web browser to <http://yourIPAddress>

You can optionally supply the following query parameters in the Url:

* `?endpoint=your-broker-connection-string` - the connection string of the broker to use
* `?instanceurl=your-adt-instance-url` - the URL of the Azure Digital Twins instance to use

e.g. <https://localhost:5001/Setup?endpoint=[your-connection-string]&instanceUrl=[your-adt-instance-url]>
 

## Build Status

[![Docker](https://github.com/digitaltwinconsortium/UA-CloudTwin/actions/workflows/docker-build.yml/badge.svg)](https://github.com/digitaltwinconsortium/UA-CloudTwin/actions/workflows/docker-build.yml)

