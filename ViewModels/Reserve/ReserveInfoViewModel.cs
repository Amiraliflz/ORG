using Microsoft.AspNetCore.Mvc.RazorPages;
using System.ComponentModel.DataAnnotations;
using System.Runtime.InteropServices;

namespace Application.ViewModels.Reserve
{
  public class ReserveInfoViewModel
  {
    [Required(ErrorMessage ="نام مسافر را وارد کنید")]
    public string Firstname { get; set; }
    
    [Required(ErrorMessage = "نام خانوادگی مسافر را وارد کنید")]
    public string Lastname { get; set; }

    [Required(ErrorMessage = "جنسیت مسافر را انتخاب کنید")]
    public string Gender { get; set; }

    [Required(ErrorMessage = "کد ملی مسافر را وارد کنید")]
    [RegularExpression(@"^\d{10}$", ErrorMessage = "کد ملی باید دقیقاً 10 رقم باشد")]
    [StringLength(10, MinimumLength = 10, ErrorMessage = "کد ملی باید دقیقاً 10 رقم باشد")]
    public string NaCode { get; set; }

    [Required(ErrorMessage = "شماره تلفن مسافر را وارد کنید")]
    [RegularExpression(@"^((0?9)|(\+?989))\d{9}$", ErrorMessage = "شماره تلفن را صحیح وارد کنید")]
    public string NumebrPhone { get; set; }

    [EmailAddress(ErrorMessage = "لطفا یک ایمیل معتبر وارد کنید")]
    public string? Email { get; set; }

    [Required]
    public string TripCode { get; set; }
  }
}
