using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace TruckGeodis;

public class AzFuncHttpTruck
{
    private readonly ILogger<AzFuncHttpTruck> _logger;
    private readonly SharePointTruckRequestService _sharePoint;

    public AzFuncHttpTruck(
        ILogger<AzFuncHttpTruck> logger,
        SharePointTruckRequestService sharePoint)
    {
        _logger = logger;
        _sharePoint = sharePoint;
    }

    [Function("AzFuncHttpTruck")]
    public async Task<IActionResult> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post")] HttpRequest req,
        CancellationToken cancellationToken)
    {
        var form = await req.ReadFormAsync(cancellationToken);
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

        SharePointSubmissionResult submission;
        try
        {
            submission = await _sharePoint.CreateRequestAndUploadPhotosAsync(
                trailerNo,
                carrier,
                pickupLocation,
                existingDamage == "yes" ? "Yes" : "No",
                form["damaged"].ToString().Trim(),
                form["description"].ToString().Trim(),
                form.Files.GetFile("frontPhoto"),
                form.Files.GetFile("driverSidePhoto"),
                cancellationToken);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogError(exception, "Failed to save the truck request to SharePoint.");
            return new ObjectResult(new
            {
                message = "The submission could not be saved to SharePoint. Check the Function logs for details."
            })
            {
                StatusCode = StatusCodes.Status500InternalServerError
            };
        }

        _logger.LogInformation(
            "Created SharePoint truck request {ItemId} ({UniqueId}) for trailer {TrailerNo}.",
            submission.ItemId,
            submission.UniqueId,
            trailerNo);

        return new OkObjectResult(new
        {
            message = "Trailer condition form submitted successfully.",
            itemId = submission.ItemId,
            uniqueId = submission.UniqueId,
            trailerNo,
            carrier,
            pickupLocation,
            existingDamage = existingDamage == "yes" ? "Yes" : "No",
            damaged = form["damaged"].ToString(),
            description = form["description"].ToString(),
            photos = submission.PhotoUrls
        });
    }
}