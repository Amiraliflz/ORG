using Application.Services.Auth;
using Application.ViewModels.Auth;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using System.Security.Policy;
using static System.Net.Mime.MediaTypeNames;

namespace Application.Areas.AgencyArea
{
  [Area("AgencyArea")]
  public class AuthController : Controller
  {

    private readonly SignInManager<IdentityUser> _signInManager;
    private readonly UserManager<IdentityUser> _usermanager;
    private readonly IOtpLogin _otpLogin;

    public AuthController(SignInManager<IdentityUser> signInManager, IOtpLogin otplogin, UserManager<IdentityUser> usermanager)
    {
      _otpLogin = otplogin;
      _signInManager = signInManager;
      _usermanager = usermanager;
    }

    [HttpGet]
    public IActionResult Login(string? ReturnUrl)
    {
      if (_signInManager.IsSignedIn(User))
      {
        // If already signed in and there's a return URL, redirect there
        if (!string.IsNullOrEmpty(ReturnUrl) && Url.IsLocalUrl(ReturnUrl))
        {
          return LocalRedirect(ReturnUrl);
        }
        // Otherwise redirect to home
        return RedirectToAction("Index", "Home");
      }

      // Pass ReturnUrl to the view via ViewBag
      ViewBag.ReturnUrl = ReturnUrl;
      return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginViewModel viewmodel, string? ReturnUrl)
    {
      if (ModelState.IsValid)
      {

        // Without password hash logic

        var user = await _usermanager.FindByNameAsync(viewmodel.Username);

        if(user == null)
        {
          ViewBag.errormessage = "نام کاربری یا رمز عبور اشتباه است";
          ViewBag.ReturnUrl = ReturnUrl;
          return View(viewmodel);
        }
       
        var result = await _signInManager.PasswordSignInAsync(user, viewmodel.Password, viewmodel.RemmemberMe,viewmodel.RemmemberMe);
        if (user != null && result.Succeeded)
        {
          // Redirect or take further action
          if (!string.IsNullOrEmpty(ReturnUrl) && Url.IsLocalUrl(ReturnUrl))
            return LocalRedirect(ReturnUrl);

          // Else : redirect to IndexPage
          return RedirectToAction("Index", "Home");

        }
        else
        {
          ViewBag.errormessage = "نام کاربری یا رمز عبور اشتباه است";
          ViewBag.ReturnUrl = ReturnUrl;
          return View(viewmodel);
        }

        //  var result = await _signInManager.PasswordSignInAsync(viewmodel.NumberPhone, viewmodel.Password, viewmodel.RemmemberMe, lockoutOnFailure: false);

        //  if (!_signInManager.UserManager.Users.Any(u => u.UserName == viewmodel.NumberPhone) || result.IsNotAllowed)
        //  {
        //    ViewBag.errormessage = "شماره تلفن یا رمز عبور اشتباه است";
        //    return View(viewmodel);
        //  }

        //  if (result.IsLockedOut)
        //  {
        //    ViewBag.errormessage = "حساب کاربری شما مسدود شده است";
        //    return View(viewmodel);
        //  }

        //  if (result.Succeeded)
        //  {
        //    if (!string.IsNullOrEmpty(ReturnUrl))
        //      return LocalRedirect(ReturnUrl);

        //    // Else : redirect to IndexPage
        //    return RedirectToAction("Index", "Home");

        //  }
        //  else
        //  {

        //    return View(viewmodel);
        //  }
        //}

      }
      ViewBag.ReturnUrl = ReturnUrl;
      return View(viewmodel);
    }

    [HttpGet]
    public async Task<IActionResult> Loginotp(string? ReturnUrl)
    {
      // Pass ReturnUrl to the view
      ViewBag.ReturnUrl = ReturnUrl;
      return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Loginotp(string numberphone, string? ReturnUrl)
    {

      // Check if the user is already authenticated
      if (_signInManager.IsSignedIn(User))
      {
        // If already signed in and there's a return URL, redirect there
        if (!string.IsNullOrEmpty(ReturnUrl) && Url.IsLocalUrl(ReturnUrl))
        {
          return LocalRedirect(ReturnUrl);
        }
        return RedirectToAction("Index", "Home");
      }


      if (string.IsNullOrEmpty(numberphone))
      {
        ViewBag.ReturnUrl = ReturnUrl;
        return RedirectToAction("Loginotp", new { ReturnUrl });
      }

      var user = await _usermanager.FindByNameAsync(numberphone);

      // User with entered numberphone does not exists
      if (user == null)
      {
        ViewBag.errormessage = "شماره وارد شده در سیستم ثبت نشده است";
        ViewBag.ReturnUrl = ReturnUrl;
        return View();
      }

      // Account is banned
      if (!await _signInManager.CanSignInAsync(user))
      {
        ViewBag.errormessage = "اجازه ورود به این حساب را ندارید";
        ViewBag.ReturnUrl = ReturnUrl;
        return View();
      }

      string otpcode = await _otpLogin.SendCode(numberphone);


      // Storing the code and exp_time in session values
      TempData["otp_code"] = otpcode;
      TempData["otp_exptime"] = DateTime.Now.AddSeconds(90).ToString();
      TempData["otp_returnurl"] = ReturnUrl; // Store ReturnUrl in TempData


      return RedirectToAction("LoginotpSubmit", new { numberphone, ReturnUrl });
    }

    [HttpGet]
    public IActionResult LoginotpSubmit(string numberphone, string? ReturnUrl)
    {
      if (string.IsNullOrEmpty(numberphone))
      {
        return RedirectToAction("Loginotp", new { ReturnUrl });
      }


      ViewBag.numberphone = numberphone;
      ViewBag.ReturnUrl = ReturnUrl;
      return View();
    }


    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> LoginotpSubmit(string code1, string code2, string code3, string code4, string code5, string numberphone, string? ReturnUrl)
    {
      if (_signInManager.IsSignedIn(User))
      {
        // If already signed in and there's a return URL, redirect there
        if (!string.IsNullOrEmpty(ReturnUrl) && Url.IsLocalUrl(ReturnUrl))
        {
          return LocalRedirect(ReturnUrl);
        }
        return RedirectToAction("Index", "Home");
      }

      string otpcode = code1 + code2 + code3 + code4 + code5;

      // Checking for the data existance
      if (TempData["otp_code"] == null || TempData["otp_exptime"] == null || numberphone is null)
      {
        return RedirectToAction("Loginotp", new { ReturnUrl });
      }

      string storedcode = TempData["otp_code"].ToString();
      DateTime exptime = Convert.ToDateTime(TempData["otp_exptime"].ToString());
      
      // Get ReturnUrl from TempData if not in parameter
      if (string.IsNullOrEmpty(ReturnUrl) && TempData["otp_returnurl"] != null)
      {
        ReturnUrl = TempData["otp_returnurl"].ToString();
      }


      var user = await _usermanager.FindByNameAsync(numberphone);

      if (user == null)
      {
        ViewBag.errormessage = "حساب کاربر وجود ندارد";

        ViewBag.numberphone = numberphone;
        ViewBag.ReturnUrl = ReturnUrl;

        return View("LoginotpSubmit");

      }

      var canlogin = await _signInManager.CanSignInAsync(user);



      // If otp expierd
      if (DateTime.Now > exptime)
      {
        TempData.Remove("otp-code");
        TempData.Remove("otp-exptime");
        return RedirectToAction("Loginotp", new { ReturnUrl });
      }


      if (otpcode != storedcode)
      {
        ViewBag.errormessage = "کد وارد شده نادرست است";

        ViewBag.numberphone = numberphone;
        ViewBag.ReturnUrl = ReturnUrl;
        TempData.Keep();

        return View("LoginotpSubmit");
      }

      // Logging in the user
      await _signInManager.SignInAsync(user, true, "OTP");
      
      // Redirect to ReturnUrl if provided and is local
      if (!string.IsNullOrEmpty(ReturnUrl) && Url.IsLocalUrl(ReturnUrl))
      {
        return LocalRedirect(ReturnUrl);
      }
      
      return RedirectToAction("Index", "Home");

    }

    public async Task<IActionResult> Logout()
    {
      await _signInManager.SignOutAsync();

      return RedirectToAction("Index", "Home");
    }
  }
}
