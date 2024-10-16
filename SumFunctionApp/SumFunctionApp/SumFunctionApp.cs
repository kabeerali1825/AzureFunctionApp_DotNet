using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using System.Net;

namespace SumFunctionApp
{
    public class SumFunctionApp
    {
        private readonly ILogger _logger;

        public SumFunctionApp(ILoggerFactory loggerFactory)
        {
            _logger = loggerFactory.CreateLogger<SumFunctionApp>();
        }

        [Function("SumFunction")]
        public async Task<HttpResponseData> Run(
            [HttpTrigger(AuthorizationLevel.Function, "post", Route = null)] HttpRequestData req)
        {
            _logger.LogInformation("C# HTTP trigger function processed a request.");

            var query = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
            string num1Str = query["num1"];
            string num2Str = query["num2"];

            if (string.IsNullOrEmpty(num1Str) || string.IsNullOrEmpty(num2Str) ||
                !int.TryParse(num1Str, out int num1) || !int.TryParse(num2Str, out int num2))
            {
                var badRequestResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequestResponse.WriteStringAsync("Please pass two valid numbers (num1 and num2) on the query string.");
                return badRequestResponse;
            }

            int sum = num1 + num2;

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteStringAsync($"The sum of {num1} and {num2} is {sum}");
            return response;
        }
    }
}
