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
        return View("AccessDenied");
      }

      if (statusCode == 404)
      {
        Response.StatusCode = 404;
        return View("NotFound");
      }

      // 405 Method Not Allowed (e.g. HEAD before AcceptVerbs) and other codes —
      // avoid missing GenericError view which previously turned probes into 500.
      Response.StatusCode = statusCode;
      return Content($"Error {statusCode}", "text/plain");
    }
  }
}
