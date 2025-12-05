using Microsoft.AspNetCore.Mvc;
namespace Application.Areas.AgencyArea
{
  public class ErrorController : Controller
  {

    [Area("AgencyArea")]
    [Route("Error/{statusCode:int}")]
    public async Task<IActionResult> HandleError(int statusCode)
    {
      // Ensure the response reflects the requested status code
      if (Response.StatusCode == StatusCodes.Status200OK)
      {
        Response.StatusCode = statusCode;
      }

      if (statusCode == StatusCodes.Status403Forbidden)
      {
        Response.StatusCode = StatusCodes.Status403Forbidden;
        return View("AccessDenied");
      }
      else if (statusCode == StatusCodes.Status404NotFound)
      {
        Response.StatusCode = StatusCodes.Status404NotFound;
        return View("NotFound");
      }
      else
      {
        // Default to 500 for unhandled errors
        Response.StatusCode = StatusCodes.Status500InternalServerError;
        return View("GenericError");
      }
    }
  }
}
