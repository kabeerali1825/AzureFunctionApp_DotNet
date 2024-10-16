using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Azure.Messaging.ServiceBus;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.DataContracts;
using System.Text;

namespace Assignment1_OPS_OrderCompletedQueue
{
    public class OrderCompletedQueue
    {
        private readonly ILogger<OrderCompletedQueue> _logger;
        private readonly TelemetryClient _telemetryClient;

        public OrderCompletedQueue(ILogger<OrderCompletedQueue> logger, TelemetryClient telemetryClient)
        {
            _logger = logger;
            _telemetryClient = telemetryClient;
        }

        [Function(nameof(OrderCompletedQueue))]
        public async Task Run(
            [ServiceBusTrigger("order-completed-queue", Connection = "ServiceBusConnectionString")]
            ServiceBusReceivedMessage message,
            ServiceBusMessageActions messageActions)
        {
            var orderId = message.MessageId;
            var orderDetails = Encoding.UTF8.GetString(message.Body);

            // Track the start time
            var startTime = DateTime.UtcNow;

            _logger.LogInformation("Processing completed order with ID: {orderId}", orderId);
            _logger.LogInformation("Order Details: {orderDetails}", orderDetails);

            // Log detailed order information to Application Insights
            var telemetry = new TraceTelemetry($"Order ID: {orderId}", SeverityLevel.Information);
            telemetry.Properties.Add("OrderDetails", orderDetails);
            _telemetryClient.TrackTrace(telemetry);

            try
            {
                // Process the order
                await ProcessOrderAsync(orderDetails);

                // Complete the message
                await messageActions.CompleteMessageAsync(message);
                _logger.LogInformation("Message ID: {orderId} completed.", orderId);
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error processing completed order ID {orderId}: {ex.Message}");
                _telemetryClient.TrackException(new ExceptionTelemetry(ex)
                {
                    SeverityLevel = SeverityLevel.Error
                });
                await messageActions.AbandonMessageAsync(message);
            }
            finally
            {
                // Track the end time and calculate the processing duration
                var endTime = DateTime.UtcNow;
                var duration = endTime - startTime;
                _telemetryClient.TrackMetric("OrderProcessingDuration", duration.TotalSeconds);

                // Check if the processing took too long
                if (duration > TimeSpan.FromSeconds(10)) // 10 seconds threshold for alerting
                {
                    _telemetryClient.TrackTrace($"Order ID: {orderId} took too long to process.", SeverityLevel.Warning);
                }
            }
        }

        private async Task ProcessOrderAsync(string orderDetails)
        {
            // Simulate order processing
            await Task.Delay(100); // Replace with actual order processing logic
        }
    }

    public class Order
    {
        public string OrderID { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public IEnumerable<ProductDetail> ProductDetails { get; set; } = new List<ProductDetail>();
    }

    public class ProductDetail
    {
        public string ProductID { get; set; } = string.Empty;
        public int Quantity { get; set; }
    }
}
