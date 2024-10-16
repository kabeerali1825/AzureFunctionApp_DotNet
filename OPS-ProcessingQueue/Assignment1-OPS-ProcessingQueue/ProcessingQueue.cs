using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Azure.Messaging.ServiceBus;
using Azure.Storage.Blobs;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.Azure.Cosmos;

namespace Assignment1_OPS_ProcessingQueue
{
    public class ProcessingQueue
    {
        private readonly ILogger<ProcessingQueue> _logger;
        private readonly BlobServiceClient _blobServiceClient;
        private readonly CosmosClient _cosmosClient;
        private readonly string _containerName = "processed-orders";
        private readonly string _orderCompletedQueueName = "order-completed-queue";
        private readonly string _databaseName = "OrderProcessingSystem";
        private readonly string _ordersContainerName = "Orders_Details";

        public ProcessingQueue(ILogger<ProcessingQueue> logger)
        {
            _logger = logger;

            // Initialize Blob Service Client with connection string
            string blobServiceConnectionString = Environment.GetEnvironmentVariable("BlobServiceConnectionString");
            if (string.IsNullOrEmpty(blobServiceConnectionString))
            {
                throw new InvalidOperationException("Blob service connection string is not configured.");
            }
            _blobServiceClient = new BlobServiceClient(blobServiceConnectionString);

            // Initialize Cosmos Client with connection string
            string cosmosConnectionString = Environment.GetEnvironmentVariable("CosmosConnectionString");
            if (string.IsNullOrEmpty(cosmosConnectionString))
            {
                throw new InvalidOperationException("Cosmos DB connection string is not configured.");
            }
            _cosmosClient = new CosmosClient(cosmosConnectionString);
        }

        [Function(nameof(ProcessingQueue))]
        public async Task Run(
            [ServiceBusTrigger("processing-queue", Connection = "ServiceBusConnectionString")]
            ServiceBusReceivedMessage message,
            ServiceBusMessageActions messageActions)
        {
            _logger.LogInformation("Processing message with ID: {id}", message.MessageId);
            _logger.LogInformation("Message Body: {body}", Encoding.UTF8.GetString(message.Body));
            _logger.LogInformation("Message Content-Type: {contentType}", message.ContentType);

            try
            {
                // Deserialize the order details from the Service Bus message
                var orderDetails = Encoding.UTF8.GetString(message.Body);
                var order = Newtonsoft.Json.JsonConvert.DeserializeObject<Order>(orderDetails);

                // Log the order ID and status
                _logger.LogInformation("Processing order with ID: {id}, Status: {status}", order.OrderId, order.Status);



                // Update order status in Cosmos DB
                //var ordersContainer = _cosmosClient.GetContainer(_databaseName, _ordersContainerName);
                //var orderResponse = await ordersContainer.ReadItemAsync<Order>(order.OrderId, new PartitionKey(order.OrderId));

                //// Update the order status to "Processed"
                //var orderToUpdate = orderResponse.Resource;
                //orderToUpdate.Status = "Processed";
                //await ordersContainer.ReplaceItemAsync(orderToUpdate, order.OrderId, new PartitionKey(order.OrderId));

                //_logger.LogInformation("Order status updated in Cosmos DB for ID: {id}", order.OrderId);

                // Store the processed order in Blob Storage
                var containerClient = _blobServiceClient.GetBlobContainerClient(_containerName);
                await containerClient.CreateIfNotExistsAsync();
                var blobClient = containerClient.GetBlobClient($"{message.MessageId}.json");
                using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(orderDetails)))
                {
                    await blobClient.UploadAsync(stream, true);
                }
                _logger.LogInformation("Processed order stored in Blob Storage with ID: {id}", message.MessageId);

                // Send result to OrderCompletedQueue
                string serviceBusConnectionString = Environment.GetEnvironmentVariable("ServiceBusConnectionString");
                var serviceBusClient = new ServiceBusClient(serviceBusConnectionString);
                var sender = serviceBusClient.CreateSender(_orderCompletedQueueName);
                var resultMessage = new ServiceBusMessage(message.Body)
                {
                    MessageId = message.MessageId,
                    Subject = "Order completed"
                };
                await sender.SendMessageAsync(resultMessage);
                _logger.LogInformation("Result sent to {queueName} with ID: {id}", _orderCompletedQueueName, message.MessageId);

                // Complete the message
                await messageActions.CompleteMessageAsync(message);
                _logger.LogInformation("Message ID: {id} completed.", message.MessageId);
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error processing message ID {message.MessageId}: {ex.Message}");
                await messageActions.AbandonMessageAsync(message);
            }
        }
    }

    public class Order
    {
        public string OrderId { get; set; }
        public UserInfo UserInfo { get; set; }
        public IEnumerable<ProductDetail> ProductDetails { get; set; }
        public decimal OrderTotal { get; set; }
        public DateTime OrderDate { get; set; }
        public string Status { get; set; }
    }

    public class UserInfo
    {
        public string UserID { get; set; }
        public string UserName { get; set; }
        public string Email { get; set; }
        public ShippingAddress ShippingAddress { get; set; }
    }

    public class ShippingAddress
    {
        public string Street { get; set; }
        public string City { get; set; }
        public string State { get; set; }
        public string PostalCode { get; set; }
        public string Country { get; set; }
    }

    public class ProductDetail
    {
        public string ProductID { get; set; }
        public string ProductName { get; set; }
        public int Quantity { get; set; }
        public decimal UnitPrice { get; set; }
        public decimal TotalPrice { get; set; }
    }
}
