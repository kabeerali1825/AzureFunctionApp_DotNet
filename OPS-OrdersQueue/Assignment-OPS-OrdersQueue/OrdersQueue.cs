using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Azure.Messaging.ServiceBus;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.Azure.Cosmos;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.DataContracts;

namespace Assignment_OPS_OrderValidation
{
    public class OrdersQueue
    {
        private readonly ILogger<OrdersQueue> _logger;
        private readonly CosmosClient _cosmosClient;
        private readonly TelemetryClient _telemetryClient;
        private readonly string _databaseName = "OrdersProcessingSystem";
        private readonly string _containerName = "Orders_Details";
        private const int OrderProcessingTimeThreshold = 30; // In seconds (set the threshold for alert)

        public OrdersQueue(ILogger<OrdersQueue> logger, TelemetryClient telemetryClient)
        {
            _logger = logger;
            _telemetryClient = telemetryClient;

            // Initialize Cosmos Client with connection string
            string cosmosConnectionString = Environment.GetEnvironmentVariable("COSMOS_DB_CONNECTION_STRING");

            if (string.IsNullOrEmpty(cosmosConnectionString))
            {
                string errorMsg = "Cosmos DB connection string is not configured.";
                _logger.LogError(errorMsg);
                throw new InvalidOperationException(errorMsg);
            }

            _cosmosClient = new CosmosClient(cosmosConnectionString);
            _logger.LogInformation("CosmosClient initialized successfully.");
        }

        [Function(nameof(OrdersQueue))]
        public async Task Run(
            [ServiceBusTrigger("orders-queue", Connection = "ServiceBusConnectionString")] ServiceBusReceivedMessage message,
            FunctionContext context)
        {
            try
            {
                // Deserialize the message body
                var orderDetailsJson = message.Body.ToString(); // Convert binary data to string
                var orderDetails = JsonSerializer.Deserialize<Order>(orderDetailsJson, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true // Ignore case sensitivity
                });

                // Make sure the orderDetails object is not null
                if (orderDetails == null)
                {
                    _logger.LogError("Failed to deserialize order details.");
                    await SendToQueue("failed-orders-queue", message, "Deserialization error");
                    return;
                }

                // Extract the orderId
                string orderId = orderDetails.OrderId;

                _logger.LogInformation("Processing order with ID: {id}", orderId);
                _telemetryClient.TrackTrace($"Processing order with ID: {orderId}");

                // Track start time for processing delay detection
                var orderProcessingStartTime = DateTime.UtcNow;

                // Retrieve order details from Cosmos DB
                var order = await GetOrderFromCosmos(orderId);

                if (order == null)
                {
                    string errorMsg = $"Order ID {orderId} not found.";
                    _logger.LogError(errorMsg);
                    _telemetryClient.TrackException(new ExceptionTelemetry(new Exception(errorMsg)));
                    await SendToQueue("failed-orders-queue", message, "Order not found");
                    return;
                }

                // Validate order details (product availability, price accuracy)
                bool isValid = ValidateOrder(order);

                if (!isValid)
                {
                    string errorMsg = $"Order ID {orderId} validation failed.";
                    _logger.LogError(errorMsg);
                    _telemetryClient.TrackException(new ExceptionTelemetry(new Exception(errorMsg)));
                    await SendToQueue("failed-orders-queue", message, "Order validation failed");
                }
                else
                {
                    string successMsg = $"Order ID {orderId} validation successful.";
                    _logger.LogInformation(successMsg);
                    _telemetryClient.TrackEvent("OrderProcessedSuccessfully", new Dictionary<string, string>
                    {
                        {"OrderId", orderId}
                    });

                    await SendToQueue("processing-queue", message, "Order validation successful");
                }

                // Track end time and check for delays
                var orderProcessingEndTime = DateTime.UtcNow;
                var processingTime = (orderProcessingEndTime - orderProcessingStartTime).TotalSeconds;

                if (processingTime > OrderProcessingTimeThreshold)
                {
                    _logger.LogWarning($"Order {orderId} took too long to process: {processingTime} seconds.");
                    _telemetryClient.TrackMetric("OrderProcessingDelay", processingTime);
                    // Trigger an alert
                    _telemetryClient.TrackTrace($"Alert: Order {orderId} exceeded processing time threshold of {OrderProcessingTimeThreshold} seconds.");
                }
            }
            catch (Exception ex)
            {
                string errorMsg = $"Error processing order: {ex.Message}";
                _logger.LogError(errorMsg);
                _telemetryClient.TrackException(new ExceptionTelemetry(ex));
                await SendToQueue("failed-orders-queue", message, "Order processing error");
            }
        }

        private async Task<dynamic?> GetOrderFromCosmos(string orderId)
        {
            var container = _cosmosClient.GetContainer(_databaseName, _containerName);

            try
            {
                var response = await container.ReadItemAsync<dynamic>(orderId, new PartitionKey(orderId));
                _logger.LogInformation($"Retrieved order ID {orderId} from Cosmos DB.");
                return response.Resource;
            }
            catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                string errorMsg = $"Order with ID {orderId} not found in Cosmos DB.";
                _logger.LogError(errorMsg);
                return null;
            }
            catch (CosmosException ex)
            {
                string errorMsg = $"Cosmos DB error: {ex.Message}";
                _logger.LogError(errorMsg);
                throw;
            }
        }

        private bool ValidateOrder(dynamic order)
        {
            var productDetails = order.ProductDetails as IEnumerable<dynamic>;
            foreach (var product in productDetails)
            {
                if (product.Quantity <= 0 || product.UnitPrice <= 0)
                {
                    string errorMsg = $"Invalid product {product.ProductID} in the order.";
                    _logger.LogError(errorMsg);
                    return false;
                }
            }
            return true;
        }

        private async Task SendToQueue(string queueName, ServiceBusReceivedMessage originalMessage, string reason)
        {
            string serviceBusConnectionString = Environment.GetEnvironmentVariable("ServiceBusConnectionString");

            if (string.IsNullOrEmpty(serviceBusConnectionString))
            {
                string errorMsg = "Service Bus connection string is not configured.";
                _logger.LogError(errorMsg);
                return;
            }

            var client = new ServiceBusClient(serviceBusConnectionString);
            var sender = client.CreateSender(queueName);

            var newMessage = new ServiceBusMessage(originalMessage.Body)
            {
                MessageId = originalMessage.MessageId,
                Subject = reason
            };

            try
            {
                await sender.SendMessageAsync(newMessage);
                _logger.LogInformation($"Message sent to {queueName} with ID: {originalMessage.MessageId}");
            }
            catch (Exception ex)
            {
                string errorMsg = $"Error sending message to {queueName}: {ex.Message}";
                _logger.LogError(errorMsg);
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
