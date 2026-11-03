using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace TruckGeodis;

public class AzFuncHttpTruck
{
    private readonly ILogger<AzFuncHttpTruck> _logger;

    public AzFuncHttpTruck(ILogger<AzFuncHttpTruck> logger)
    {
        _logger = logger;
    }

    [Function("AzFuncHttpTruck")]
    public async Task<IActionResult> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post")] HttpRequest req)
    {
        var form = await req.ReadFormAsync();
        var trailerNo = form["trailerNo"].ToString().Trim();
        var carrier = form["carrier"].ToString().Trim();
        var pickupLocation = form["pickupLocation"].ToString().Trim();
        var existingDamage = form["existingDamage"].ToString();

        if (string.IsNullOrWhiteSpace(trailerNo)
            || string.IsNullOrWhiteSpace(carrier)
            || string.IsNullOrWhiteSpace(pickupLocation)
            || (existingDamage != "yes" && existingDamage != "no"))
        {
            return new BadRequestObjectResult(new
            {
                message = "Trailer number, carrier, pickup location, and damage choice are required."
            });
        }

        var photoUploads = new List<object>();
        foreach (var fieldName in new[] { "frontPhoto", "driverSidePhoto" })
        {
            var file = form.Files.GetFile(fieldName);
            if (file is not null)
            {
                photoUploads.Add(new
                {
                    field = fieldName,
                    fileName = Path.GetFileName(file.FileName),
                    contentType = file.ContentType,
                    size = file.Length
                });
            }
        }

        _logger.LogInformation("Received trailer condition form for trailer {TrailerNo}.", trailerNo);

        return new OkObjectResult(new
        {
            message = "Trailer condition form submitted successfully.",
            trailerNo,
            carrier,
            pickupLocation,
            existingDamage,
            damaged = form["damaged"].ToString(),
            description = form["description"].ToString(),
            photos = photoUploads
        });
    }
}