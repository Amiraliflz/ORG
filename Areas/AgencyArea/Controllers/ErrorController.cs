using Microsoft.AspNetCore.Mvc;
namespace Application.Areas.AgencyArea
{
  public class ErrorController : Controller
  {

    [Area("AgencyArea")]
    [Route("Error/{statusCode}")]
    public async Task<IActionResult> HandleError(int statusCode)
    {


      if (Response.StatusCode == StatusCodes.Status200OK)
      {
        Response.StatusCode = statusCode;
      }

      if (statusCode == 403)
      {
        Response.StatusCode = 403;
        return View("AccessDenied"); // Assuming the view name is "AccessDenied"
      }
      else if (statusCode == 404)
      {
        Response.StatusCode = 404;
        return View("NotFound"); // Assuming the view name is "NotFound"
      }
      else
      {
        // Handle other error scenarios or return a generic error view
        return View("GenericError"); // Assuming the view name is "GenericError"
      }
    }
  }
}
