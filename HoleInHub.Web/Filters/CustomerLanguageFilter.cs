using HoleInHubReservationManagementSubsystem.Localization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace HoleInHubReservationManagementSubsystem.Filters;

// Translate framework/business errors at the customer presentation boundary, keeping domain data unchanged.
public sealed class CustomerLanguageFilter : IResultFilter
{
    public void OnResultExecuting(ResultExecutingContext context)
    {
        if (!CustomerText.Korean || context.RouteData.Values["controller"]?.ToString() is not ("Home" or "Account" or "AccountHub" or "Reservations" or "GuestReservations"))
        { return; }
        if (context.Controller is not Controller controller) { return; }
        foreach (var entry in controller.ModelState.Values)
        {
            var messages = entry.Errors.Select(error => CustomerText.Error(string.IsNullOrEmpty(error.ErrorMessage)
                ? "Check the entered value." : error.ErrorMessage)).ToArray();
            entry.Errors.Clear();
            foreach (var message in messages) { entry.Errors.Add(message); }
        }
        foreach (var key in new[] { "VerificationMessage", "RecoveryMessage", "AccountMessage" })
        {
            if (controller.ViewData[key] is string message) { controller.ViewData[key] = CustomerText.Error(message); }
        }
        if (context.Result is BadRequestObjectResult { Value: string error })
        { context.Result = new BadRequestObjectResult(CustomerText.Error(error)); }
    }
    public void OnResultExecuted(ResultExecutedContext context) { }
}
