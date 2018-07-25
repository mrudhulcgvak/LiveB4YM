using System;
using System.IO;
using System.Linq;
using System.Web.Mvc;
using Better4You.Meal.Config;
using Better4You.Meal.Service;
using Better4You.Meal.Service.Messages;
using Better4You.Meal.ViewModel;
using Tar.Service.Messages;
using System.Collections.Generic;
using System.Globalization;
using Better4You.Core;
using Better4You.UserManagment.Service;
using Better4You.UserManagment.Service.Messages;
using Tar.Core.Configuration;
using Better4You.UI.Mvc.Helpers;
using Better4You.UI.Mvc.Models;

namespace Better4You.UI.Mvc.Controllers
{
    public class ReportsController : ControllerBase
    {
        public IReportService ReportService
        {
            get { return ServiceLocator.Get<IReportService>(); }
        }
        //
        // GET: /Reports/

        public ActionResult Index()
        {
            var mealTypeId = (int)MealTypes.Breakfast;
            if (!string.IsNullOrWhiteSpace(Request["MealTypeId"]))
                mealTypeId = Int32.Parse(Request["MealTypeId"]);


            var mealTypes = Lookups.GetItems<MealTypes>().Where(d => d.Id > 0).Select(d => new SelectListItem
            {
                Value = d.Id.ToString(),
                Text = d.Text,
                Selected = d.Id == mealTypeId
            }).ToList();

            ViewBag.MealTypes = mealTypes;
            ViewBag.Year = Year;
            ViewBag.Month = Month;

            return View();
        }

        public ActionResult MilkExport()
        {
            var orderStartDate = DateTime.Now;

            if (Request["OrderStartDate"] != null && !string.IsNullOrWhiteSpace(Request["OrderStartDate"]))
                orderStartDate = DateTime.Parse(Request["OrderStartDate"]);

            var mealTypeId = MealTypes.Breakfast;
            if (!string.IsNullOrWhiteSpace(Request["MealTypeId"]))
                Enum.TryParse(Request["MealTypeId"], true, out mealTypeId);

            orderStartDate = new DateTime(orderStartDate.Year, orderStartDate.Month, 1);
            var orderEndDate = new DateTime(orderStartDate.Year, orderStartDate.Month, DateTime.DaysInMonth(orderStartDate.Year, orderStartDate.Month));

            var response = ReportService.MontlyMilkExport(new MontlyMilkExportRequest
            {
                Filter = new OrderReportFilterView
                {
                    OrderStartDate = orderStartDate,
                    OrderEndDate = orderEndDate,
                    MealTypeId = (int)mealTypeId,
                }
            });
            if (response.Result == Result.Success)
            {
                var startIndex = response.FileName.LastIndexOf(response.FileName.IndexOf('\\') > -1 ? '\\' : '/');

                return new FileStreamResult(new FileStream(response.FileName, FileMode.Open), "application/vnd.ms-excel")
                {
                    FileDownloadName = response.FileName.Substring(startIndex + 1)//string.Format("{0}_{1}_Milk.xlsx", orderStartDate.ToString("yyyy-MMM"), mealTypeId.ToString("G"))

                };
            }
            ErrorMessage = response.Message + " Couldn't generate monthly milk export file";
            return null;
        }

        //public ActionResult LunchExport()
        //{
        //    var orderStartDate = DateTime.Now;

        //    if (Request["OrderStartDate"] != null && !string.IsNullOrWhiteSpace(Request["OrderStartDate"]))
        //        orderStartDate = DateTime.Parse(Request["OrderStartDate"]);

        //    orderStartDate = new DateTime(orderStartDate.Year, orderStartDate.Month, 1);
        //    var orderEndDate = new DateTime(orderStartDate.Year, orderStartDate.Month, DateTime.DaysInMonth(orderStartDate.Year, orderStartDate.Month));

        //    var response = ReportService.DaylunchExport(new MontlyMilkExportRequest
        //    {
        //        Filter = new OrderReportFilterView
        //        {
        //            OrderStartDate = orderStartDate,
        //            OrderEndDate = orderEndDate,
        //        }
        //    });
        //    if (response.Result == Result.Success)
        //    {
        //        var startIndex = response.FileName.LastIndexOf(response.FileName.IndexOf('\\') > -1 ? '\\' : '/');

        //        return new FileStreamResult(new FileStream(response.FileName, FileMode.Open), "application/vnd.ms-excel")
        //        {
        //            FileDownloadName = response.FileName.Substring(startIndex + 1)//string.Format("{0}_{1}_Milk.xlsx", orderStartDate.ToString("yyyy-MMM"), mealTypeId.ToString("G"))

        //        };
        //    }
        //    ErrorMessage = response.Message + " Couldn't generate monthly milk export file";
        //    return null;
        //}


        public ActionResult FruitVegExport()
        {
            var orderDate = new DateTime(DateTime.Now.Year, DateTime.Now.Month, 1);
            if (Request["OrderMonth"] != null && Request["OrderYear"] != null)
                orderDate = new DateTime(Int32.Parse(Request["OrderYear"]), Int32.Parse(Request["OrderMonth"]), 1);

            var response = ReportService.OrderDayPropItemExport(new OrderDayPropItemRequest
            {
                Filter = new MealMenuOrderFilterView
                {
                    OrderDate = orderDate
                }
            });

            if (response.Result == Result.Success)
            {
                var startIndex = response.FileName.LastIndexOf(response.FileName.IndexOf('\\') > -1 ? '\\' : '/');

                return new FileStreamResult(new FileStream(response.FileName, FileMode.Open), "application/vnd.ms-excel")
                {
                    FileDownloadName = response.FileName.Substring(startIndex + 1)//string.Format("{0}_{1}_Milk.xlsx", orderStartDate.ToString("yyyy-MMM"), mealTypeId.ToString("G"))

                };
            }
            ErrorMessage = response.Message + " Couldn't generate Daily Item Props export file";
            return null;

        }

        public ActionResult DailyLunchNoOrderExport()
        {
            var orderDate = Convert.ToDateTime(Request.QueryString["OrderLunchDate"]);
            if (Request["OrderMonth"] != null && Request["OrderYear"] != null)
                orderDate = new DateTime(Int32.Parse(Request["OrderYear"]), Int32.Parse(Request["OrderMonth"]), 1);

            var response = ReportService.DailyLunchRouteReport(new OrderDayPropItemRequest
            {
                Filter = new MealMenuOrderFilterView
                {
                    OrderDate = orderDate
                }
            });
            //var path = AppDomain.CurrentDomain.BaseDirectory;
            //var filePath = Path.Combine(path, "Templates\\DAILYLUNCHSNACKSUPPERREPORT.xlsx");
            //response.FileName = filePath;

            if (response.Result == Result.Success)
            {
                var startIndex = response.FileName.LastIndexOf(response.FileName.IndexOf('\\') > -1 ? '\\' : '/');

                return new FileStreamResult(new FileStream(response.FileName, FileMode.Open), "application/vnd.ms-excel")
                {
                    FileDownloadName = response.FileName.Substring(startIndex + 1)//string.Format("{0}_{1}_Milk.xlsx", orderStartDate.ToString("yyyy-MMM"), mealTypeId.ToString("G"))

                };
            }
            ErrorMessage = response.Message + " Couldn't generate Daily Lunch Export file";
            return null;

        }

        public ActionResult DailyBreakfastNoOrderExport()
        {
            var orderDate = Convert.ToDateTime(Request.QueryString["OrderBreakfastDate"]);
            if (Request["OrderMonth"] != null && Request["OrderYear"] != null)
                orderDate = new DateTime(Int32.Parse(Request["OrderYear"]), Int32.Parse(Request["OrderMonth"]), 1);

            var response = ReportService.DailyBreakfastRouteReport(new OrderDayPropItemRequest
            {
                Filter = new MealMenuOrderFilterView
                {
                    OrderDate = orderDate
                }
            });
            //var path = AppDomain.CurrentDomain.BaseDirectory;
            //var filePath = Path.Combine(path, "Templates\\DAILYLUNCHSNACKSUPPERREPORT.xlsx");
            //response.FileName = filePath;

            if (response.Result == Result.Success)
            {
                var startIndex = response.FileName.LastIndexOf(response.FileName.IndexOf('\\') > -1 ? '\\' : '/');

                return new FileStreamResult(new FileStream(response.FileName, FileMode.Open), "application/vnd.ms-excel")
                {
                    FileDownloadName = response.FileName.Substring(startIndex + 1)//string.Format("{0}_{1}_Milk.xlsx", orderStartDate.ToString("yyyy-MMM"), mealTypeId.ToString("G"))

                };
            }
            ErrorMessage = response.Message + " Couldn't generate Daily Lunch Export file";
            return null;

        }
        public ActionResult DailyLunchExport()
        {
            var orderDate = Convert.ToDateTime(Request.QueryString["OrderLunchDate"]);
            if (Request["OrderMonth"] != null && Request["OrderYear"] != null)
                orderDate = new DateTime(Int32.Parse(Request["OrderYear"]), Int32.Parse(Request["OrderMonth"]), 1);

            var response = ReportService.DailyLunchReport(new OrderDayPropItemRequest
            {
                Filter = new MealMenuOrderFilterView
                {
                    OrderDate = orderDate
                }
            });
            //var path = AppDomain.CurrentDomain.BaseDirectory;
            //var filePath = Path.Combine(path, "Templates\\DAILYLUNCHSNACKSUPPERREPORT.xlsx");
            //response.FileName = filePath;

            if (response.Result == Result.Success)
            {
                var startIndex = response.FileName.LastIndexOf(response.FileName.IndexOf('\\') > -1 ? '\\' : '/');

                return new FileStreamResult(new FileStream(response.FileName, FileMode.Open), "application/vnd.ms-excel")
                {
                    FileDownloadName = response.FileName.Substring(startIndex + 1)//string.Format("{0}_{1}_Milk.xlsx", orderStartDate.ToString("yyyy-MMM"), mealTypeId.ToString("G"))

                };
            }
            ErrorMessage = response.Message + " Couldn't generate Daily Lunch Export file";
            return null;

        }
        public ActionResult DailySupperExport()
        {
            var orderDate = Convert.ToDateTime(Request.QueryString["OrderSupperDate"]);
            if (Request["OrderMonth"] != null && Request["OrderYear"] != null)
                orderDate = new DateTime(Int32.Parse(Request["OrderYear"]), Int32.Parse(Request["OrderMonth"]), 1);

            var response = ReportService.DailySupperReport(new OrderDayPropItemRequest
            {
                Filter = new MealMenuOrderFilterView
                {
                    OrderDate = orderDate
                }
            });
            //var path = AppDomain.CurrentDomain.BaseDirectory;
            //var filePath = Path.Combine(path, "Templates\\DAILYLUNCHSNACKSUPPERREPORT.xlsx");
            //response.FileName = filePath;

            if (response.Result == Result.Success)
            {
                var startIndex = response.FileName.LastIndexOf(response.FileName.IndexOf('\\') > -1 ? '\\' : '/');

                return new FileStreamResult(new FileStream(response.FileName, FileMode.Open), "application/vnd.ms-excel")
                {
                    FileDownloadName = response.FileName.Substring(startIndex + 1)//string.Format("{0}_{1}_Milk.xlsx", orderStartDate.ToString("yyyy-MMM"), mealTypeId.ToString("G"))

                };
            }
            ErrorMessage = response.Message + " Couldn't generate Daily Lunch Export file";
            return null;

        }
        public ActionResult DailyBreakfastExport()
        {
            var orderDate = Convert.ToDateTime(Request.QueryString["OrderBreakfastDate"]);
            //var orderDate = new DateTime(DateTime.Now.Year, DateTime.Now.Month, 1);
            if (Request["OrderMonth"] != null && Request["OrderYear"] != null)
                orderDate = new DateTime(Int32.Parse(Request["OrderYear"]), Int32.Parse(Request["OrderMonth"]), 1);

            var response = ReportService.DailyBreakfastReport(new OrderDayPropItemRequest
            {
                Filter = new MealMenuOrderFilterView
                {
                    OrderDate = orderDate
                }
            });
            //var path = AppDomain.CurrentDomain.BaseDirectory;
            //var filePath = Path.Combine(path, "Templates\\DAILYLUNCHSNACKSUPPERREPORT.xlsx");
            //response.FileName = filePath;

            if (response.Result == Result.Success)
            {
                var startIndex = response.FileName.LastIndexOf(response.FileName.IndexOf('\\') > -1 ? '\\' : '/');

                return new FileStreamResult(new FileStream(response.FileName, FileMode.Open), "application/vnd.ms-excel")
                {
                    FileDownloadName = response.FileName.Substring(startIndex + 1)//string.Format("{0}_{1}_Milk.xlsx", orderStartDate.ToString("yyyy-MMM"), mealTypeId.ToString("G"))

                };
            }
            ErrorMessage = response.Message + " Couldn't generate Daily Breakfast Export file";
            return null;

        }
        public ActionResult MonthlyPurchacseExport()
        {
            var orderDate = Convert.ToDateTime(Request.QueryString["OrderPurchacseDate"]);
            //var orderDate = new DateTime(DateTime.Now.Year, DateTime.Now.Month, 1);
            if (Request["OrderMonth"] != null && Request["OrderYear"] != null)
                orderDate = new DateTime(Int32.Parse(Request["OrderYear"]), Int32.Parse(Request["OrderMonth"]), 1);

            var response = ReportService.MonthlyPurchacseReport(new OrderDayPropItemRequest
            {
                Filter = new MealMenuOrderFilterView
                {
                    OrderDate = orderDate
                }
            });
            if (response.Result == Result.Success)
            {
                var startIndex = response.FileName.LastIndexOf(response.FileName.IndexOf('\\') > -1 ? '\\' : '/');

                return new FileStreamResult(new FileStream(response.FileName, FileMode.Open), "application/vnd.ms-excel")
                {
                    FileDownloadName = response.FileName.Substring(startIndex + 1)//string.Format("{0}_{1}_Milk.xlsx", orderStartDate.ToString("yyyy-MMM"), mealTypeId.ToString("G"))

                };
            }
            ErrorMessage = response.Message + " Couldn't generate Monthly Purchacse Export file";
            return null;
        }
    }
}
