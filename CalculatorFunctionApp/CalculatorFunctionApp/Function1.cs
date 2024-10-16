using System.IO;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;  // Ensure this is included
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;


public static class CalculatorFunction
{
    [FunctionName("Calculate")]
    public static IActionResult Run(
        [Microsoft.Azure.WebJobs.HttpTrigger(AuthorizationLevel.Function, "get", "post", Route = null)] HttpRequest req,
        ILogger log)
    {
        log.LogInformation("Processing a calculation request.");

        string operation = req.Query["operation"];
        string num1Str = req.Query["num1"];
        string num2Str = req.Query["num2"];

        if (!double.TryParse(num1Str, out double num1) || !double.TryParse(num2Str, out double num2))
        {
            return new BadRequestObjectResult("Invalid numbers.");
        }

        double result;
        switch (operation.ToLower())
        {
            case "add":
                result = num1 + num2;
                break;
            case "subtract":
                result = num1 - num2;
                break;
            case "multiply":
                result = num1 * num2;
                break;
            case "divide":
                if (num2 == 0)
                {
                    return new BadRequestObjectResult("Cannot divide by zero.");
                }
                result = num1 / num2;
                break;
            default:
                return new BadRequestObjectResult("Invalid operation.");
        }

        return new OkObjectResult($"The result is {result}");
    }
}
