using System.Text.Json;
using HoleInHub.Business.Reservations;
using HoleInHub.Business.Services;
using HoleInHubReservationManagementSubsystem.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.WebUtilities;

namespace HoleInHubReservationManagementSubsystem.Filters;

public sealed class ServiceUnavailableFilter(ILogger<ServiceUnavailableFilter> logger, TimeProvider clock) : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var executed = await next();
        if (executed.Exception == null || executed.ExceptionHandled) { return; }
        if (context.HttpContext.RequestAborted.IsCancellationRequested) { return; }
        var failure = ServiceAvailability.Translate(executed.Exception);
        if (failure == null) { return; }
        var http = context.HttpContext; var request = http.Request;
        logger.LogWarning("Customer request unavailable ({FailureType}); correlation {CorrelationId}.", executed.Exception.GetType().Name, http.TraceIdentifier);
        http.Response.Headers.CacheControl = "no-store"; http.Response.Headers["Referrer-Policy"] = "no-referrer";
        if (context.RouteData.Values["action"]?.ToString() is "CheckAvailability" or "RentalQuote")
        {
            executed.Result = new ObjectResult(new { message = failure.Message, correlationId = http.TraceIdentifier }) { StatusCode = 503 };
        }
        else if (context.Controller is Controller controller)
        {
            var saved = false; var retry = request.Path.ToString() + request.QueryString;
            if (HttpMethods.IsPost(request.Method) && context.RouteData.Values["controller"]?.ToString() == "Reservations" && context.RouteData.Values["action"]?.ToString() == "Create")
            {
                var booking = new SubmitReservationRequest(); await controller.TryUpdateModelAsync(booking);
                if (booking.FirstName?.Length is not > 100 && booking.LastName?.Length is not > 100 && booking.Phone?.Length is not > 30 && booking.Email?.Length is not > 254 && booking.Notes?.Length is not > 1000 && booking.ResourceIds is { Length: <= 8 } && booking.ResourceIds.All(id => id?.Length is <= 100) && booking.Service?.Length is not > 30 && booking.IdempotencyKey?.Length is not > 100)
                {
                    booking.Participants = [];
                    controller.TempData["BookingRecovery"] = JsonSerializer.Serialize(new BookingRecoveryState(clock.GetUtcNow(), booking));
                    saved = true; retry = "/Reservations/Create?recover=true";
                }
            }
            else if (HttpMethods.IsPost(request.Method) && request.HasFormContentType)
            {
                var values = await request.ReadFormAsync(http.RequestAborted); var destination = values["ReturnUrl"].ToString();
                if (context.RouteData.Values["controller"]?.ToString() == "Account" && context.RouteData.Values["action"]?.ToString() == "GoogleComplete") { retry = "/account/google-callback"; }
                if (controller.Url.IsLocalUrl(destination) && destination.Length <= 2048) { retry = QueryHelpers.AddQueryString(retry, "returnUrl", destination); }
                if (context.RouteData.Values["controller"]?.ToString() == "Account" && context.RouteData.Values["action"]?.ToString() == "ResetPassword")
                {
                    var userId = values["UserId"].ToString(); var token = values["Token"].ToString();
                    if (userId.Length <= 64 && token.Length <= 4096)
                    {
                        retry = QueryHelpers.AddQueryString(retry, new Dictionary<string, string?> { { "userId", userId }, { "token", token } });
                    }
                }
            }
            controller.ViewData.Model = new ServiceUnavailableViewModel(failure.Message, retry, saved, http.TraceIdentifier);
            executed.Result = new ViewResult { ViewName = "~/Views/Shared/ServiceUnavailable.cshtml", ViewData = controller.ViewData, TempData = controller.TempData, StatusCode = 503 };
        }
        else { executed.Result = new StatusCodeResult(503); }
        executed.ExceptionHandled = true;
    }
}
