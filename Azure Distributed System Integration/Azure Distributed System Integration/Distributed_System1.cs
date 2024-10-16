using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Azure.Messaging.ServiceBus;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Azure_Distributed_System_Integration
{
    public class Distributed_System1
    {
        private readonly ILogger<Distributed_System1> _logger;
        private readonly BlobServiceClient _blobServiceClient;

        public Distributed_System1(ILogger<Distributed_System1> logger, IConfiguration config)
        {
            _logger = logger;
            // Initialize BlobServiceClient using the connection string
            _blobServiceClient = new BlobServiceClient(config["BlobStorageConnectionString"]);
        }

        [Function(nameof(Distributed_System1))]
        public async Task Run(
            [ServiceBusTrigger("asp-processing-assignment2", Connection = "ServiceBusConnection")]
            ServiceBusReceivedMessage message,
            ServiceBusMessageActions messageActions)
        {
            _logger.LogInformation("Processing message: {MessageId} with SequenceNumber: {SequenceNumber}", message.MessageId, message.SequenceNumber);

            try
            {
                // Parse the message body (JSON payload) to get the Blob URL
                string messageBody = message.Body.ToString();
                _logger.LogInformation("Received message body: {messageBody}", messageBody);

                // Deserialize the JSON payload
                var messageJson = JsonDocument.Parse(messageBody);
                var blobUri = messageJson.RootElement.GetProperty("data").GetProperty("url").GetString();

                if (string.IsNullOrEmpty(blobUri))
                {
                    _logger.LogError("Blob URI is missing or invalid in the message.");
                    await messageActions.AbandonMessageAsync(message);
                    return;
                }

                _logger.LogInformation("Blob URI: {blobUri}", blobUri);

                // Download the file from Blob Storage
                BlobClient blobClient = new BlobClient(new Uri(blobUri), new BlobClientOptions());
                BlobDownloadInfo download;

                try
                {
                    download = await blobClient.DownloadAsync();
                }
                catch (Exception blobException)
                {
                    _logger.LogError($"Failed to download blob from {blobUri}. Error: {blobException.Message}");
                    await messageActions.AbandonMessageAsync(message);
                    return;
                }

                using (var reader = new StreamReader(download.Content))
                {
                    string fileContent = await reader.ReadToEndAsync();
                    _logger.LogInformation("Downloaded file content: {fileContent}", fileContent);

                    // Simulate processing the file (e.g., converting format or parsing)
                    string processedContent = ProcessFile(fileContent);

                    // Store the processed result in a new Blob Container using the BlobServiceClient
                    string resultContainerName = "processed-results";
                    BlobContainerClient resultContainerClient = _blobServiceClient.GetBlobContainerClient(resultContainerName);
                    await resultContainerClient.CreateIfNotExistsAsync(PublicAccessType.None);

                    BlobClient resultBlobClient = resultContainerClient.GetBlobClient($"Processed-{Guid.NewGuid()}.txt");
                    using (var memoryStream = new MemoryStream(Encoding.UTF8.GetBytes(processedContent)))
                    {
                        await resultBlobClient.UploadAsync(memoryStream, overwrite: true);
                        _logger.LogInformation("Processed file uploaded to {Uri}", resultBlobClient.Uri);
                    }

                    // Send a message to the ResultQueue after processing
                    var resultMessage = new ServiceBusMessage($"Processed file stored at {resultBlobClient.Uri}");
                    await messageActions.CompleteMessageAsync(message); // Completing the original message
                    _logger.LogInformation("Message successfully processed and uploaded to ResultQueue");
                }
            }
            catch (JsonException jsonEx)
            {
                _logger.LogError($"Failed to parse message JSON. Error: {jsonEx.Message}");
                await messageActions.AbandonMessageAsync(message);
            }
            catch (Exception ex)
            {
                _logger.LogError($"Unexpected error processing message: {ex.Message}");
                // Optionally, move to dead-letter or retry queue if necessary
                await messageActions.AbandonMessageAsync(message);
            }
        }

        private string ProcessFile(string fileContent)
        {
            // Simulate file processing logic (e.g., transforming or parsing content)
            _logger.LogInformation("Simulating file processing...");
            return fileContent.ToUpper(); // Example: just convert the content to uppercase
        }
    }
}
