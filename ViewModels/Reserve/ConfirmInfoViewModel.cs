using Microsoft.AspNetCore.Mvc.RazorPages;
using System.ComponentModel.DataAnnotations;
using System.Runtime.InteropServices;

namespace Application.ViewModels.Reserve
{
  public class ConfirmInfoViewModel
  {
    [Required]
    public string TripCode { get; set; }
    [Required(ErrorMessage ="نام مسافر را وارد کنید")]
    public string Firstname { get; set; }
    [Required(ErrorMessage = "نام خانوادگی مسافر را وارد کنید")]
    public string Lastname { get; set; }
    [Required(ErrorMessage = "شماره تلفن مسافر را وارد کنید")]
    [RegularExpression(@"((0?9)|(\+?989))\d{9}", ErrorMessage = "شماره تلفن را صحیح وارد کنید")]
    public string Numberphone { get; set; }
    [Required(ErrorMessage ="شماره ملی مسافر را وارد کنید")]
    [RegularExpression(@"^\d{10}$", ErrorMessage = "کد ملی باید دقیقا ۱۰ رقم باشد")]
    public string Nacode { get; set; }
    [Required(ErrorMessage = "جنسیت مسافر را انتخاب کنید")]
    public string Gender { set; get; }
  }
}
