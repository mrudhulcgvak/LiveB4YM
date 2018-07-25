using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.ServiceModel.Activation;
using System.Text;
using Better4You.Meal.Business;
using Better4You.Meal.Config;
using Better4You.Meal.Service.Messages;
using Better4You.Meal.ViewModel;
using ClosedXML.Excel;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using Tar.Core.Compression;
using Tar.Core.Configuration;
using Tar.Service;
using Tar.Service.Messages;
using Better4You.Core;

namespace Better4You.Meal.Service.Implementation
{
    [AspNetCompatibilityRequirements(RequirementsMode = AspNetCompatibilityRequirementsMode.Allowed)]
    public class ReportService : Service<IReportService, ReportService>, IReportService
    {
        private readonly IMealMenuOrderFacade _mealMenuOrderFacade;
        private readonly IMenuFacade _menuFacade;
        private readonly IApplicationSettings _appSetting;
        private readonly IMealMenuOrderService _mealMenuOrderService;

        public ReportService(IMealMenuOrderFacade mealMenuOrderFacade, IApplicationSettings appSetting,
            IMealMenuOrderService mealMenuOrderService, IMenuFacade menuFacade)
        {
            if (mealMenuOrderFacade == null) throw new ArgumentNullException("mealMenuOrderFacade");
            if (menuFacade == null) throw new ArgumentNullException("menuFacade");
            if (appSetting == null) throw new ArgumentNullException("appSetting");
            if (mealMenuOrderService == null) throw new ArgumentNullException("mealMenuOrderService");
            _mealMenuOrderFacade = mealMenuOrderFacade;
            _menuFacade = menuFacade;
            _appSetting = appSetting;
            _mealMenuOrderService = mealMenuOrderService;
        }

        private string FileRepositoryPath
        {
            get
            {
                return Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
                    _appSetting.GetSetting<string>("FileRepositoryPath"));
            }
        }

        public ReportResponse MonthlyExport(MonthlyExportRequest request)
        {
            return Execute<MonthlyExportRequest, ReportResponse>(
                request,
                response =>
                {
                    int totalCount;
                    if (!request.Filter.OrderStartDate.HasValue)
                        throw new Exception("Order StartDate Is Null");
                    var orderStartDate = request.Filter.OrderStartDate.Value;
                    var eMealType = (MealTypes)request.Filter.MealTypeId;

                    //Dictionary<long, MealOrderManageView> sackLunches = new Dictionary<long, MealOrderManageView>();

                    var orders = _mealMenuOrderFacade.GetOrderReport(request.Filter, int.MaxValue, 1, "Route", true,
                        out totalCount);

                    var fileRepository = FileRepositoryPath;//_appSetting.GetSetting<string>("FileRepositoryPath");
                    var templateFile = Path.Combine(fileRepository, "Templates", "MonthlyOrder.xlsx");
                    var zipFolder = Path.Combine(fileRepository, "MonthlyOrder",
                        string.Format("{0}_{1}", orderStartDate.ToString("yyyy-MMM"), eMealType.ToString("G")));
                    var zipFile = Path.Combine(fileRepository, "MonthlyOrder",
                        string.Format("{0}_{1}.zip", orderStartDate.ToString("yyyy-MMM"), eMealType.ToString("G")));
                    if (Directory.Exists(zipFolder))
                        Directory.Delete(zipFolder, true);
                    Directory.CreateDirectory(zipFolder);


                    var orderMenuTypes =
                        orders.SelectMany(o => o.Items.SelectMany(i => i.Menus.Where(k => k.MenuTypeId != (int)MenuTypes.Milk).Select(m => m.MenuTypeId)))
                            .GroupBy(d => d)
                            .Select(d => d.Key)
                            .ToList();


                    orderMenuTypes.ForEach(menuType =>
                    {
                        var eMenuType = (MenuTypes)menuType;
                        var menuTypeText = eMenuType.ToString("G");

                        var lMenuType = Lookups.MenuTypeList.FirstOrDefault(lm => lm.Id == menuType);
                        if (lMenuType != null)
                            menuTypeText = lMenuType.Text;
                        //var filePath = Path.Combine(zipFolder, string.Format("{0}.xlsx", eMenuType.ToString("G")));
                        var filePath = Path.Combine(zipFolder, string.Format("{0}.xlsx", menuTypeText));
                        File.Copy(templateFile, filePath, false);
                        File.SetAttributes(filePath, FileAttributes.Normal | FileAttributes.Archive);
                        using (var workBook = new XLWorkbook(filePath, XLEventTracking.Disabled))
                        {
                            /*
                            var headerTitle = string.Format("{2} {0} Order -  {1}  Menu Type", eMealType.ToString("G"),
                                eMenuType.ToString("G"), orderStartDate.ToString("yyyy-MMM"));
                            */
                            var headerTitle = string.Format("{2} {0} Order -  {1}  Menu Type", eMealType.ToString("G"),
                                menuTypeText, orderStartDate.ToString("yyyy-MMM"));

                            var orderList =
                                orders.Where(o => o.Items.Any(i => i.Menus.Any(m => m.MenuTypeId == menuType)))
                                    .OrderBy(d => d.SchoolRoute)
                                    .ThenBy(d => d.SchoolName)
                                    .ToList();

                            var workSheet = workBook.Worksheets.First();

                            var lastDayOfMonth = DateTime.DaysInMonth(orderStartDate.Year, orderStartDate.Month);

                            var rowStart = 6;
                            var columnStart = 6;

                            for (var day = orderStartDate.Day; day <= lastDayOfMonth; day++)
                            {
                                var currentDay = new DateTime(orderStartDate.Year, orderStartDate.Month, day);
                                if (currentDay.DayOfWeek == DayOfWeek.Saturday ||
                                    currentDay.DayOfWeek == DayOfWeek.Sunday)
                                    continue;

                                workSheet.Cell(rowStart, columnStart)
                                    .SetValue(currentDay.DayOfWeek.ToString().Substring(0, 2));
                                workSheet.Cell(rowStart + 1, columnStart).SetValue(day.ToString());
                                columnStart++;
                            }

                            workSheet.Range(rowStart, columnStart, rowStart + 1, columnStart)
                                .Merge()
                                .SetValue("Notes")
                                .Style.Alignment.SetVertical(XLAlignmentVerticalValues.Center);

                            var tableHeaderRange =
                                workSheet.Range(rowStart, 6, rowStart + 1, columnStart).AddToNamed("tableHeader");
                            tableHeaderRange.Style.Fill.BackgroundColor = XLColor.FromArgb(218, 238, 243);
                            tableHeaderRange.Style.Border.InsideBorderColor = XLColor.FromArgb(0, 0, 221);
                            tableHeaderRange.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
                            tableHeaderRange.Style.Border.OutsideBorderColor = XLColor.FromArgb(0, 0, 139);
                            tableHeaderRange.Style.Border.OutsideBorder = XLBorderStyleValues.Medium;

                            var headerTitleRange =
                                workSheet.Range(2, 6, 3, columnStart).Merge().AddToNamed("headerTitle");
                            headerTitleRange.SetValue(headerTitle);
                            headerTitleRange.Style.Fill.BackgroundColor = XLColor.FromArgb(67, 255, 152);
                            headerTitleRange.Style.Font.SetFontSize(16);
                            headerTitleRange.Style.Font.SetBold(true);
                            headerTitleRange.Style.Font.SetItalic(true);
                            headerTitleRange.Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);
                            headerTitleRange.Style.Alignment.SetVertical(XLAlignmentVerticalValues.Center);



                            var orderRowStart = 8;
                            var orderColumnStart = 1;

                            var summaryTotal = new Dictionary<DateTime, long>();

                            orderList.ForEach(o =>
                            {
                                var schoolRoute = o.SchoolRoute;
                                workSheet.Cell(orderRowStart, orderColumnStart).SetValue(schoolRoute);


                                workSheet.Cell(orderRowStart, orderColumnStart + 1).SetValue(o.FoodServiceType);
                                if (request.Filter.MealTypeId == 1)
                                {
                                    workSheet.Cell(orderRowStart, orderColumnStart + 2).SetValue(o.BrakfastOVSType);
                                }
                                else
                                {
                                    workSheet.Cell(orderRowStart, orderColumnStart + 2).SetValue(o.LunchOVSType);
                                }
                                workSheet.Cell(orderRowStart, orderColumnStart + 3).SetValue(o.SchoolType);
                                workSheet.Cell(orderRowStart, orderColumnStart + 4).SetValue(o.SchoolName);
                                var mealNames = "";
                                if (eMenuType == MenuTypes.Special)
                                {
                                    mealNames = string.Join(Environment.NewLine,
                                        o.Items.SelectMany(l => l.Menus.Select(m => m))
                                            .Where(m => m.MenuTypeId == menuType)
                                            .GroupBy(m => m.Name)
                                            .Select(m => m.Key));
                                }
                                /*
                                if(eMenuType==MenuTypes.SackLunch)
                                {
                                    MealOrderManageView sackLunchView = null;
                                    if (sackLunches.ContainsKey(o.SchoolId))
                                    {
                                        sackLunchView = sackLunches[o.SchoolId];
                                    }
                                    else
                                    {
                                        sackLunchView = GetSackLunches(o.SchoolId, o.OrderDate);
                                        sackLunches.Add(o.SchoolId, sackLunchView);
                                    }
                                }
                                */
                                workSheet.Cell(orderRowStart, orderColumnStart + 4)
                                    .SetValue(string.IsNullOrWhiteSpace(mealNames)
                                        ? o.SchoolName
                                        : string.Format("{0}{1}{2}", o.SchoolName, Environment.NewLine, mealNames));

                                columnStart = 6;
                                for (var day = orderStartDate.Day; day <= lastDayOfMonth; day++)
                                {
                                    var currentDay = new DateTime(orderStartDate.Year, orderStartDate.Month, day);
                                    if (currentDay.DayOfWeek == DayOfWeek.Saturday ||
                                        currentDay.DayOfWeek == DayOfWeek.Sunday)
                                        continue;
                                    var currentDayInfo = o.Items.FirstOrDefault(d => Equals(d.Date, currentDay));

                                    if (!summaryTotal.ContainsKey(currentDay))
                                        summaryTotal.Add(currentDay, 0);

                                    if (currentDayInfo != null)
                                    {
                                        var currentDayMenus =
                                            currentDayInfo.Menus.Where(m => m.MenuTypeId == menuType).ToList();
                                        var sumCount = currentDayMenus.Sum(m => m.TotalCount);
                                        workSheet.Cell(orderRowStart, columnStart).SetValue(sumCount);
                                        summaryTotal[currentDay] += sumCount;
                                        if (currentDayMenus.Any(m => m.RefId.HasValue && m.RefId.Value > 0))
                                            workSheet.Cell(orderRowStart, columnStart).Style.Fill.BackgroundColor =
                                                XLColor.PowderBlue;

                                        if (eMenuType == MenuTypes.SackLunch1 && currentDayInfo.DeliveryType == (long)DeliveryTypes.Breakfast)

                                            workSheet.Cell(orderRowStart, columnStart).Style.Fill.BackgroundColor = XLColor.AppleGreen;
                                    }
                                    columnStart++;
                                }

                                orderRowStart++;
                            });
                            //workSheet.Range(8, 1, orderRowStart, columnStart).AddToNamed("tableContent");

                            var tableContent =
                                workSheet.Range(8, 1, orderRowStart, columnStart).AddToNamed("tableContent");
                            tableContent.Style.Border.InsideBorderColor = XLColor.FromArgb(0, 0, 221);
                            tableContent.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
                            tableContent.Style.Border.OutsideBorderColor = XLColor.FromArgb(0, 0, 139);
                            tableContent.Style.Border.OutsideBorder = XLBorderStyleValues.Medium;


                            workSheet.Range(6, 1, 6, 4)
                                .Merge()
                                .SetValue(string.Format("Report Date : {0:G}", DateTime.Now));

                            var dailyTotal =
                                workSheet.Range(orderRowStart, 1, orderRowStart, 5).Merge().AddToNamed("dailyTotal");
                            dailyTotal.SetValue("Daily Total");
                            dailyTotal.Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Right);

                            columnStart = 6;
                            summaryTotal.OrderBy(s => s.Key).ForEach(s =>
                            {
                                workSheet.Cell(orderRowStart, columnStart).SetValue(s.Value);
                                columnStart++;
                            });
                            var footerRow =
                                workSheet.Range(orderRowStart, 1, orderRowStart, columnStart).AddToNamed("footerRow");
                            footerRow.Style.Font.SetBold(true);
                            footerRow.Style.Font.SetItalic(true);
                            footerRow.Style.Border.InsideBorderColor = XLColor.FromArgb(0, 0, 221);
                            footerRow.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
                            footerRow.Style.Border.OutsideBorderColor = XLColor.FromArgb(0, 0, 139);
                            footerRow.Style.Border.OutsideBorder = XLBorderStyleValues.Medium;


                            workSheet.PageSetup.PageOrientation = XLPageOrientation.Landscape;
                            workSheet.PageSetup.PagesWide = 1;
                            workSheet.Columns().AdjustToContents();

                            workBook.Save();
                        }

                    });
                    using (var stream = new FileStream(zipFile, FileMode.Create))
                    {
                        ZipComponentFactory.CreateZipComponent().Zip(stream, zipFolder, true);
                        Directory.Delete(zipFolder, true);
                    }
                    response.FileName = zipFile;
                });
        }

        public ReportResponse DailySupperReport(OrderDayPropItemRequest request)
        {
            return Execute<OrderDayPropItemRequest, ReportResponse>(
                request,
                response =>
                {
                    var orderDayDate = Convert.ToDateTime(request.Filter.OrderDate);
                    var orderItems = _mealMenuOrderFacade.GetDailySupperReport(request.Filter);
                    var path = AppDomain.CurrentDomain.BaseDirectory;
                    var filePath = Path.Combine(path, "Templates\\DAILYSUPPERREPORT.xlsx");
                    var workBook = new XLWorkbook(filePath);
                    var sheet = workBook.Worksheet(1);
                    var schoolListNames = orderItems.GroupBy(d => new { d.SchoolName, d.Route, d.ServiceType, d.LunchOVS, d.Type }).Select(d => new
                    {
                        d.Key.SchoolName,
                        d.Key.Route,
                        d.Key.ServiceType,
                        d.Key.LunchOVS,
                        d.Key.Type
                    }).Distinct().ToList();

                    var rowIndex = 8;
                    var counter = 0;
                    var OrderDate = Convert.ToDateTime(orderDayDate.ToShortDateString());
                    var orderDay = OrderDate.Day;
                    var orderMonth = OrderDate.ToString("MMM");
                    var orderYear = OrderDate.ToString("yy");
                    var reportDate = DateTime.Now;
                    var Lunchday = OrderDate.ToShortDateString();
                    sheet.Cell(5, 1).SetValue("Report Date : " + reportDate);
                    sheet.Cell(3, 5).SetValue(orderDay + "-" + orderMonth + "-" + orderYear);
                    sheet.Cell(5, 1).SetValue("Report Date : " + reportDate);
                    sheet.Cell(5, 5).SetValue(orderMonth + " " + Lunchday + " SUPPER");



                    //var q = orderRoute.Where(a => !orderItems.Select(b => b.SchoolName).Contains(a.SchoolName));



                    schoolListNames.ForEach(m =>
                    {
                        var SchoolName = m.SchoolName;
                        sheet.Cell(rowIndex + counter, 5).SetValue(SchoolName);
                        sheet.Cell(rowIndex + counter, 1).SetValue(Convert.ToInt32(m.Route));
                        sheet.Cell(rowIndex + counter, 2).SetValue(m.ServiceType);
                        sheet.Cell(rowIndex + counter, 3).SetValue(m.LunchOVS);
                        sheet.Cell(rowIndex + counter, 4).SetValue(m.Type);
                        sheet.Cell(rowIndex + counter, 7).SetValue(0);
                        sheet.Cell(rowIndex + counter, 6).SetValue(0);
                        counter++;
                    });

                    var menuItem = orderItems.GroupBy(d => new { d.id, d.FruitCount, d.MealType, d.VegetableCount, d.SchoolName, d.TotalCount, d.MenuName, d.validate, d.menuType }).Select(d => new
                    {
                        menuId = d.Key.id,
                        menuName = d.Key.MenuName,
                        totalCount = d.Key.TotalCount,
                        fruitCount = d.Key.FruitCount,
                        vegCount = d.Key.VegetableCount,
                        validdate = d.Key.validate,
                        menuTypeId = d.Key.menuType,
                        SchoolName = d.Key.SchoolName,
                        mealType = d.Key.MealType
                    }).ToList();

                    for (var day = orderDayDate.Day; day <= orderDayDate.Day; day++)
                    {
                        var currentDay = new DateTime(orderDayDate.Year, orderDayDate.Month, day);
                        if (currentDay.DayOfWeek == DayOfWeek.Saturday ||
                            currentDay.DayOfWeek == DayOfWeek.Sunday)
                            continue;

                        var SupperSchoolNames = orderItems.Where(m => m.validate == currentDay && m.MealType.Id == 5).Select(d => new { d.SchoolName, d.Route, d.Type, d.ServiceType, d.LunchOVS }).Distinct().ToList();
                        var Supper1 = menuItem.Where(m => m.validdate == currentDay && m.menuTypeId == (int)MenuTypes.Supper1).Select(d => new { d.totalCount, d.SchoolName }).ToList();
                        var Supper2 = menuItem.Where(m => m.validdate == currentDay && m.menuTypeId == (int)MenuTypes.Vegetarian).Select(d => new { d.totalCount, d.SchoolName }).ToList();
                        var SupperCount2 = menuItem.Where(m => m.validdate == currentDay && m.menuTypeId == (int)MenuTypes.Supper2).Select(d => new { d.totalCount, d.SchoolName }).ToList();
                        var Supper2Option = 8;
                        var startSupper = 8;
                        SupperSchoolNames.ForEach(sl =>
                        {
                            Supper1.ForEach(sn =>
                            {
                                if (sl.SchoolName == sn.SchoolName)
                                {
                                    sheet.Cell(startSupper, 6).SetValue(sn.totalCount);
                                }
                            });
                            startSupper++;
                        });


                        SupperSchoolNames.ForEach(sl =>
                        {
                            Supper2.ForEach(sn =>
                            {
                                if (sl.SchoolName == sn.SchoolName)
                                {
                                    sheet.Cell(Supper2Option, 7).SetValue(sn.totalCount);
                                }
                            });
                            Supper2Option++;
                        });

                        List<int> supperlist1 = new List<int>();
                        var supper12 = 8;
                        for (int i = 1; i <= SupperSchoolNames.Count; i++)
                        {
                            var count = Convert.ToInt32(sheet.Cell(supper12, 6).Value);
                            supperlist1.Add(count);
                            supper12++;
                        }
                        var supperSum = supperlist1.Sum(s => s);
                        var supperSum1 = sheet.Cell(startSupper, 6).SetValue(supperSum);
                        supperSum1.Style.Font.SetBold(true);
                        supperSum1.Style.Font.SetFontSize(12.5);
                        supperSum1.Style.Border.OutsideBorderColor = XLColor.FromArgb(0, 0, 139);
                        supperSum1.Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Right);

                        List<int> supperVeg = new List<int>();
                        var supperVeg1 = 8;
                        for (int i = 1; i <= SupperSchoolNames.Count; i++)
                        {
                            var count = Convert.ToInt32(sheet.Cell(supperVeg1, 7).Value);
                            supperVeg.Add(count);
                            supperVeg1++;
                        }
                        var supperVegSum = supperVeg.Sum(s => s);
                        var supperVegSum1 = sheet.Cell(startSupper, 7).SetValue(supperVegSum);
                        supperVegSum1.Style.Font.SetBold(true);
                        supperVegSum1.Style.Font.SetFontSize(12.5);
                        supperVegSum1.Style.Border.OutsideBorderColor = XLColor.FromArgb(0, 0, 139);
                        supperVegSum1.Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Right);

                        var Total = sheet.Cell(rowIndex + counter, 5).SetValue("Total");
                        Total.Style.Font.SetBold(true);
                        Total.Style.Font.SetFontSize(12.5);
                        Total.Style.Border.OutsideBorderColor = XLColor.FromArgb(0, 0, 139);
                        Total.Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Right);

                        //var TotalSupper = supper1.Cell(startSupper, 5).SetValue("Total");
                        //TotalSupper.Style.Font.SetBold(true);
                        //TotalSupper.Style.Border.OutsideBorderColor = XLColor.FromArgb(0, 0, 139);
                        //TotalSupper.Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Right);

                        var tableContent =
                        sheet.Range(8, 1, rowIndex + counter, 7).AddToNamed("tableContent");
                        tableContent.Style.Border.InsideBorderColor = XLColor.FromArgb(0, 0, 221);
                        tableContent.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
                        tableContent.Style.Border.OutsideBorderColor = XLColor.FromArgb(0, 0, 139);
                        tableContent.Style.Border.OutsideBorder = XLBorderStyleValues.Medium;

                    }
                    var directory = String.Format(path + "Documents\\DAILYSUPPERREPORT.xlsx");
                    if (!Directory.Exists(directory))
                        workBook.SaveAs(directory);
                    var filePath1 = Path.Combine(path, "Documents\\DAILYSUPPERREPORT.xlsx");
                    var workBook1 = new XLWorkbook(filePath1);
                    var sheet1 = workBook.Worksheet(1);
                    workBook.SaveAs(filePath1);
                    response.FileName = filePath1;
                });
        }

        public ReportResponse DailyBreakfastRouteReport(OrderDayPropItemRequest request)
        {
            return Execute<OrderDayPropItemRequest, ReportResponse>(
                request,
                response =>
                {
                    var orderDayDate = Convert.ToDateTime(request.Filter.OrderDate);
                    var orderItems = _mealMenuOrderFacade.GetDailyBreakfastRouteReport(request.Filter);
                    var orderLunchItems = _mealMenuOrderFacade.GetDailyBreakfastRouteReportSchools(request.Filter);
                    var path = AppDomain.CurrentDomain.BaseDirectory;
                    var filePath = Path.Combine(path, "Templates\\DAILYBREAKFASTWITHOUTORDERREPORT.xlsx");
                    var workBook = new XLWorkbook(filePath);
                    var sheet = workBook.Worksheet(1);
                    var meals = Lookups.MealTypeShortList.OrderBy(d => d.Id).ToList();
                    var rowIndex = 6;
                    var counter = 0;
                    var milkTotalCount = 0;
                    var milkMenus = _menuFacade.GetByFilter(new MenuFilterView { MenuTypeId = (int)MenuTypes.Milk, RecordStatusId = (int)Meal.Config.RecordStatuses.Active }, 0, 1, "Name", true, out milkTotalCount);
                    var schoolList = orderItems.GroupBy(d => new { d.SchoolName, d.TotalCount, d.MenuName, d.menuType }).Select(d => new
                    {
                        totalCount = d.Key.TotalCount,
                        SchoolName = d.Key.SchoolName,
                        menuName = d.Key.MenuName,
                        MenuType = d.Key.menuType
                    }).ToList();

                    var menuItem = orderItems.GroupBy(d => new { d.id, d.FruitCount, d.MealType, d.VegetableCount, d.SchoolName, d.TotalCount, d.MenuName, d.validate, d.menuType }).Select(d => new
                    {
                        menuId = d.Key.id,
                        menuName = d.Key.MenuName,
                        totalCount = d.Key.TotalCount,
                        fruitCount = d.Key.FruitCount,
                        vegCount = d.Key.VegetableCount,
                        validdate = d.Key.validate,
                        menuTypeId = d.Key.menuType,
                        SchoolName = d.Key.SchoolName,
                        mealType = d.Key.MealType
                    }).ToList();
                    var mealType1 = new long[] { 2, 3, 4 };
                    //var schoolNameList1 = orderItems.GroupBy(d => new { d.SchoolName, d.Route, d.ServiceType, d.LunchOVS, d.Type }).Distinct().ToList();
                    var schoolNameList = orderItems.GroupBy(d => new { d.SchoolName, d.Route, d.ServiceType, d.BreakFast, d.Type }).Select(d => new
                    {
                        d.Key.SchoolName,
                        d.Key.Route,
                        d.Key.ServiceType,
                        d.Key.BreakFast,
                        d.Key.Type
                    }).Distinct().ToList();

                    var lunchschools = orderLunchItems.GroupBy(d => new { d.SchoolName, d.Route, d.ServiceType, d.BreakFast, d.Type }).Select(d => new
                    {
                        d.Key.SchoolName,
                        d.Key.Route,
                        d.Key.ServiceType,
                        d.Key.BreakFast,
                        d.Key.Type
                    }).Distinct().ToList();

                    //List<string> countSchools = new List<string>();

                    //lunchschools.ForEach(s1 =>
                    //{
                    //    schoolNameList.ForEach(s2 => {
                    //        if (s1.SchoolName != s2.SchoolName)
                    //        {
                    //            var SchoolCount = s1.SchoolName;
                    //            countSchools.Add(SchoolCount.ToString());
                    //            counter++;
                    //        }
                    //    });
                    //});

                    //var list22 = countSchools.Distinct();

                    var listSchoolsOrder = schoolNameList.AsEnumerable().Where(
                        r => !lunchschools.AsEnumerable().Select(x => x.SchoolName).ToList().Contains(r.SchoolName)).ToList();

                    listSchoolsOrder.ForEach(m =>
                    {
                        sheet.Cell(rowIndex + counter, 5).SetValue(m.SchoolName);
                        sheet.Cell(rowIndex + counter, 10).SetValue(0);
                        sheet.Cell(rowIndex + counter, 11).SetValue(0);
                        sheet.Cell(rowIndex + counter, 12).SetValue(0);
                        sheet.Cell(rowIndex + counter, 8).SetValue(0);
                        sheet.Cell(rowIndex + counter, 9).SetValue(0);
                        sheet.Cell(rowIndex + counter, 1).SetValue(Convert.ToInt32(m.Route));
                        sheet.Cell(rowIndex + counter, 2).SetValue(m.ServiceType);
                        sheet.Cell(rowIndex + counter, 3).SetValue(m.BreakFast);
                        sheet.Cell(rowIndex + counter, 4).SetValue(m.Type);
                        var meal = sheet.Cell(rowIndex + counter, 6).SetValue(0);
                        meal.Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Right);
                        sheet.Cell(rowIndex + counter, 7).SetValue(0);
                        sheet.Cell(rowIndex + counter, 15).SetValue(0);
                        sheet.Cell(rowIndex + counter, 16).SetValue(0);
                        sheet.Cell(rowIndex + counter, 17).SetValue(0);
                        counter++;
                    });
                    var OrderDate = Convert.ToDateTime(orderDayDate.ToShortDateString());
                    var orderDay = OrderDate.Day;
                    var orderMonth = OrderDate.ToString("MMM");
                    var orderYear = OrderDate.ToString("yy");
                    var reportDate = DateTime.Now;
                    sheet.Cell(2, 1).SetValue("Report Date : " + reportDate);
                    sheet.Cell(2, 6).SetValue(orderDay + "-" + orderMonth + "-" + orderYear);

                    int days;
                    var rowDays = 7;
                    for (days = 1; days <= 11; days++)
                    {
                        if (days != 7 && days != 8)
                        {
                            sheet.Cell(4, rowDays).SetValue(orderDay);
                            sheet.Cell(3, rowDays).SetValue(orderMonth);
                        }
                        rowDays++;
                    }
                    var Bfday = OrderDate.ToShortDateString();
                    sheet.Cell(3, 5).SetValue(orderMonth + " " + Bfday + " BK");

                    var Total = sheet.Cell(rowIndex + counter, 5).SetValue("Total");
                    Total.Style.Font.SetBold(true);
                    Total.Style.Font.SetFontSize(12.5);
                    Total.Style.Border.OutsideBorderColor = XLColor.FromArgb(0, 0, 139);
                    Total.Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Right);

                    //var TotalSupper = supper1.Cell(startSupper, 5).SetValue("Total");
                    //TotalSupper.Style.Font.SetBold(true);
                    //TotalSupper.Style.Border.OutsideBorderColor = XLColor.FromArgb(0, 0, 139);
                    //TotalSupper.Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Right);
                    var totalCountValue = 6;

                    for (int i = 1; i <= 12; i++)
                    {
                        var TotalValues = sheet.Cell(rowIndex + counter, totalCountValue).SetValue(0);
                        TotalValues.Style.Font.SetBold(true);
                        TotalValues.Style.Font.SetFontSize(12.5);
                        TotalValues.Style.Border.OutsideBorderColor = XLColor.FromArgb(0, 0, 139);
                        TotalValues.Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Right);
                        totalCountValue++;
                    }


                    var tableContent =
                        sheet.Range(6, 1, rowIndex + counter, 17).AddToNamed("tableContent");
                    tableContent.Style.Border.InsideBorderColor = XLColor.FromArgb(0, 0, 221);
                    tableContent.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
                    tableContent.Style.Border.OutsideBorderColor = XLColor.FromArgb(0, 0, 139);
                    tableContent.Style.Border.OutsideBorder = XLBorderStyleValues.Medium;

                    //var tableContentSupper =
                    //supper1.Range(8, 1, startSupper, 7).AddToNamed("tableContent");
                    //tableContentSupper.Style.Border.InsideBorderColor = XLColor.FromArgb(0, 0, 221);
                    //tableContentSupper.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
                    //tableContentSupper.Style.Border.OutsideBorderColor = XLColor.FromArgb(0, 0, 139);
                    //tableContentSupper.Style.Border.OutsideBorder = XLBorderStyleValues.Medium;


                    var directory = String.Format(path + "Documents\\DAILYBREAKFASTWITHOUTORDERREPORT.xlsx");
                    if (!Directory.Exists(directory))
                        workBook.SaveAs(directory);
                    var filePath1 = Path.Combine(path, "Documents\\DAILYBREAKFASTWITHOUTORDERREPORT.xlsx");
                    var workBook1 = new XLWorkbook(filePath1);

                    workBook.SaveAs(filePath1);
                    response.FileName = filePath1;
                });
        }
        public ReportResponse DailyLunchRouteReport(OrderDayPropItemRequest request)
        {
            return Execute<OrderDayPropItemRequest, ReportResponse>(
                request,
                response =>
                {
                    var orderDayDate = Convert.ToDateTime(request.Filter.OrderDate);
                    var orderItems = _mealMenuOrderFacade.GetDailyLunchRouteReport(request.Filter);
                    var orderLunchItems = _mealMenuOrderFacade.GetDailyLunchRouteReportSchools(request.Filter);
                    var path = AppDomain.CurrentDomain.BaseDirectory;
                    var filePath = Path.Combine(path, "Templates\\DAILYLUNCHSNACKWITHOUTREPORT.xlsx");
                    var workBook = new XLWorkbook(filePath);
                    var sheet = workBook.Worksheet(1);
                    var meals = Lookups.MealTypeShortList.OrderBy(d => d.Id).ToList();
                    var rowIndex = 8;
                    var counter = 0;
                    var milkTotalCount = 0;
                    var milkMenus = _menuFacade.GetByFilter(new MenuFilterView { MenuTypeId = (int)MenuTypes.Milk, RecordStatusId = (int)Meal.Config.RecordStatuses.Active }, 0, 1, "Name", true, out milkTotalCount);
                    var schoolList = orderItems.GroupBy(d => new { d.SchoolName, d.TotalCount, d.MenuName, d.menuType }).Select(d => new
                    {
                        totalCount = d.Key.TotalCount,
                        SchoolName = d.Key.SchoolName,
                        menuName = d.Key.MenuName,
                        MenuType = d.Key.menuType
                    }).ToList();

                    var menuItem = orderItems.GroupBy(d => new { d.id, d.FruitCount, d.MealType, d.VegetableCount, d.SchoolName, d.TotalCount, d.MenuName, d.validate, d.menuType }).Select(d => new
                    {
                        menuId = d.Key.id,
                        menuName = d.Key.MenuName,
                        totalCount = d.Key.TotalCount,
                        fruitCount = d.Key.FruitCount,
                        vegCount = d.Key.VegetableCount,
                        validdate = d.Key.validate,
                        menuTypeId = d.Key.menuType,
                        SchoolName = d.Key.SchoolName,
                        mealType = d.Key.MealType
                    }).ToList();
                    var mealType1 = new long[] { 2, 3, 4 };
                    //var schoolNameList1 = orderItems.GroupBy(d => new { d.SchoolName, d.Route, d.ServiceType, d.LunchOVS, d.Type }).Distinct().ToList();
                    var schoolNameList = orderItems.GroupBy(d => new { d.SchoolName, d.Route, d.ServiceType, d.LunchOVS, d.Type }).Select(d => new
                    {
                        d.Key.SchoolName,
                        d.Key.Route,
                        d.Key.ServiceType,
                        d.Key.LunchOVS,
                        d.Key.Type
                    }).Distinct().ToList();

                    var lunchschools = orderLunchItems.GroupBy(d => new { d.SchoolName, d.Route, d.ServiceType, d.LunchOVS, d.Type }).Select(d => new
                    {
                        d.Key.SchoolName,
                        d.Key.Route,
                        d.Key.ServiceType,
                        d.Key.LunchOVS,
                        d.Key.Type
                    }).Distinct().ToList();

                    //List<string> countSchools = new List<string>();
                    
                    //lunchschools.ForEach(s1 =>
                    //{
                    //    schoolNameList.ForEach(s2 => {
                    //        if (s1.SchoolName != s2.SchoolName)
                    //        {
                    //            var SchoolCount = s1.SchoolName;
                    //            countSchools.Add(SchoolCount.ToString());
                    //            counter++;
                    //        }
                    //    });
                    //});

                    //var list22 = countSchools.Distinct();

                    var listSchoolsOrder = schoolNameList.AsEnumerable().Where(
                        r => !lunchschools.AsEnumerable().Select(x => x.SchoolName).ToList().Contains(r.SchoolName)).ToList();

                    listSchoolsOrder.ForEach(m =>
                    {
                        var SchoolName = m.SchoolName;
                        sheet.Cell(rowIndex + counter, 1).SetValue(0);
                        sheet.Cell(rowIndex + counter, 5).SetValue(SchoolName);
                        sheet.Cell(rowIndex + counter, 1).SetValue(Convert.ToInt32(m.Route));
                        sheet.Cell(rowIndex + counter, 2).SetValue(m.ServiceType);
                        sheet.Cell(rowIndex + counter, 3).SetValue(m.LunchOVS);
                        sheet.Cell(rowIndex + counter, 4).SetValue(m.Type);
                        var mealTotal = sheet.Cell(rowIndex + counter, 6).SetValue(0);
                        mealTotal.Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Right);
                        sheet.Cell(rowIndex + counter, 7).SetValue(0);
                        sheet.Cell(rowIndex + counter, 8).SetValue(0);
                        sheet.Cell(rowIndex + counter, 9).SetValue(0);
                        sheet.Cell(rowIndex + counter, 10).SetValue(0);
                        sheet.Cell(rowIndex + counter, 11).SetValue(0);
                        sheet.Cell(rowIndex + counter, 12).SetValue(0);
                        var soys = sheet.Cell(rowIndex + counter, 13).SetValue(0);
                        soys.Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Right);
                        var bbqs = sheet.Cell(rowIndex + counter, 14).SetValue(0);
                        bbqs.Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Right);
                        var pickups = sheet.Cell(rowIndex + counter, 15).SetValue(0);
                        pickups.Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Right);
                        var pizzas = sheet.Cell(rowIndex + counter, 16).SetValue(0);
                        pizzas.Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Right);
                        var vegLunch = sheet.Cell(rowIndex + counter, 17).SetValue(0);
                        vegLunch.Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Right);
                        sheet.Cell(rowIndex + counter, 20).SetValue(0);
                        sheet.Cell(rowIndex + counter, 21).SetValue(0);
                        sheet.Cell(rowIndex + counter, 22).SetValue(0);
                        sheet.Cell(rowIndex + counter, 24).SetValue(0);
                        sheet.Cell(rowIndex + counter, 25).SetValue(0);
                        sheet.Cell(rowIndex + counter, 26).SetValue(0);
                        sheet.Cell(rowIndex + counter, 27).SetValue(0);
                        counter++;
                    });
                    var OrderDate = Convert.ToDateTime(orderDayDate.ToShortDateString());
                    var orderDay = OrderDate.Day;
                    var orderMonth = OrderDate.ToString("MMM");
                    var orderYear = OrderDate.ToString("yy");
                    var reportDate = DateTime.Now;
                    sheet.Cell(5, 1).SetValue("Report Date : " + reportDate);
                    sheet.Cell(2, 6).SetValue(orderDay + "-" + orderMonth + "-" + orderYear);

                    int days;
                    var rowDays = 7;
                    for (days = 1; days <= 21; days++)
                    {
                        if (days != 12 && days != 13)
                        {
                            sheet.Cell(6, rowDays).SetValue(orderDay);
                            sheet.Cell(5, rowDays).SetValue(orderMonth);
                        }
                        rowDays++;
                    }
                    var Lunchday = OrderDate.ToShortDateString();
                    sheet.Cell(5, 5).SetValue(orderMonth + " " + Lunchday + " LUNCH");

                    var Total = sheet.Cell(rowIndex + counter, 5).SetValue("Total");
                    Total.Style.Font.SetBold(true);
                    Total.Style.Font.SetFontSize(12.5);
                    Total.Style.Border.OutsideBorderColor = XLColor.FromArgb(0, 0, 139);
                    Total.Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Right);

                   

                    //var TotalSupper = supper1.Cell(startSupper, 5).SetValue("Total");
                    //TotalSupper.Style.Font.SetBold(true);
                    //TotalSupper.Style.Border.OutsideBorderColor = XLColor.FromArgb(0, 0, 139);
                    //TotalSupper.Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Right);
                    var totalCountValue = 6;

                    for (int i = 1; i <= 22; i++)
                    {
                        var TotalValues = sheet.Cell(rowIndex + counter, totalCountValue).SetValue(0);
                        TotalValues.Style.Font.SetBold(true);
                        TotalValues.Style.Font.SetFontSize(12.5);
                        TotalValues.Style.Border.OutsideBorderColor = XLColor.FromArgb(0, 0, 139);
                        TotalValues.Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Right);
                        totalCountValue++;
                    }


                    var tableContent =
                         sheet.Range(8, 1, rowIndex + counter, 27).AddToNamed("tableContent");
                    tableContent.Style.Border.InsideBorderColor = XLColor.FromArgb(0, 0, 221);
                    tableContent.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
                    tableContent.Style.Border.OutsideBorderColor = XLColor.FromArgb(0, 0, 139);
                    tableContent.Style.Border.OutsideBorder = XLBorderStyleValues.Medium;

                    //var tableContentSupper =
                    //supper1.Range(8, 1, startSupper, 7).AddToNamed("tableContent");
                    //tableContentSupper.Style.Border.InsideBorderColor = XLColor.FromArgb(0, 0, 221);
                    //tableContentSupper.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
                    //tableContentSupper.Style.Border.OutsideBorderColor = XLColor.FromArgb(0, 0, 139);
                    //tableContentSupper.Style.Border.OutsideBorder = XLBorderStyleValues.Medium;


                    var directory = String.Format(path + "Documents\\DAILYLUNCHSNACKWITHOUTREPORT.xlsx");
                    if (!Directory.Exists(directory))
                        workBook.SaveAs(directory);
                    var filePath1 = Path.Combine(path, "Documents\\DAILYLUNCHSNACKWITHOUTREPORT.xlsx");
                    var workBook1 = new XLWorkbook(filePath1);
                    
                    workBook.SaveAs(filePath1);
                    response.FileName = filePath1;
                });
        }

        public ReportResponse DailyLunchReport(OrderDayPropItemRequest request)
        {
            return Execute<OrderDayPropItemRequest, ReportResponse>(
                request,
                response =>
                {
                    //int totalCount = 0;
                    var orderDayDate = Convert.ToDateTime(request.Filter.OrderDate);
                    //var mealSackList = _mealMenuOrderFacade.GetOrderReport(request1.Filter, 1000, 1, "Route", true, out totalCount);
                    var VegCount = _mealMenuOrderFacade.GetLunchVegetableReport(request.Filter);
                    var orderItems = _mealMenuOrderFacade.GetDailyLunchReport(request.Filter);
                    //var orderItems1 = _mealMenuOrderFacade.GetDailySupperRouteReport(request.Filter);
                    var path = AppDomain.CurrentDomain.BaseDirectory;
                    var filePath = Path.Combine(path, "Templates\\DAILYLUNCHSNACKREPORT.xlsx");
                    var workBook = new XLWorkbook(filePath);
                    var sheet = workBook.Worksheet(1);
                    var meals = Lookups.MealTypeShortList.OrderBy(d => d.Id).ToList();
                    var rowIndex = 8;
                    var counter = 0;
                    var milkTotalCount = 0;
                    var milkMenus = _menuFacade.GetByFilter(new MenuFilterView { MenuTypeId = (int)MenuTypes.Milk, RecordStatusId = (int)Meal.Config.RecordStatuses.Active }, 0, 1, "Name", true, out milkTotalCount);
                    var schoolList = orderItems.GroupBy(d => new { d.SchoolName, d.TotalCount, d.MenuName, d.menuType }).Select(d => new
                    {
                        totalCount = d.Key.TotalCount,
                        SchoolName = d.Key.SchoolName,
                        menuName = d.Key.MenuName,
                        MenuType = d.Key.menuType
                    }).ToList();

                    var menuItem = orderItems.GroupBy(d => new { d.id, d.FruitCount, d.MealType, d.VegetableCount, d.SchoolName, d.TotalCount, d.MenuName, d.validate, d.menuType }).Select(d => new
                    {
                        menuId = d.Key.id,
                        menuName = d.Key.MenuName,
                        totalCount = d.Key.TotalCount,
                        fruitCount = d.Key.FruitCount,
                        vegCount = d.Key.VegetableCount,
                        validdate = d.Key.validate,
                        menuTypeId = d.Key.menuType,
                        SchoolName = d.Key.SchoolName,
                        mealType = d.Key.MealType
                    }).ToList();
                    var mealType1 = new long[] { 2, 3, 4 };

                   
                    //var schoolNameList1 = orderItems.GroupBy(d => new { d.SchoolName, d.Route, d.ServiceType, d.LunchOVS, d.Type }).Distinct().ToList();
                    var schoolNameList = orderItems.GroupBy(d => new { d.SchoolName, d.Route, d.ServiceType, d.LunchOVS, d.Type }).Select(d => new
                    {
                        d.Key.SchoolName,
                        d.Key.Route,
                        d.Key.ServiceType,
                        d.Key.LunchOVS,
                        d.Key.Type
                    }).Distinct().ToList();

                    var coutVegetable = VegCount.GroupBy(s => new { s.SchoolName , s.VegetableCount , s.validate }).Select(s => new { s.Key.VegetableCount , s.Key.validate , s.Key.SchoolName}).ToList();

                    //schoolNameList.ForEach(m =>
                    //{
                    //    sheet.Cell(rowIndex + counter, 5).SetValue(m.SchoolName);
                    //    sheet.Cell(rowIndex + counter, 2).SetValue(m.ServiceType);
                    //    sheet.Cell(rowIndex + counter, 1).SetValue(Convert.ToInt32(m.Route));
                    //    sheet.Cell(rowIndex + counter, 3).SetValue(m.LunchOVS);
                    //    sheet.Cell(rowIndex + counter, 4).SetValue(m.Type);
                    //});

                    schoolNameList.ForEach(m =>
                    {
                        var SchoolName = m.SchoolName;
                        sheet.Cell(rowIndex + counter, 1).SetValue(0);
                        sheet.Cell(rowIndex + counter, 5).SetValue(SchoolName);
                        sheet.Cell(rowIndex + counter, 1).SetValue(Convert.ToInt32(m.Route));
                        sheet.Cell(rowIndex + counter, 2).SetValue(m.ServiceType);
                        sheet.Cell(rowIndex + counter, 3).SetValue(m.LunchOVS);
                        sheet.Cell(rowIndex + counter, 4).SetValue(m.Type);
                        sheet.Cell(rowIndex + counter, 7).SetValue(0);
                        sheet.Cell(rowIndex + counter, 8).SetValue(0);
                        sheet.Cell(rowIndex + counter, 9).SetValue(0);
                        sheet.Cell(rowIndex + counter, 10).SetValue(0);
                        sheet.Cell(rowIndex + counter, 11).SetValue(0);
                        sheet.Cell(rowIndex + counter, 12).SetValue(0);
                        var soys = sheet.Cell(rowIndex + counter, 13).SetValue(0);
                        soys.Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Right);
                        var bbqs = sheet.Cell(rowIndex + counter, 14).SetValue(0);
                        bbqs.Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Right);
                        var pickups = sheet.Cell(rowIndex + counter, 15).SetValue(0);
                        pickups.Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Right);
                        var pizzas = sheet.Cell(rowIndex + counter, 16).SetValue(0);
                        pizzas.Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Right);
                        var vegLunch = sheet.Cell(rowIndex + counter, 17).SetValue(0);
                        vegLunch.Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Right);
                        sheet.Cell(rowIndex + counter, 20).SetValue(0);
                        sheet.Cell(rowIndex + counter, 21).SetValue(0);
                        sheet.Cell(rowIndex + counter, 22).SetValue(0);
                        sheet.Cell(rowIndex + counter, 24).SetValue(0);
                        sheet.Cell(rowIndex + counter, 25).SetValue(0);
                        sheet.Cell(rowIndex + counter, 26).SetValue(0);
                        sheet.Cell(rowIndex + counter, 27).SetValue(0);
                        counter++;
                    });
                    var OrderDate = Convert.ToDateTime(orderDayDate.ToShortDateString());
                    var orderDay = OrderDate.Day;
                    var orderMonth = OrderDate.ToString("MMM");
                    var orderYear = OrderDate.ToString("yy");
                    var reportDate = DateTime.Now;
                    sheet.Cell(5, 1).SetValue("Report Date : " + reportDate);
                    sheet.Cell(2, 6).SetValue(orderDay + "-" + orderMonth + "-" + orderYear);

                    int days;
                    var rowDays = 7;
                    for (days = 1; days <= 21; days++)
                    {
                        if (days != 12 && days != 13)
                        {
                            sheet.Cell(6, rowDays).SetValue(orderDay);
                            sheet.Cell(5, rowDays).SetValue(orderMonth);
                        }
                        rowDays++;
                    }
                    var Lunchday = OrderDate.ToShortDateString();
                    sheet.Cell(5, 5).SetValue(orderMonth + " " + Lunchday + " LUNCH");




                    var orderColumnStart = 21;
                    for (var day = orderDayDate.Day; day <= orderDayDate.Day; day++)
                    {
                        var currentDay = new DateTime(orderDayDate.Year, orderDayDate.Month, day);
                        if (currentDay.DayOfWeek == DayOfWeek.Saturday ||
                            currentDay.DayOfWeek == DayOfWeek.Sunday)
                            continue;

                        var snackCount = meals.ToList();
                        var LunchOption1 = menuItem.Where(m => m.validdate == currentDay && m.menuTypeId == (int)MenuTypes.LunchOption1).Select(d => new { d.totalCount, d.SchoolName }).ToList();
                        var LunchOption2 = menuItem.Where(m => m.validdate == currentDay && m.menuTypeId == (int)MenuTypes.LunchOption2).Select(d => new { d.totalCount, d.SchoolName }).ToList();
                        var LunchOption3 = menuItem.Where(m => m.validdate == currentDay && m.menuTypeId == (int)MenuTypes.LunchOption3).Select(d => new { d.totalCount, d.SchoolName }).ToList();
                        var LunchOption4 = menuItem.Where(m => m.validdate == currentDay && m.menuTypeId == (int)MenuTypes.LunchOption4).Select(d => new { d.totalCount, d.SchoolName }).ToList();
                        var LunchOption5 = menuItem.Where(m => m.validdate == currentDay && m.menuTypeId == (int)MenuTypes.LunchOption5).Select(d => new { d.totalCount, d.SchoolName }).ToList();
                        var Vegetarian = menuItem.Where(m => m.validdate == currentDay && m.menuTypeId == (int)MenuTypes.Vegetarian && m.mealType.Id == 3).Select(d => new { d.totalCount, d.SchoolName }).ToList();
                        var SoyMilk = menuItem.Where(m => m.validdate == currentDay && m.menuTypeId == 14).Select(d => new { d.totalCount, d.SchoolName }).ToList();
                        var BBQ = menuItem.Where(m => m.validdate == currentDay && m.menuTypeId == (int)MenuTypes.Bbq && m.mealType.Id == 3).Select(d => new { d.totalCount, d.SchoolName }).ToList();
                        var PickupStix = menuItem.Where(m => m.validdate == currentDay && m.menuTypeId == 10).Select(d => new { d.totalCount, d.SchoolName }).ToList();
                        var Pizza = menuItem.Where(m => m.validdate == currentDay && m.menuTypeId == 13).Select(d => new { d.totalCount, d.SchoolName }).ToList();
                        var VegetableLunch = coutVegetable.Where(m => m.validate == currentDay).Select(d => new { d.VegetableCount , d.SchoolName}).ToList();
                        var snack1 = menuItem.Where(m => m.validdate == currentDay && m.mealType.Id == 2).Select(d => new { d.totalCount, d.SchoolName }).Distinct().ToList();
                        var snack2 = menuItem.Where(m => m.validdate == currentDay && m.menuTypeId == (int)MenuTypes.Snack2).Select(d => new { d.totalCount, d.SchoolName }).Distinct().ToList();
                        var SackLunch1 = menuItem.Where(m => m.validdate == currentDay && m.mealType.Id == 4 && m.menuTypeId == (int)MenuTypes.SackLunch1).Select(d => new { d.totalCount, d.SchoolName }).Distinct().ToList();
                        var SackLunch2 = menuItem.Where(m => m.validdate == currentDay && m.mealType.Id == 4 && m.menuTypeId == (int)MenuTypes.Vegetarian).Select(d => new { d.totalCount, d.SchoolName }).Distinct().ToList();
                        var lunchVegetable = menuItem.Where(m => m.validdate == currentDay && m.mealType.Id == 3).Select(d => new { d.vegCount , d.SchoolName }).Distinct().ToList();
                        var Lunch1orderRowStart = 8;
                        var Lunch2orderRowStart = 8;
                        var Lunch3orderRowStart = 8;
                        var Lunch4orderRowStart = 8;
                        var Lunch5orderRowStart = 8;
                        var LunchVegetableCount = 8;
                        schoolNameList.ForEach(l =>
                        {
                            LunchOption1.ForEach(lo1 =>
                            {
                                if (l.SchoolName == lo1.SchoolName)
                                {
                                    sheet.Cell(Lunch1orderRowStart, 7).SetValue(lo1.totalCount);
                                }
                            });
                            Lunch1orderRowStart++;
                        });


                        schoolNameList.ForEach(lo1 =>
                        {
                            VegetableLunch.ForEach(l =>
                            {
                                if (l.SchoolName == lo1.SchoolName) {
                                    var count = sheet.Cell(LunchVegetableCount, 17).SetValue(l.VegetableCount);
                                }
                            });
                            LunchVegetableCount++;
                        });

                        //schoolNameList.ForEach(l =>
                        //{
                        //    VegetableLunch.ForEach(lo1 =>
                        //    {
                        //        if (l.SchoolName == lo1.SchoolName)
                        //        {
                        //            var count = sheet.Cell(LunchVegetableCount, 17).SetValue(lo1.VegetableCount);
                        //        }
                        //    });
                        //    LunchVegetableCount++;
                        //});

                        schoolNameList.ForEach(l =>
                        {
                            LunchOption2.ForEach(lo1 =>
                            {
                                if (l.SchoolName == lo1.SchoolName)
                                {
                                    sheet.Cell(Lunch2orderRowStart, 8).SetValue(lo1.totalCount);
                                }
                            });
                            Lunch2orderRowStart++;
                        });
                        schoolNameList.ForEach(l =>
                        {
                            LunchOption3.ForEach(lo1 =>
                            {
                                if (l.SchoolName == lo1.SchoolName)
                                {
                                    sheet.Cell(Lunch3orderRowStart, 9).SetValue(lo1.totalCount);
                                }
                            });
                            Lunch3orderRowStart++;
                        });
                        schoolNameList.ForEach(l =>
                        {
                            LunchOption4.ForEach(lo1 =>
                            {
                                if (l.SchoolName == lo1.SchoolName)
                                {
                                    sheet.Cell(Lunch4orderRowStart, 10).SetValue(lo1.totalCount);
                                }
                            });
                            Lunch4orderRowStart++;
                        });
                        schoolNameList.ForEach(l =>
                        {
                            LunchOption5.ForEach(lo1 =>
                            {
                                if (l.SchoolName == lo1.SchoolName)
                                {
                                    sheet.Cell(Lunch5orderRowStart, 11).SetValue(lo1.totalCount);
                                }
                            });
                            Lunch5orderRowStart++;
                        });

                        var VegOrderRowStart = 8;
                        schoolNameList.ForEach(l =>
                        {
                            Vegetarian.ForEach(lo1 =>
                            {
                                if (l.SchoolName == lo1.SchoolName)
                                {
                                    sheet.Cell(VegOrderRowStart, 12).SetValue(lo1.totalCount);
                                }
                            });
                            VegOrderRowStart++;
                        });

                        var SoyMilkCount = 8;
                        schoolNameList.ForEach(sl =>
                        {
                            SoyMilk.ForEach(sn =>
                            {
                                if (sl.SchoolName == sn.SchoolName)
                                {
                                    sheet.Cell(SoyMilkCount, 13).SetValue(sn.totalCount);
                                }
                            });
                            SoyMilkCount++;
                        });

                        var schoolNameBBQ = menuItem.Where(item => item.menuName == "BBQ Chicken Nuggets").Select(total => new { total.SchoolName, total.totalCount }).ToList();
                        var BBQCount = 8;
                        schoolNameList.ForEach(sl =>
                        {
                            BBQ.ForEach(sn =>
                            {
                                if (sl.SchoolName == sn.SchoolName)
                                {
                                    sheet.Cell(BBQCount, 14).SetValue(sn.totalCount);
                                }
                            });
                            BBQCount++;
                        });
                        var PStixCount = 8;
                        schoolNameList.ForEach(sl =>
                        {
                            PickupStix.ForEach(sn =>
                            {
                                if (sl.SchoolName == sn.SchoolName)
                                {
                                    sheet.Cell(PStixCount, 15).SetValue(sn.totalCount);
                                }
                            });
                            PStixCount++;
                        });

                        var PizzaCount = 8;
                        schoolNameList.ForEach(sl =>
                        {
                            Pizza.ForEach(sn =>
                            {
                                if (sl.SchoolName == sn.SchoolName)
                                {
                                    sheet.Cell(PizzaCount, 16).SetValue(sn.totalCount);
                                }
                            });
                            PizzaCount++;
                        });

                        var juice = sheet.Cell(rowIndex + counter, 18).SetValue("");
                        juice.Style.Font.SetBold(true);
                        juice.Style.Font.SetFontSize(12.5);
                        juice.Style.Border.OutsideBorderColor = XLColor.FromArgb(0, 0, 139);
                        juice.Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Right);

                        var fruit = sheet.Cell(rowIndex + counter, 19).SetValue("");
                        fruit.Style.Font.SetBold(true);
                        fruit.Style.Font.SetFontSize(12.5);
                        fruit.Style.Border.OutsideBorderColor = XLColor.FromArgb(0, 0, 139);
                        fruit.Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Right);

                        orderColumnStart++;

                        var Menu1 = orderItems.Where(d => d.MealType.Id == 3 && d.MenuName == "1% White Milk").Select(s => new { s.SchoolName, s.TotalCount }).Distinct().ToList();
                        var Menu2 = orderItems.Where(d => d.MealType.Id == 3 && d.MenuName == "Fat Free White Milk ").Select(s => new { s.SchoolName, s.TotalCount }).Distinct().ToList();
                        var Menu3 = orderItems.Where(d => d.MealType.Id == 3 && d.MenuName == "Fat Free Chocolate Milk ").Select(s => new { s.SchoolName, s.TotalCount }).Distinct().ToList();

                        var milk1Option = 8;
                        schoolNameList.ForEach(l =>
                        {
                            Menu1.ForEach(lo1 =>
                            {
                                if (l.SchoolName == lo1.SchoolName)
                                {
                                    sheet.Cell(milk1Option, 20).SetValue(lo1.TotalCount);
                                }
                            });
                            milk1Option++;
                        });
                        var milk2Option = 8;
                        schoolNameList.ForEach(l =>
                        {
                            Menu2.ForEach(lo1 =>
                            {
                                if (l.SchoolName == lo1.SchoolName)
                                {
                                    sheet.Cell(milk2Option, 21).SetValue(lo1.TotalCount);
                                }
                            });
                            milk2Option++;
                        });
                        var milk3Option = 8;
                        schoolNameList.ForEach(l =>
                        {
                            Menu3.ForEach(lo1 =>
                            {
                                if (l.SchoolName == lo1.SchoolName)
                                {
                                    sheet.Cell(milk3Option, 22).SetValue(lo1.TotalCount);
                                }
                            });
                            milk3Option++;
                        });

                        orderColumnStart++;
                        var snackOption1 = menuItem.Where(m => m.validdate == currentDay && m.menuTypeId == (int)MenuTypes.Snack1).Select(d => new { d.totalCount, d.SchoolName }).ToList();
                        var Snack1Count = 8;
                        schoolNameList.ForEach(sl =>
                        {
                            snack1.ForEach(sn =>
                            {
                                if (sl.SchoolName == sn.SchoolName)
                                {
                                    sheet.Cell(Snack1Count, 24).SetValue(sn.totalCount);
                                }
                            });
                            Snack1Count++;
                        });
                        var snackOption2 = menuItem.Where(m => m.validdate == currentDay && m.menuTypeId == (int)MenuTypes.Snack2).Select(d => new { d.totalCount, d.SchoolName }).ToList();
                        var Snack2Count = 8;
                        schoolNameList.ForEach(sl =>
                        {
                            snackOption2.ForEach(sn =>
                            {
                                if (sl.SchoolName == sn.SchoolName)
                                {
                                    sheet.Cell(Snack2Count, 25).SetValue(sn.totalCount);
                                }
                            });
                            Snack2Count++;
                        });
                        var sack1 = menuItem.Where(m => m.validdate == currentDay && m.menuTypeId == (int)MenuTypes.SackLunch1).Select(d => new { d.totalCount, d.SchoolName }).ToList();
                        var sack1Option = 8;
                        schoolNameList.ForEach(sl =>
                        {
                            SackLunch1.ForEach(sn =>
                            {
                                if (sl.SchoolName == sn.SchoolName)
                                {
                                    sheet.Cell(sack1Option, 26).SetValue(sn.totalCount);
                                }
                            });
                            sack1Option++;
                        });
                        var sack2 = menuItem.Where(m => m.validdate == currentDay && m.menuTypeId == (int)MenuTypes.SackLunch2).Select(d => new { d.totalCount, d.SchoolName }).ToList();
                        var sack2Option = 8;
                        schoolNameList.ForEach(sl =>
                        {
                            SackLunch2.ForEach(sn =>
                            {
                                if (sl.SchoolName == sn.SchoolName)
                                {
                                    sheet.Cell(sack2Option, 27).SetValue(sn.totalCount);
                                }
                            });
                            sack2Option++;
                        });

                        //var SupperSchoolNames = orderItems.Where(m => m.validate == currentDay && m.MealType.Id == 5).Select(d => new { d.SchoolName, d.Route, d.Type, d.ServiceType, d.LunchOVS }).Distinct().ToList();
                        //var Supper1 = menuItem.Where(m => m.validdate == currentDay && m.menuTypeId == (int)MenuTypes.Supper1).Select(d => new { d.totalCount, d.SchoolName }).ToList();
                        //var Supper2 = menuItem.Where(m => m.validdate == currentDay && m.menuTypeId == (int)MenuTypes.Supper2).Select(d => new { d.totalCount, d.SchoolName }).ToList();
                        //var startSupper = 8;



                        //var SupperCount2 = menuItem.Where(m => m.validdate == currentDay && m.menuTypeId == (int)MenuTypes.Supper2).Select(d => new { d.totalCount, d.SchoolName }).ToList();
                        //var Supper2Option = 8;
                        //SupperSchoolNames.ForEach(sl =>
                        //{
                        //    Supper2.ForEach(sn =>
                        //    {
                        //        if (sl.SchoolName == sn.SchoolName)
                        //        {
                        //            supper1.Cell(Supper2Option, 7).SetValue(sn.totalCount);
                        //        }
                        //    });
                        //    Supper2Option++;
                        //});
                        //var countColumn = SupperSchoolNames.Count + 1;

                        //List<int> supperlist1 = new List<int>();
                        //var supper12 = 8;
                        //for (int i = 1; i <= SupperSchoolNames.Count; i++)
                        //{
                        //    var count = Convert.ToInt32(supper1.Cell(supper12, 6).Value);
                        //    supperlist1.Add(count);
                        //    supper12++;
                        //}
                        //var supperSum = supperlist1.Sum(s => s);
                        //var supperSum1 = supper1.Cell(startSupper, 6).SetValue(supperSum);
                        //supperSum1.Style.Font.SetBold(true);
                        //supperSum1.Style.Font.SetFontSize(12.5);
                        //supperSum1.Style.Border.OutsideBorderColor = XLColor.FromArgb(0, 0, 139);
                        //supperSum1.Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Right);

                        //List<int> supperVeg = new List<int>();
                        //var supperVeg1 = 8;
                        //for (int i = 1; i <= SupperSchoolNames.Count; i++)
                        //{
                        //    var count = Convert.ToInt32(supper1.Cell(supperVeg1, 7).Value);
                        //    supperVeg.Add(count);
                        //    supperVeg1++;
                        //}
                        //var supperVegSum = supperVeg.Sum(s => s);
                        //var supperVegSum1 = supper1.Cell(startSupper, 7).SetValue(supperVegSum);
                        //supperVegSum1.Style.Font.SetBold(true);
                        //supperVegSum1.Style.Font.SetFontSize(12.5);
                        //supperVegSum1.Style.Border.OutsideBorderColor = XLColor.FromArgb(0, 0, 139);
                        //supperVegSum1.Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Right);

                       

                        var Total = sheet.Cell(rowIndex + counter, 5).SetValue("Total");
                        Total.Style.Font.SetBold(true);
                        Total.Style.Font.SetFontSize(12.5);
                        Total.Style.Border.OutsideBorderColor = XLColor.FromArgb(0, 0, 139);
                        Total.Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Right);

                        //var TotalSupper = supper1.Cell(startSupper, 5).SetValue("Total");
                        //TotalSupper.Style.Font.SetBold(true);
                        //TotalSupper.Style.Border.OutsideBorderColor = XLColor.FromArgb(0, 0, 139);
                        //TotalSupper.Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Right);

                        var tableContent =
                        sheet.Range(8, 1, rowIndex + counter, 27).AddToNamed("tableContent");
                        tableContent.Style.Border.InsideBorderColor = XLColor.FromArgb(0, 0, 221);
                        tableContent.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
                        tableContent.Style.Border.OutsideBorderColor = XLColor.FromArgb(0, 0, 139);
                        tableContent.Style.Border.OutsideBorder = XLBorderStyleValues.Medium;

                        //var tableContentSupper =
                        //supper1.Range(8, 1, startSupper, 7).AddToNamed("tableContent");
                        //tableContentSupper.Style.Border.InsideBorderColor = XLColor.FromArgb(0, 0, 221);
                        //tableContentSupper.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
                        //tableContentSupper.Style.Border.OutsideBorderColor = XLColor.FromArgb(0, 0, 139);
                        //tableContentSupper.Style.Border.OutsideBorder = XLBorderStyleValues.Medium;
                    }

                    var directory = String.Format(path + "Documents\\DAILYLUNCHSNACKREPORT.xlsx");
                    if (!Directory.Exists(directory))
                        workBook.SaveAs(directory);
                    var filePath1 = Path.Combine(path, "Documents\\DAILYLUNCHSNACKREPORT.xlsx");
                    var workBook1 = new XLWorkbook(filePath1);
                    var sheet1 = workBook.Worksheet(1);
                    var mealRowStart = 8;

                    List<int> mealList = new List<int>();
                    for (int i = 1; i <= schoolNameList.Count; i++)
                    {
                        int count = Convert.ToInt32(sheet1.Cell(mealRowStart, 7).Value);
                        mealList.Add(count);
                        mealRowStart++;
                    }
                    var mealRowStart1 = 8;
                    List<int> mealList1 = new List<int>();
                    for (int i = 1; i <= schoolNameList.Count; i++)
                    {
                        int count = Convert.ToInt32(sheet1.Cell(mealRowStart1, 8).Value);
                        mealList1.Add(count);
                        mealRowStart1++;
                    }
                    var mealRowStart2 = 8;
                    List<int> mealList2 = new List<int>();
                    for (int i = 1; i <= schoolNameList.Count; i++)
                    {
                        int count = Convert.ToInt32(sheet1.Cell(mealRowStart2, 9).Value);
                        mealList2.Add(count);
                        mealRowStart2++;
                    }
                    var mealRowStart3 = 8;
                    List<int> mealList3 = new List<int>();
                    for (int i = 1; i <= schoolNameList.Count; i++)
                    {
                        int count = Convert.ToInt32(sheet1.Cell(mealRowStart3, 10).Value);
                        mealList3.Add(count);
                        mealRowStart3++;
                    }
                    var mealRowStart4 = 8;
                    List<int> mealList4 = new List<int>();
                    for (int i = 1; i <= schoolNameList.Count; i++)
                    {
                        int count = Convert.ToInt32(sheet1.Cell(mealRowStart4, 11).Value);
                        mealList4.Add(count);
                        mealRowStart4++;
                    }
                    var mealRowStart5 = 8;
                    List<int> mealList5 = new List<int>();
                    for (int i = 1; i <= schoolNameList.Count; i++)
                    {
                        int count = Convert.ToInt32(sheet1.Cell(mealRowStart5, 12).Value);
                        mealList5.Add(count);
                        mealRowStart5++;
                    }
                    var mealRowStart6 = 8;
                    List<int> mealList6 = new List<int>();
                    for (int i = 1; i <= schoolNameList.Count; i++)
                    {
                        int count = Convert.ToInt32(sheet1.Cell(mealRowStart6, 13).Value);
                        mealList6.Add(count);
                        mealRowStart6++;
                    }
                    var mealRowStart7 = 8;
                    List<int> mealList7 = new List<int>();
                    for (int i = 1; i <= schoolNameList.Count; i++)
                    {
                        int count = Convert.ToInt32(sheet1.Cell(mealRowStart7, 14).Value);
                        mealList7.Add(count);
                        mealRowStart7++;
                    }
                    var mealRowStart8 = 8;
                    List<int> mealList8 = new List<int>();
                    for (int i = 1; i <= schoolNameList.Count; i++)
                    {
                        int count = Convert.ToInt32(sheet1.Cell(mealRowStart8, 15).Value);
                        mealList8.Add(count);
                        mealRowStart8++;
                    }
                    var mealRowStart9 = 8;
                    List<int> mealList9 = new List<int>();
                    for (int i = 1; i <= schoolNameList.Count; i++)
                    {
                        int count = Convert.ToInt32(sheet1.Cell(mealRowStart9, 16).Value);
                        mealList9.Add(count);
                        mealRowStart9++;
                    }
                    List<int> countList = new List<int>();
                    for (int i = 0; i < schoolNameList.Count; i++)
                    {
                        int count = mealList[i] + mealList1[i] + mealList2[i] + mealList3[i] + mealList4[i] + mealList5[i]
                                    + mealList7[i] + mealList8[i] + mealList9[i];
                        countList.Add(count);
                    }
                    var list = countList.ToList();
                    var MealRow1Start = 8;
                    list.ForEach(l =>
                    {
                        sheet.Cell(MealRow1Start, 6).SetValue(l);
                        MealRow1Start++;
                    });
                    var milkRowStart = 8;
                    List<int> milkList1 = new List<int>();
                    for (int i = 1; i <= schoolNameList.Count; i++)
                    {
                        int count = Convert.ToInt32(sheet1.Cell(milkRowStart, 20).Value);
                        milkList1.Add(count);
                        milkRowStart++;
                    }
                    var milkRowStart2 = 8;
                    List<int> milkList2 = new List<int>();
                    for (int i = 1; i <= schoolNameList.Count; i++)
                    {
                        int count = Convert.ToInt32(sheet1.Cell(milkRowStart2, 21).Value);
                        milkList2.Add(count);
                        milkRowStart2++;
                    }
                    var milkRowStart3 = 8;
                    List<int> milkList3 = new List<int>();
                    for (int i = 1; i <= schoolNameList.Count; i++)
                    {
                        int count = Convert.ToInt32(sheet1.Cell(milkRowStart3, 22).Value);
                        milkList3.Add(count);
                        milkRowStart3++;
                    }

                    List<int> MilkcountList = new List<int>();
                    for (int i = 0; i < schoolNameList.Count; i++)
                    {
                        int count = milkList1[i] + milkList2[i] + milkList3[i];
                        MilkcountList.Add(count);
                    }
                    var list1 = countList.ToList();
                    var MilkRow2Start = 8;
                    MilkcountList.ForEach(l =>
                    {
                        sheet.Cell(MilkRow2Start, 23).SetValue(l);
                        MilkRow2Start++;
                    });

                    var milkCount = MilkcountList.Sum(s => s);
                    var mCount = sheet.Cell(rowIndex + counter, 23).SetValue(milkCount);
                    mCount.Style.Font.SetBold(true);
                    mCount.Style.Font.SetFontSize(12.5);
                    mCount.Style.Border.OutsideBorderColor = XLColor.FromArgb(0, 0, 139);
                    mCount.Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Right);

                    var mealCount = countList.Sum(s => s);
                    var mealCount1 = sheet.Cell(rowIndex + counter, 6).SetValue(mealCount);
                    mealCount1.Style.Font.SetBold(true);
                    mealCount1.Style.Font.SetFontSize(12.5);
                    mealCount1.Style.Border.OutsideBorderColor = XLColor.FromArgb(0, 0, 139);
                    mealCount1.Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Right);

                    List<int> lunch1 = new List<int>();
                    var lunch1Start = 8;
                    for (int i = 1; i <= schoolNameList.Count; i++)
                    {
                        var count = Convert.ToInt32(sheet.Cell(lunch1Start, 7).Value);
                        lunch1.Add(count);
                        lunch1Start++;
                    }
                    var Suml1 = lunch1.Sum(s => s);
                    var Sumlunch1 = sheet.Cell(rowIndex + counter, 7).SetValue(Suml1);
                    Sumlunch1.Style.Font.SetBold(true);
                    Sumlunch1.Style.Font.SetFontSize(12.5);
                    Sumlunch1.Style.Border.OutsideBorderColor = XLColor.FromArgb(0, 0, 139);
                    Sumlunch1.Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Right);

                    List<int> lunch2 = new List<int>();
                    var lunch2Start = 8;
                    for (int i = 1; i <= schoolNameList.Count; i++)
                    {
                        var count = Convert.ToInt32(sheet.Cell(lunch2Start, 8).Value);
                        lunch2.Add(count);
                        lunch2Start++;
                    }
                    var Suml2 = lunch2.Sum(s => s);
                    var Sumlunch2 = sheet.Cell(rowIndex + counter, 8).SetValue(Suml2);
                    Sumlunch2.Style.Font.SetBold(true);
                    Sumlunch2.Style.Font.SetFontSize(12.5);
                    Sumlunch2.Style.Border.OutsideBorderColor = XLColor.FromArgb(0, 0, 139);
                    Sumlunch2.Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Right);

                    List<int> lunch3 = new List<int>();
                    var lunch3Start = 8;
                    for (int i = 1; i <= schoolNameList.Count; i++)
                    {
                        var count = Convert.ToInt32(sheet.Cell(lunch3Start, 9).Value);
                        lunch3.Add(count);
                        lunch3Start++;
                    }
                    var Suml3 = lunch3.Sum(s => s);
                    var Sumlunch3 = sheet.Cell(rowIndex + counter, 9).SetValue(Suml3);
                    Sumlunch3.Style.Font.SetBold(true);
                    Sumlunch3.Style.Font.SetFontSize(12.5);
                    Sumlunch3.Style.Border.OutsideBorderColor = XLColor.FromArgb(0, 0, 139);
                    Sumlunch3.Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Right);

                    List<int> lunch4 = new List<int>();
                    var lunch4Start = 8;
                    for (int i = 1; i <= schoolNameList.Count; i++)
                    {
                        var count = Convert.ToInt32(sheet.Cell(lunch4Start, 10).Value);
                        lunch4.Add(count);
                        lunch4Start++;
                    }
                    var Suml4 = lunch4.Sum(s => s);
                    var Sumlunch4 = sheet.Cell(rowIndex + counter, 10).SetValue(Suml4);
                    Sumlunch4.Style.Font.SetBold(true);
                    Sumlunch4.Style.Font.SetFontSize(12.5);
                    Sumlunch4.Style.Border.OutsideBorderColor = XLColor.FromArgb(0, 0, 139);
                    Sumlunch4.Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Right);

                    List<int> lunch5 = new List<int>();
                    var lunch5Start = 8;
                    for (int i = 1; i <= schoolNameList.Count; i++)
                    {
                        var count = Convert.ToInt32(sheet.Cell(lunch5Start, 11).Value);
                        lunch5.Add(count);
                        lunch5Start++;
                    }
                    var Suml5 = lunch5.Sum(s => s);
                    var Sumlunch5 = sheet.Cell(rowIndex + counter, 11).SetValue(Suml5);
                    Sumlunch5.Style.Font.SetBold(true);
                    Sumlunch5.Style.Font.SetFontSize(12.5);
                    Sumlunch5.Style.Border.OutsideBorderColor = XLColor.FromArgb(0, 0, 139);
                    Sumlunch5.Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Right);

                    List<int> vegetarian6 = new List<int>();
                    var vegetarian6Start = 8;
                    for (int i = 1; i <= schoolNameList.Count; i++)
                    {
                        var count = Convert.ToInt32(sheet.Cell(vegetarian6Start, 12).Value);
                        vegetarian6.Add(count);
                        vegetarian6Start++;
                    }
                    var vegetarian6Sum = vegetarian6.Sum(s => s);
                    var vegetarian = sheet.Cell(rowIndex + counter, 12).SetValue(vegetarian6Sum);
                    vegetarian.Style.Font.SetBold(true);
                    vegetarian.Style.Font.SetFontSize(12.5);
                    vegetarian.Style.Border.OutsideBorderColor = XLColor.FromArgb(0, 0, 139);
                    vegetarian.Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Right);

                    List<int> soymilk7 = new List<int>();
                    var soymilk7Start = 8;
                    for (int i = 1; i <= schoolNameList.Count; i++)
                    {
                        var count = Convert.ToInt32(sheet.Cell(soymilk7Start, 13).Value);
                        soymilk7.Add(count);
                        soymilk7Start++;
                    }
                    var soymilk7Sum = soymilk7.Sum(s => s);
                    var soymilk = sheet.Cell(rowIndex + counter, 13).SetValue(soymilk7Sum);
                    soymilk.Style.Font.SetBold(true);
                    soymilk.Style.Font.SetFontSize(12.5);
                    soymilk.Style.Border.OutsideBorderColor = XLColor.FromArgb(0, 0, 139);
                    soymilk.Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Right);

                    List<int> bbp8 = new List<int>();
                    var bbp8Start = 8;
                    for (int i = 1; i <= schoolNameList.Count; i++)
                    {
                        var count = Convert.ToInt32(sheet.Cell(bbp8Start, 14).Value);
                        bbp8.Add(count);
                        bbp8Start++;
                    }
                    var bbp8Sum = bbp8.Sum(s => s);
                    var bbp = sheet.Cell(rowIndex + counter, 14).SetValue(bbp8Sum);
                    bbp.Style.Font.SetBold(true);
                    bbp.Style.Font.SetFontSize(12.5);
                    bbp.Style.Border.OutsideBorderColor = XLColor.FromArgb(0, 0, 139);
                    bbp.Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Right);

                    List<int> pickup9 = new List<int>();
                    var pickup9Start = 8;
                    for (int i = 1; i <= schoolNameList.Count; i++)
                    {
                        var count = Convert.ToInt32(sheet.Cell(pickup9Start, 15).Value);
                        pickup9.Add(count);
                        pickup9Start++;
                    }
                    var pickup9Sum = pickup9.Sum(s => s);
                    var pickup = sheet.Cell(rowIndex + counter, 15).SetValue(pickup9Sum);
                    pickup.Style.Font.SetBold(true);
                    pickup.Style.Font.SetFontSize(12.5);
                    pickup.Style.Border.OutsideBorderColor = XLColor.FromArgb(0, 0, 139);
                    pickup.Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Right);

                    List<int> pizza10 = new List<int>();
                    var pizza10Start = 8;
                    for (int i = 1; i <= schoolNameList.Count; i++)
                    {
                        var count = Convert.ToInt32(sheet.Cell(pizza10Start, 16).Value);
                        pizza10.Add(count);
                        pizza10Start++;
                    }
                    var pizza10Sum = pizza10.Sum(s => s);
                    var pizza = sheet.Cell(rowIndex + counter, 16).SetValue(pizza10Sum);
                    pizza.Style.Font.SetBold(true);
                    pizza.Style.Font.SetFontSize(12.5);
                    pizza.Style.Border.OutsideBorderColor = XLColor.FromArgb(0, 0, 139);
                    pizza.Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Right);

                    List<int> veglunch = new List<int>();
                    var veglunch1 = 8;
                    for (int i = 1; i <= schoolNameList.Count; i++)
                    {
                        var count = Convert.ToInt32(sheet.Cell(veglunch1, 17).Value);
                        veglunch.Add(count);
                        veglunch1++;
                    }
                    var veglunchSum = veglunch.Sum(s => s);
                    var veglunch2 = sheet.Cell(rowIndex + counter, 17).SetValue(veglunchSum);
                    veglunch2.Style.Font.SetBold(true);
                    veglunch2.Style.Font.SetFontSize(12.5);
                    veglunch2.Style.Border.OutsideBorderColor = XLColor.FromArgb(0, 0, 139);
                    veglunch2.Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Right);

                    List<int> wmilk = new List<int>();
                    var wmilk1 = 8;
                    for (int i = 1; i <= schoolNameList.Count; i++)
                    {
                        var count = Convert.ToInt32(sheet.Cell(wmilk1, 20).Value);
                        wmilk.Add(count);
                        wmilk1++;
                    }
                    var wmilksum = wmilk.Sum(s => s);
                    var wmilksum1 = sheet.Cell(rowIndex + counter, 20).SetValue(wmilksum);
                    wmilksum1.Style.Font.SetBold(true);
                    wmilksum1.Style.Font.SetFontSize(12.5);
                    wmilksum1.Style.Border.OutsideBorderColor = XLColor.FromArgb(0, 0, 139);
                    wmilksum1.Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Right);

                    List<int> wfmilk = new List<int>();
                    var wmilk2 = 8;
                    for (int i = 1; i <= schoolNameList.Count; i++)
                    {
                        var count = Convert.ToInt32(sheet.Cell(wmilk2, 21).Value);
                        wfmilk.Add(count);
                        wmilk2++;
                    }
                    var wmilk2Sum = wfmilk.Sum(s => s);
                    var wfmilk1 = sheet.Cell(rowIndex + counter, 21).SetValue(wmilk2Sum);
                    wfmilk1.Style.Font.SetBold(true);
                    wfmilk1.Style.Font.SetFontSize(12.5);
                    wfmilk1.Style.Border.OutsideBorderColor = XLColor.FromArgb(0, 0, 139);
                    wfmilk1.Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Right);

                    List<int> fcmilk = new List<int>();
                    var fcmilk1 = 8;
                    for (int i = 1; i <= schoolNameList.Count; i++)
                    {
                        var count = Convert.ToInt32(sheet.Cell(fcmilk1, 22).Value);
                        fcmilk.Add(count);
                        fcmilk1++;
                    }
                    var fcmilksum = fcmilk.Sum(s => s);
                    var fcmilksum1 = sheet.Cell(rowIndex + counter, 22).SetValue(fcmilksum);
                    fcmilksum1.Style.Font.SetBold(true);
                    fcmilksum1.Style.Font.SetFontSize(12.5);
                    fcmilksum1.Style.Border.OutsideBorderColor = XLColor.FromArgb(0, 0, 139);
                    fcmilksum1.Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Right);

                    List<int> snacklist = new List<int>();
                    var snacklist1 = 8;
                    for (int i = 1; i <= schoolNameList.Count; i++)
                    {
                        var count = Convert.ToInt32(sheet.Cell(snacklist1, 24).Value);
                        snacklist.Add(count);
                        snacklist1++;
                    }
                    var snacklistsum = snacklist.Sum(s => s);
                    var snacklistsum1 = sheet.Cell(rowIndex + counter, 24).SetValue(snacklistsum);
                    snacklistsum1.Style.Font.SetBold(true);
                    snacklistsum1.Style.Font.SetFontSize(12.5);
                    snacklistsum1.Style.Border.OutsideBorderColor = XLColor.FromArgb(0, 0, 139);
                    snacklistsum1.Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Right);

                    List<int> snackOption = new List<int>();
                    var snackOp1 = 8;
                    for (int i = 1; i <= schoolNameList.Count; i++)
                    {
                        var count = Convert.ToInt32(sheet.Cell(snackOp1, 25).Value);
                        snackOption.Add(count);
                        snackOp1++;
                    }
                    var snackOptionSum = snackOption.Sum(s => s);
                    var snackOptionSum1 = sheet.Cell(rowIndex + counter, 25).SetValue(snackOptionSum);
                    snackOptionSum1.Style.Font.SetBold(true);
                    snackOptionSum1.Style.Font.SetFontSize(12.5);
                    snackOptionSum1.Style.Border.OutsideBorderColor = XLColor.FromArgb(0, 0, 139);
                    snackOptionSum1.Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Right);

                    List<int> SackLunch = new List<int>();
                    var SackLunchStart = 8;
                    for (int i = 1; i <= schoolNameList.Count; i++)
                    {
                        var count = Convert.ToInt32(sheet.Cell(SackLunchStart, 26).Value);
                        SackLunch.Add(count);
                        SackLunchStart++;
                    }
                    var SackLunch1Sum = SackLunch.Sum(s => s);
                    var SackLunch2Sum = sheet.Cell(rowIndex + counter, 26).SetValue(SackLunch1Sum);
                    SackLunch2Sum.Style.Font.SetBold(true);
                    SackLunch2Sum.Style.Font.SetFontSize(12.5);
                    SackLunch2Sum.Style.Border.OutsideBorderColor = XLColor.FromArgb(0, 0, 139);
                    SackLunch2Sum.Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Right);

                    List<int> SackLunchVeg = new List<int>();
                    var SackLunchStart1 = 8;
                    for (int i = 1; i <= schoolNameList.Count; i++)
                    {
                        var count = Convert.ToInt32(sheet.Cell(SackLunchStart1, 27).Value);
                        SackLunchVeg.Add(count);
                        SackLunchStart1++;
                    }
                    var SackLunchSum = SackLunchVeg.Sum(s => s);
                    var SackLunchSum1 = sheet.Cell(rowIndex + counter, 27).SetValue(SackLunchSum);
                    SackLunchSum1.Style.Font.SetBold(true);
                    SackLunchSum1.Style.Font.SetFontSize(12.5);
                    SackLunchSum1.Style.Border.OutsideBorderColor = XLColor.FromArgb(0, 0, 139);
                    SackLunchSum1.Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Right);




                    workBook.SaveAs(filePath1);
                    response.FileName = filePath1;
                });
        }

        public ReportResponse DailyBreakfastReport(OrderDayPropItemRequest request)
        {
            return Execute<OrderDayPropItemRequest, ReportResponse>(
                request,
                response =>
                {
                    var orderDayDate = Convert.ToDateTime(request.Filter.OrderDate);
                    var orderItems = _mealMenuOrderFacade.GetDailyBreakfastReport(request.Filter);
                    var path = AppDomain.CurrentDomain.BaseDirectory;
                    var filePath = Path.Combine(path, "Templates\\DAILYBREAKFASTREPORT.xlsx");
                    var workBook = new XLWorkbook(filePath);
                    var sheet = workBook.Worksheet(1);
                    var meals = Lookups.MealTypeShortList.OrderBy(d => d.Id).ToList();
                    var rowIndex = 6;
                    var rowSchool = 6;
                    var colIndex = 2;
                    var counter = 0;
                    var schoolList = orderItems.GroupBy(d => new { d.SchoolName, d.TotalCount, d.MenuName, d.menuType }).Select(d => new
                    {
                        totalCount = d.Key.TotalCount,
                        SchoolName = d.Key.SchoolName,
                        menuName = d.Key.MenuName,
                        MenuType = d.Key.menuType
                    }).ToList();

                    var menuItem = orderItems.GroupBy(d => new { d.id, d.MealType, d.FruitCount, d.VegetableCount, d.SchoolName, d.TotalCount, d.MenuName, d.validate, d.menuType }).Select(d => new
                    {
                        menuId = d.Key.id,
                        menuName = d.Key.MenuName,
                        totalCount = d.Key.TotalCount,
                        fruitCount = d.Key.FruitCount,
                        vegCount = d.Key.VegetableCount,
                        validdate = d.Key.validate,
                        menuTypeId = d.Key.menuType,
                        SchoolName = d.Key.SchoolName,
                        MealType = d.Key.MealType
                    }).ToList();
                    var schoolNames = orderItems.GroupBy(d => new { d.SchoolName }).Distinct().ToList();

                    var schoolNameList = orderItems.GroupBy(d => new { d.SchoolName, d.Route, d.ServiceType, d.BreakFast, d.Type }).Distinct().ToList();
                    schoolNameList.ForEach(m =>
                    {
                        sheet.Cell(rowIndex + counter, 5).SetValue(m.Key.SchoolName);
                        sheet.Cell(rowIndex + counter, 10).SetValue(0);
                        sheet.Cell(rowIndex + counter, 11).SetValue(0);
                        sheet.Cell(rowIndex + counter, 12).SetValue(0);
                        sheet.Cell(rowIndex + counter, 8).SetValue(0);
                        sheet.Cell(rowIndex + counter, 9).SetValue(0);
                        sheet.Cell(rowIndex + counter, 1).SetValue(Convert.ToInt32(m.Key.Route));
                        sheet.Cell(rowIndex + counter, 2).SetValue(m.Key.ServiceType);
                        sheet.Cell(rowIndex + counter, 3).SetValue(m.Key.BreakFast);
                        sheet.Cell(rowIndex + counter, 4).SetValue(m.Key.Type);
                        counter++;
                    });
                    var OrderDate = Convert.ToDateTime(orderDayDate.ToShortDateString());
                    var orderDay = OrderDate.Day;
                    var orderMonth = OrderDate.ToString("MMM");
                    var orderYear = OrderDate.ToString("yy");
                    var reportDate = DateTime.Now;
                    sheet.Cell(2, 1).SetValue("Report Date : " + reportDate);
                    sheet.Cell(2, 6).SetValue(orderDay + "-" + orderMonth + "-" + orderYear);

                    int days;
                    var rowDays = 7;
                    for (days = 1; days <= 11; days++)
                    {
                        if (days != 7 && days != 8)
                        {
                            sheet.Cell(4, rowDays).SetValue(orderDay);
                            sheet.Cell(3, rowDays).SetValue(orderMonth);
                        }
                        rowDays++;
                    }

                    var Bfday = OrderDate.ToShortDateString();
                    sheet.Cell(3, 5).SetValue(orderMonth + " " + Bfday + " BK");

                    var Total = sheet.Cell(rowIndex + counter, 5).SetValue("Total");
                    Total.Style.Font.SetBold(true);
                    Total.Style.Font.SetFontSize(12.5);
                    Total.Style.Border.OutsideBorderColor = XLColor.FromArgb(0, 0, 139);
                    Total.Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Right);


                    var orderRowStart = 6;
                    var orderRow1Start = 6;
                    var orderColumnStart = 23;
                    for (var day = orderDayDate.Day; day <= orderDayDate.Day; day++)
                    {
                        var currentDay = new DateTime(orderDayDate.Year, orderDayDate.Month, day);
                        if (currentDay.DayOfWeek == DayOfWeek.Saturday ||
                            currentDay.DayOfWeek == DayOfWeek.Sunday)
                            continue;

                        var snackCount = meals.ToList();
                        var BreakfastOption1 = menuItem.Where(m => m.validdate == currentDay && m.menuTypeId == (int)MenuTypes.Breakfast1).Select(d => new { d.totalCount, d.SchoolName }).ToList();
                        var BreakfastOption2 = menuItem.Where(m => m.validdate == currentDay && m.menuTypeId == (int)MenuTypes.Breakfast2).Select(d => new { d.totalCount, d.SchoolName }).ToList();
                        var BreakfastOption3 = menuItem.Where(m => m.validdate == currentDay && m.menuTypeId == (int)MenuTypes.Breakfast3).Select(d => new { d.totalCount, d.SchoolName }).ToList();
                        var BreakfastOption4 = menuItem.Where(m => m.validdate == currentDay && m.menuTypeId == (int)MenuTypes.Breakfast4).Select(d => new { d.totalCount, d.SchoolName }).ToList();
                        var Vegetarian = menuItem.Where(m => m.validdate == currentDay && m.menuTypeId == (int)MenuTypes.Vegetarian).Select(d => d.totalCount).ToList();
                        var SoyMilk = menuItem.Where(m => m.validdate == currentDay && m.menuTypeId == (int)MenuTypes.SoyMilk).Select(d => d.totalCount).ToList();

                        List<long?> orderCountbf1 = new List<long?>();
                        schoolNameList.ForEach(sn => {
                            List<int> orderValues = new List<int>();
                            BreakfastOption1.ForEach(bf1 =>
                            {
                                if (sn.Key.SchoolName == bf1.SchoolName)
                                {
                                    int totalOrder = Convert.ToInt32(bf1.totalCount);
                                    orderValues.Add(totalOrder);
                                }
                            });
                            int CountOrder = 0;
                            if (orderValues.Count == 1)
                            {
                                CountOrder = Convert.ToInt32(orderValues[0]);
                            }
                            if (orderValues.Count == 2)
                            {
                                CountOrder = Convert.ToInt32(orderValues[0] + orderValues[1]);
                            }
                            orderCountbf1.Add(CountOrder);
                        });

                        List<long?> orderCountbf2 = new List<long?>();
                        schoolNameList.ForEach(sn => {
                            List<int> orderValues = new List<int>();
                            BreakfastOption2.ForEach(bf1 =>
                            {
                                if (sn.Key.SchoolName == bf1.SchoolName)
                                {
                                    int totalOrder = Convert.ToInt32(bf1.totalCount);
                                    orderValues.Add(totalOrder);
                                }
                            });
                            int CountOrder = 0;
                            if (orderValues.Count == 1)
                            {
                                CountOrder = Convert.ToInt32(orderValues[0]);
                            }
                            if (orderValues.Count == 2)
                            {
                                CountOrder = Convert.ToInt32(orderValues[0] + orderValues[1]);
                            }
                            orderCountbf2.Add(CountOrder);
                        });
                        List<long?> orderCountbf3 = new List<long?>();
                        schoolNameList.ForEach(sn => {
                            List<int> orderValues = new List<int>();
                            BreakfastOption3.ForEach(bf1 =>
                            {
                                if (sn.Key.SchoolName == bf1.SchoolName)
                                {
                                    int totalOrder = Convert.ToInt32(bf1.totalCount);
                                    orderValues.Add(totalOrder);
                                }
                            });
                            int CountOrder = 0;
                            if (orderValues.Count == 1)
                            {
                                CountOrder = Convert.ToInt32(orderValues[0]);
                            }
                            if (orderValues.Count == 2)
                            {
                                CountOrder = Convert.ToInt32(orderValues[0] + orderValues[1]);
                            }
                            orderCountbf3.Add(CountOrder);
                        });
                        List<long?> orderCountbf4 = new List<long?>();
                        schoolNameList.ForEach(sn => {
                            List<int> orderValues = new List<int>();
                            BreakfastOption4.ForEach(bf1 =>
                            {
                                if (sn.Key.SchoolName == bf1.SchoolName)
                                {
                                    int totalOrder = Convert.ToInt32(bf1.totalCount);
                                    orderValues.Add(totalOrder);
                                }
                            });
                            int CountOrder = 0;
                            if (orderValues.Count == 1)
                            {
                                CountOrder = Convert.ToInt32(orderValues[0]);
                            }
                            if (orderValues.Count == 2)
                            {
                                CountOrder = Convert.ToInt32(orderValues[0] + orderValues[1]);
                            }
                            orderCountbf4.Add(CountOrder);
                        });

                        var breakfast1 = orderCountbf1.Sum(s => s);
                        var bfast1 = sheet.Cell(rowIndex + counter, 7).SetValue(breakfast1);
                        bfast1.Style.Font.SetBold(true);
                        bfast1.Style.Font.SetFontSize(12.5);
                        bfast1.Style.Border.OutsideBorderColor = XLColor.FromArgb(0, 0, 139);
                        bfast1.Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Right);

                        var vegetarian1 = Vegetarian.Sum(s => s).Value;
                        var Veg1 = sheet.Cell(rowIndex + counter, 8).SetValue(vegetarian1);
                        Veg1.Style.Font.SetBold(true);
                        Veg1.Style.Font.SetFontSize(12.5);
                        Veg1.Style.Border.OutsideBorderColor = XLColor.FromArgb(0, 0, 139);
                        Veg1.Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Right);

                        var breakfast2 = orderCountbf2.Sum(s => s);
                        var bfast2 = sheet.Cell(rowIndex + counter, 9).SetValue(breakfast2);
                        bfast2.Style.Font.SetBold(true);
                        bfast2.Style.Font.SetFontSize(12.5);
                        bfast2.Style.Border.OutsideBorderColor = XLColor.FromArgb(0, 0, 139);
                        bfast2.Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Right);

                        var breakfast3 = orderCountbf3.Sum(s => s).Value;
                        var bfast3 = sheet.Cell(rowIndex + counter, 10).SetValue(breakfast3);
                        bfast3.Style.Font.SetBold(true);
                        bfast3.Style.Font.SetFontSize(12.5);
                        bfast3.Style.Border.OutsideBorderColor = XLColor.FromArgb(0, 0, 139);
                        bfast3.Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Right);

                        var breakfast4 = orderCountbf4.Sum(s => s).Value;
                        var bfast4 = sheet.Cell(rowIndex + counter, 11).SetValue(breakfast4);
                        bfast4.Style.Font.SetBold(true);
                        bfast4.Style.Font.SetFontSize(12.5);
                        bfast4.Style.Border.OutsideBorderColor = XLColor.FromArgb(0, 0, 139);
                        bfast4.Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Right);

                        var soyMilk1 = SoyMilk.Sum(s => s).Value;
                        var smilk1 = sheet.Cell(rowIndex + counter, 12).SetValue(soyMilk1);
                        smilk1.Style.Font.SetBold(true);
                        smilk1.Style.Font.SetFontSize(12.5);
                        smilk1.Style.Border.OutsideBorderColor = XLColor.FromArgb(0, 0, 139);
                        smilk1.Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Right);

                        var juice = sheet.Cell(rowIndex + counter, 13).SetValue("");
                        juice.Style.Font.SetBold(true);
                        juice.Style.Font.SetFontSize(12.5);
                        juice.Style.Border.OutsideBorderColor = XLColor.FromArgb(0, 0, 139);
                        juice.Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Right);

                        var fruit = sheet.Cell(rowIndex + counter, 14).SetValue("");
                        fruit.Style.Font.SetBold(true);
                        fruit.Style.Font.SetFontSize(12.5);
                        fruit.Style.Border.OutsideBorderColor = XLColor.FromArgb(0, 0, 139);
                        fruit.Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Right);

                        var bf1orderRowStart = 6;
                        var bf2orderRowStart = 6;
                        var bf3orderRowStart = 6;
                        var bf4orderRowStart = 6;
                        orderCountbf1.ForEach(lo1 =>
                        {
                            sheet.Cell(bf1orderRowStart, 7).SetValue(lo1.Value);
                            sheet.Cell(bf1orderRowStart, 7).Style.Border.OutsideBorderColor = XLColor.LightBlue;
                            bf1orderRowStart++;
                        });
                        orderCountbf2.ForEach(lo1 =>
                        {
                            sheet.Cell(bf2orderRowStart, 9).SetValue(lo1.Value);
                            sheet.Cell(bf2orderRowStart, 9).Style.Border.OutsideBorderColor = XLColor.LightBlue;
                            bf2orderRowStart++;
                        });
                        orderCountbf3.ForEach(lo1 =>
                        {
                            sheet.Cell(bf3orderRowStart, 10).SetValue(lo1.Value);
                            sheet.Cell(bf3orderRowStart, 10).Style.Border.OutsideBorderColor = XLColor.LightBlue;
                            bf3orderRowStart++;
                        });
                        orderCountbf4.ForEach(lo1 =>
                        {
                            sheet.Cell(bf4orderRowStart, 11).SetValue(lo1.Value);
                            sheet.Cell(bf4orderRowStart, 10).Style.Border.OutsideBorderColor = XLColor.LightBlue;
                            bf4orderRowStart++;
                        });
                        var VegOrderRowStart = 6;
                        Vegetarian.ForEach(veg => {
                            sheet.Cell(VegOrderRowStart, 8).SetValue(veg.Value);
                            sheet.Cell(VegOrderRowStart, 8).Style.Border.OutsideBorderColor = XLColor.LightBlue;
                            VegOrderRowStart++;
                        });

                        //var soyMilk = 6;
                        //Vegetarian.ForEach(soy => {
                        //    sheet.Cell(soyMilk , 12).SetValue(soy.Value);
                        //    soyMilk++;
                        //});

                        orderColumnStart++;
                        var Menu1 = menuItem.Where(m => m.validdate == currentDay && m.menuTypeId == (int)MenuTypes.Milk && m.menuName == "1% White Milk")
                                            .Select(d => new { d.totalCount }).ToList();

                        var mcount1 = Menu1.Sum(s => s.totalCount).Value;
                        var MenuCount1 = sheet.Cell(rowIndex + counter, 15).SetValue(mcount1);
                        MenuCount1.Style.Font.SetBold(true);
                        MenuCount1.Style.Font.SetFontSize(12.5);
                        MenuCount1.Style.Border.OutsideBorderColor = XLColor.Black;
                        MenuCount1.Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Right);

                        Menu1.ForEach(dm =>
                        {
                            if (Menu1.Any())
                            {
                                sheet.Cell(orderRowStart, 15).SetValue(dm.totalCount.Value);
                                sheet.Cell(orderRowStart, 15).Style.Border.OutsideBorderColor = XLColor.LightBlue;
                                orderRowStart++;
                            }
                        });
                        orderColumnStart++;
                        var Menu2 = menuItem.Where(m => m.validdate == currentDay && m.menuTypeId == (int)MenuTypes.Milk && m.menuName == "Fat Free White Milk ")
                                            .Select(d => new { d.totalCount }).ToList();

                        var mcount2 = Menu2.Sum(s => s.totalCount).Value;
                        var MenuCount2 = sheet.Cell(rowIndex + counter, 16).SetValue(mcount2);
                        MenuCount2.Style.Font.SetBold(true);
                        MenuCount2.Style.Font.SetFontSize(12.5);
                        MenuCount2.Style.Border.OutsideBorderColor = XLColor.Black;
                        MenuCount2.Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Right);

                        Menu2.ForEach(dm =>
                        {
                            if (Menu2.Any())
                            {
                                sheet.Cell(orderRow1Start, 16).SetValue(dm.totalCount.Value);
                                sheet.Cell(orderRow1Start, 16).Style.Border.OutsideBorderColor = XLColor.LightBlue;
                                orderRow1Start++;
                            }
                        });

                        //List<long?> countList = new List<long?>();
                        //for(int i=0;i<Menu1.Count;i++)
                        //{
                        //    long? count = Menu1[i].totalCount + Menu2[i].totalCount;
                        //    countList.Add(count);
                        //}
                        //var list = countList.ToList();
                        //var MilkRow1Start = 6;
                        //list.ForEach(l => {
                        //    sheet.Cell(MilkRow1Start, 17).SetValue(l);
                        //    MilkRow1Start++;
                        //});
                        //var mcount = list.Sum(t => t).Value;
                        //var MilkCount = sheet.Cell(rowIndex + counter, 17).SetValue(mcount);
                        //MilkCount.Style.Font.SetBold(true);
                        //MilkCount.Style.Font.SetFontSize(12.5);
                        //MilkCount.Style.Border.OutsideBorderColor = XLColor.Black;
                        //MilkCount.Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Right);


                        //List<long?> OptionCountList = new List<long?>();
                        //for (int i = 0; i < BreakfastOption1.Count; i++)
                        //{
                        //    long? count = BreakfastOption1[i];
                        //    OptionCountList.Add(count);
                        //}
                        //var list1 = OptionCountList.ToList();
                        //var bfRow1Start = 6;
                        //OptionCountList.ForEach(l =>
                        //{
                        //    var option1 = sheet.Cell(bfRow1Start, 6).SetValue(l);
                        //    option1.Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Right);
                        //    bfRow1Start++;
                        //});
                        //var Bfcount = OptionCountList.Sum(t => t).Value;
                        //var bfCount1 = sheet.Cell(rowIndex + counter, 6).SetValue(Bfcount);
                        //bfCount1.Style.Font.SetBold(true);
                        //bfCount1.Style.Font.SetFontSize(12.5);
                        //bfCount1.Style.Border.OutsideBorderColor = XLColor.Black;
                        //bfCount1.Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Right);

                        var schoolName = menuItem.Where(item => item.menuName == "Soy Milk ONLY").Select(total => new { total.SchoolName, total.totalCount }).ToList();

                        var soyMilk = 6;
                        schoolNames.ForEach(sl => {
                            schoolName.ForEach(sn =>
                            {
                                if (sl.Key.SchoolName == sn.SchoolName)
                                {
                                    sheet.Cell(soyMilk, 12).SetValue(sn.totalCount);
                                }
                            });
                            soyMilk++;
                        });
                        var tableContent =
                        sheet.Range(6, 1, rowIndex + counter, 17).AddToNamed("tableContent");
                        tableContent.Style.Border.InsideBorderColor = XLColor.FromArgb(0, 0, 221);
                        tableContent.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
                        tableContent.Style.Border.OutsideBorderColor = XLColor.FromArgb(0, 0, 139);
                        tableContent.Style.Border.OutsideBorder = XLBorderStyleValues.Medium;
                    }
                    var directory = String.Format(path + "Documents\\DAILYBREAKFASTREPORT.xlsx");
                    if (!Directory.Exists(directory))
                        workBook.SaveAs(directory);

                    var filePath1 = Path.Combine(path, "Documents\\DAILYBREAKFASTREPORT.xlsx");
                    var workBook1 = new XLWorkbook(filePath1);
                    var sheet1 = workBook.Worksheet(1);


                    var milkRowStart = 6;
                    List<int> milkList1 = new List<int>();
                    for (int i = 1; i <= schoolNameList.Count; i++)
                    {
                        int count = Convert.ToInt32(sheet1.Cell(milkRowStart, 15).Value);
                        milkList1.Add(count);
                        milkRowStart++;
                    }
                    var milkRowStart2 = 6;
                    List<int> milkList2 = new List<int>();
                    for (int i = 1; i <= schoolNameList.Count; i++)
                    {
                        int count = Convert.ToInt32(sheet1.Cell(milkRowStart2, 16).Value);
                        milkList2.Add(count);
                        milkRowStart2++;
                    }

                    List<int> MilkcountList = new List<int>();
                    for (int i = 0; i < schoolNameList.Count; i++)
                    {
                        int count = milkList1[i] + milkList2[i];
                        MilkcountList.Add(count);
                    }
                    var MilkRow2Start = 6;
                    MilkcountList.ForEach(l => {
                        sheet1.Cell(MilkRow2Start, 17).SetValue(l);
                        MilkRow2Start++;
                    });

                    var milkCount = MilkcountList.Sum(s => s);
                    var mCount = sheet1.Cell(rowIndex + counter, 17).SetValue(milkCount);
                    mCount.Style.Font.SetBold(true);
                    mCount.Style.Font.SetFontSize(12.5);
                    mCount.Style.Border.OutsideBorderColor = XLColor.FromArgb(0, 0, 139);
                    mCount.Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Right);

                    var MealRowStart = 6;
                    List<int> MealList1 = new List<int>();
                    for (int i = 1; i <= schoolNameList.Count; i++)
                    {
                        int count = Convert.ToInt32(sheet1.Cell(MealRowStart, 7).Value);
                        MealList1.Add(count);
                        MealRowStart++;
                    }
                    var MealRowStart1 = 6;
                    List<int> MealList2 = new List<int>();
                    for (int i = 1; i <= schoolNameList.Count; i++)
                    {
                        int count = Convert.ToInt32(sheet1.Cell(MealRowStart1, 8).Value);
                        MealList2.Add(count);
                        MealRowStart1++;
                    }
                    var MealRowStart2 = 6;
                    List<int> MealList3 = new List<int>();
                    for (int i = 1; i <= schoolNameList.Count; i++)
                    {
                        int count = Convert.ToInt32(sheet1.Cell(MealRowStart2, 9).Value);
                        MealList3.Add(count);
                        MealRowStart2++;
                    }
                    var MealRowStart3 = 6;
                    List<int> MealList4 = new List<int>();
                    for (int i = 1; i <= schoolNameList.Count; i++)
                    {
                        int count = Convert.ToInt32(sheet1.Cell(MealRowStart3, 11).Value);
                        MealList4.Add(count);
                        MealRowStart3++;
                    }

                    var MealRowStart4 = 6;
                    List<int> MealList5 = new List<int>();
                    for (int i = 1; i <= schoolNameList.Count; i++)
                    {
                        int count = Convert.ToInt32(sheet1.Cell(MealRowStart4, 10).Value);
                        MealList5.Add(count);
                        MealRowStart4++;
                    }

                    List<int> MealkcountList = new List<int>();
                    for (int i = 0; i < schoolNameList.Count; i++)
                    {
                        int count = MealList1[i] + MealList2[i] + MealList3[i] + MealList4[i] + MealList5[i];
                        MealkcountList.Add(count);
                    }
                    var MealRow2Start = 6;
                    MealkcountList.ForEach(l => {
                        sheet1.Cell(MealRow2Start, 6).SetValue(l);
                        MealRow2Start++;
                    });

                    var mealCount = MealkcountList.Sum(s => s);
                    var mealCount1 = sheet1.Cell(rowIndex + counter, 6).SetValue(mealCount);
                    mealCount1.Style.Font.SetBold(true);
                    mealCount1.Style.Font.SetFontSize(12.5);
                    mealCount1.Style.Border.OutsideBorderColor = XLColor.FromArgb(0, 0, 139);
                    mealCount1.Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Right);

                    workBook.SaveAs(filePath1);
                    response.FileName = filePath1;
                });
        }


        public ReportResponse OrderDayPropItemExport(OrderDayPropItemRequest request)
        {
            return Execute<OrderDayPropItemRequest, ReportResponse>(
                request,
                response =>
                {
                    var orderItems = _mealMenuOrderFacade.GetDailyItemsReport(request.Filter);
                    var fileRepository = FileRepositoryPath; //_appSetting.GetSetting<string>("FileRepositoryPath");
                    var dayPropItemReport = Path.Combine(fileRepository, "DayPropItemReport");
                    if (!Directory.Exists(dayPropItemReport))
                        Directory.CreateDirectory(Path.Combine(fileRepository, "DayPropItemReport"));
                    var startDate = request.Filter.OrderDate ?? new DateTime(DateTime.Now.Year, DateTime.Now.Month, 1);
                    var endDate = new DateTime(startDate.Year, startDate.Month, DateTime.DaysInMonth(startDate.Year, startDate.Month));
                    var filePath = Path.Combine(dayPropItemReport,
                        string.Format("{0}_{1}_FruitandVegetableReport.xlsx", startDate.ToString("yyyyMMdd"),
                            endDate.ToString("yyyyMMdd")));
                    if (File.Exists(filePath))
                        File.Delete(filePath);
                    using (var workBook = new XLWorkbook(XLEventTracking.Disabled))
                    {
                        var wsFruit = workBook.AddWorksheet("FruitReport");
                        var wsVeg = workBook.AddWorksheet("VegetableReport");
                        var meals = Lookups.MealTypeShortList.OrderBy(d => d.Id).ToList();
                        var rowIndex = 5;
                        var colIndex = 6;
                        var counter = 0;
                        var schoolList = orderItems.GroupBy(d => new { d.SchoolName, d.LunchOVS, d.Route, d.ServiceType, d.Type }).Select(d => new {
                            SchoolName = d.Key.SchoolName,
                            ServiceType = d.Key.ServiceType,
                            OVS = d.Key.LunchOVS,
                            route = d.Key.Route,
                            type = d.Key.Type,
                            FruitCount = d.Sum(k => k.FruitCount ?? 0),
                            VegetableCount = d.Sum(k => k.VegetableCount ?? 0)
                        }).ToList();
                        for (var day = 1; day <= endDate.Day; day++)
                        {
                            var tableDay = new DateTime(endDate.Year, endDate.Month, day);
                            if (tableDay.DayOfWeek == DayOfWeek.Saturday || tableDay.DayOfWeek == DayOfWeek.Sunday)
                                continue;
                            var fruitDayRange = wsFruit.Range(rowIndex - 1, colIndex + counter, rowIndex - 1, colIndex + counter + meals.Count - 1);
                            fruitDayRange.Merge().AddToNamed("dayRange");
                            fruitDayRange.SetValue(day);

                            var vegDayRange = wsVeg.Range(rowIndex - 1, colIndex + counter, rowIndex - 1, colIndex + counter + meals.Count - 1);
                            vegDayRange.Merge().AddToNamed("dayRange");
                            vegDayRange.SetValue(day);
                            meals.ForEach(mm =>
                            {

                                wsFruit.Cell(rowIndex, colIndex + counter).SetValue(mm.Text);
                                wsVeg.Cell(rowIndex, colIndex + counter).SetValue(mm.Text);
                                counter++;
                            });
                        }

                        //counter++;
                        wsFruit.Cell(rowIndex, colIndex + counter).SetValue("Total Count");
                        wsFruit.Range(rowIndex + 1, colIndex, rowIndex + 1 + schoolList.Count, colIndex + counter).AddToNamed("numberFormat");


                        wsVeg.Cell(rowIndex, colIndex + counter).SetValue("Total Count");
                        wsVeg.Range(rowIndex + 1, colIndex, rowIndex + 1 + schoolList.Count, colIndex + counter).AddToNamed("numberFormat");



                        wsFruit.Cell(rowIndex, 1).SetValue("Rt");
                        wsVeg.Cell(rowIndex, 1).SetValue("Rt");

                        wsFruit.Cell(rowIndex, 2).SetValue("Service");
                        wsVeg.Cell(rowIndex, 2).SetValue("Service");

                        wsFruit.Cell(rowIndex, 3).SetValue("OVS");
                        wsVeg.Cell(rowIndex, 3).SetValue("OVS");

                        wsFruit.Cell(rowIndex, 4).SetValue("Type");
                        wsVeg.Cell(rowIndex, 4).SetValue("Type");

                        wsFruit.Cell(rowIndex, 5).SetValue("School");
                        wsVeg.Cell(rowIndex, 5).SetValue("School");

                        wsFruit.Range(rowIndex, 1, rowIndex, colIndex + counter + 4).AddToNamed("tableHeaderRange");
                        wsFruit.Range(rowIndex + 1, colIndex, rowIndex + schoolList.Count, colIndex + counter).AddToNamed("numberFormat");

                        wsVeg.Range(rowIndex, 1, rowIndex, colIndex + counter + 4).AddToNamed("tableHeaderRange");
                        wsVeg.Range(rowIndex + 1, colIndex, rowIndex + schoolList.Count, colIndex + counter + 4).AddToNamed("numberFormat");


                        wsFruit.Range(rowIndex, 1, rowIndex + schoolList.Count, 2 + counter + 4).AddToNamed("tableContent");
                        wsVeg.Range(rowIndex, 1, rowIndex + schoolList.Count, 2 + counter + 4).AddToNamed("tableContent");

                        wsFruit.Range(rowIndex - 2, 1, rowIndex - 2, 2 + counter).Merge().AddToNamed("reportHeader").SetValue(string.Format("FRUIT ITEMS , {0} - {1}",
                            startDate.ToString("yyyy-MM-dd"), endDate.ToString("yyyy-MM-dd")));
                        wsVeg.Range(rowIndex - 2, 1, rowIndex - 2, 2 + counter).Merge().AddToNamed("reportHeader").SetValue(string.Format("VEGETABLE ITEMS , {0} - {1}",
                                startDate.ToString("yyyy-MM-dd"), endDate.ToString("yyyy-MM-dd")));

                        var rowItemIndex = 6;

                        schoolList.ForEach(oi =>
                        {
                            wsFruit.Cell(rowItemIndex, 1).SetValue(oi.route);
                            wsVeg.Cell(rowItemIndex, 1).SetValue(oi.route);

                            wsFruit.Cell(rowItemIndex, 2).SetValue(oi.ServiceType);
                            wsVeg.Cell(rowItemIndex, 2).SetValue(oi.ServiceType);

                            wsFruit.Cell(rowItemIndex, 3).SetValue(oi.OVS);
                            wsVeg.Cell(rowItemIndex, 3).SetValue(oi.OVS);

                            wsFruit.Cell(rowItemIndex, 4).SetValue(oi.type);
                            wsVeg.Cell(rowItemIndex, 4).SetValue(oi.type);

                            wsFruit.Cell(rowItemIndex, 5).SetValue(oi.SchoolName);
                            wsVeg.Cell(rowItemIndex, 5).SetValue(oi.SchoolName);

                            var colItemIndex = 6;

                            for (var day = 1; day <= endDate.Day; day++)
                            {
                                var tableDay = new DateTime(endDate.Year, endDate.Month, day);
                                if (tableDay.DayOfWeek == DayOfWeek.Saturday || tableDay.DayOfWeek == DayOfWeek.Sunday)
                                    continue;
                                meals.ForEach(m =>
                                {
                                    var item = orderItems.FirstOrDefault(d => d.SchoolName == oi.SchoolName && d.OrderDay == tableDay && d.MealType.Id == m.Id);
                                    if (item != null)
                                    {
                                        wsFruit.Cell(rowItemIndex, colItemIndex).SetValue(item.FruitCount);
                                        wsVeg.Cell(rowItemIndex, colItemIndex).SetValue(item.VegetableCount);
                                    }
                                    colItemIndex++;
                                });
                            }
                            wsFruit.Cell(rowItemIndex, colItemIndex).SetValue(oi.FruitCount);
                            wsVeg.Cell(rowItemIndex, colItemIndex).SetValue(oi.VegetableCount);
                            rowItemIndex++;

                        });
                        var reportHeaderRanges = workBook.NamedRanges.NamedRange("reportHeader");
                        if (reportHeaderRanges != null)
                        {
                            reportHeaderRanges.Ranges.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                            reportHeaderRanges.Ranges.Style.Font.Bold = true;
                            reportHeaderRanges.Ranges.Style.Font.FontSize = 16;
                        }

                        var mealTypeRanges = workBook.NamedRanges.NamedRange("dayRange");
                        if (mealTypeRanges != null)
                        {
                            mealTypeRanges.Ranges.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                            mealTypeRanges.Ranges.Style.Font.Bold = true;
                            mealTypeRanges.Ranges.Style.Font.FontSize = 14;
                        }

                        var menuTypeRanges = workBook.NamedRanges.NamedRange("tableHeaderRange");
                        if (menuTypeRanges != null)
                        {
                            menuTypeRanges.Ranges.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                            menuTypeRanges.Ranges.Style.Font.Bold = true;
                            menuTypeRanges.Ranges.Style.Font.FontSize = 10;
                        }

                        var numberFormatRanges = workBook.NamedRanges.NamedRange("numberFormat");
                        if (numberFormatRanges != null)
                        {
                            numberFormatRanges.Ranges.Style.NumberFormat.Format = "#,##0";
                            numberFormatRanges.Ranges.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
                            numberFormatRanges.Ranges.Style.Font.FontSize = 8;
                        }

                        var tableContentRanges = workBook.NamedRanges.NamedRange("tableContent");
                        if (tableContentRanges != null)
                        {
                            tableContentRanges.Ranges.Style.Border.InsideBorderColor = XLColor.FromArgb(0, 0, 221);
                            tableContentRanges.Ranges.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
                            tableContentRanges.Ranges.Style.Border.OutsideBorderColor = XLColor.FromArgb(0, 0, 139);
                            tableContentRanges.Ranges.Style.Border.OutsideBorder = XLBorderStyleValues.Medium;
                        }
                        wsFruit.Columns().AdjustToContents();
                        wsVeg.Columns().AdjustToContents();
                        workBook.SaveAs(filePath);


                    }
                    response.FileName = filePath;
                });
        }
        public ReportResponse DateRangeOrderItemExport(DateRangeOrderItemRequest request)
        {
            return Execute<DateRangeOrderItemRequest, ReportResponse>(
                request,
                response =>
                {

                    var orderItems = _mealMenuOrderFacade.GetDateRenageOrderItems(request.Filter);
                    var fileRepository = FileRepositoryPath;//_appSetting.GetSetting<string>("FileRepositoryPath");
                    var billingReportFolder = Path.Combine(fileRepository, "BillingReport");
                    if (!Directory.Exists(billingReportFolder))
                        Directory.CreateDirectory(Path.Combine(fileRepository, "BillingReport"));
                    var startDate = request.Filter.StartDate ?? DateTime.Now.AddDays(-7);
                    var endDate = request.Filter.EndDate ?? DateTime.Now;
                    var filePath = Path.Combine(billingReportFolder,
                        string.Format("{0}_{1}_BillingReport.xlsx", startDate.ToString("yyyyMMdd"),
                            endDate.ToString("yyyyMMdd")));
                    if (File.Exists(filePath))
                        File.Delete(filePath);
                    using (var workBook = new XLWorkbook(XLEventTracking.Disabled))
                    {
                        var workSheet = workBook.AddWorksheet("BillingReport");
                        var meals = Lookups.GetItems<MealTypes>().Where(d => d.Id > 0).OrderBy(d => d.Id).ToList();
                        var rowIndex = 5;
                        var colIndex = 2;
                        var counter = 0;
                        meals.ForEach(m =>
                        {
                            var mealMenus =
                                Lookups.MealMenuTypeList.Where(d => (long)d.Key == m.Id)
                                    .SelectMany(
                                        d => d.Value.Where(k => k != 0 && k != MenuTypes.Milk).Select(k => (long)k))
                                    .OrderBy(d => d)
                                    .ToList();

                            var mealTypeRange = workSheet.Range(rowIndex - 1, colIndex + counter, rowIndex - 1,
                                colIndex + counter + mealMenus.Count * 2 - 1);

                            mealTypeRange.Merge().AddToNamed("mealType");
                            mealTypeRange.SetValue(m.Text);
                            mealMenus.ForEach(mm =>
                            {
                                var menuTypeRange =
                                    workSheet.Range(rowIndex, colIndex + counter, rowIndex, colIndex + counter + 1)
                                        .Merge()
                                        .AddToNamed("menuType");
                                menuTypeRange.SetValue(Lookups.GetItem<MenuTypes>(mm).Text);

                                workSheet.Cell(rowIndex + 1, colIndex + counter).SetValue("Count");
                                workSheet.Range(rowIndex + 2, colIndex + counter, rowIndex + 2 + orderItems.Count,
                                    colIndex + counter).AddToNamed("numberFormat");
                                counter++;
                                workSheet.Cell(rowIndex + 1, colIndex + counter).SetValue("Price");
                                workSheet.Range(rowIndex + 2, colIndex + counter, rowIndex + 2 + orderItems.Count,
                                    colIndex + counter).AddToNamed("decimalFormat");
                                counter++;
                            });

                        });

                        workSheet.Cell(rowIndex + 1, colIndex + counter).SetValue("Total Price");
                        workSheet.Range(rowIndex + 2, colIndex + counter, rowIndex + 2 + orderItems.Count,
                            colIndex + counter).AddToNamed("decimalFormat");

                        workSheet.Cell(rowIndex + 1, 1).SetValue("School");
                        var tableSubColumnRange =
                            workSheet.Range(rowIndex + 1, 1, rowIndex + 1, 2 + counter).AddToNamed("tableSubColumn");
                        tableSubColumnRange.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                        tableSubColumnRange.Style.Font.Bold = true;
                        tableSubColumnRange.Style.Font.FontSize = 10;

                        workSheet.Range(rowIndex + 1, 1, rowIndex + 1 + orderItems.Count, 2 + counter)
                            .AddToNamed("tableContent");
                        var reportHeaderRange =
                            workSheet.Range(rowIndex - 2, 1, rowIndex - 2, 2 + counter)
                                .Merge()
                                .AddToNamed("reportHeader");
                        reportHeaderRange.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                        reportHeaderRange.Style.Font.Bold = true;
                        reportHeaderRange.Style.Font.FontSize = 16;
                        reportHeaderRange.SetValue(string.Format("INVOICE ITEMS , {0} - {1}",
                            startDate.ToString("yyyy-MM-dd"),
                            endDate.ToString("yyyy-MM-dd")));

                        var rowItemIndex = 7;

                        orderItems.ForEach(oi =>
                        {
                            workSheet.Cell(rowItemIndex, 1).SetValue(oi.SchoolName);
                            var colItemIndex = 2;
                            meals.ForEach(m =>
                            {
                                var mealMenus =
                                    Lookups.MealMenuTypeList.Where(d => (long)d.Key == m.Id)
                                        .SelectMany(
                                            d => d.Value.Where(k => k != 0 && k != MenuTypes.Milk).Select(k => (long)k))
                                        .OrderBy(d => d)
                                        .ToList();
                                var mealOrderItems = oi.MealList.FirstOrDefault(ml => ml.MealType.Id == m.Id);
                                mealMenus.ForEach(mm =>
                                {

                                    if (mealOrderItems == null)
                                    {
                                        workSheet.Cell(rowItemIndex, colItemIndex).SetValue(0);
                                        colItemIndex++;
                                        workSheet.Cell(rowItemIndex, colItemIndex).SetValue(0.00);
                                    }

                                    else
                                    {
                                        var menuOrderItems =
                                            mealOrderItems.MenuList.FirstOrDefault(ml => ml.MenuType.Id == mm);
                                        if (menuOrderItems == null)
                                        {
                                            workSheet.Cell(rowItemIndex, colItemIndex).SetValue(0);
                                            colItemIndex++;
                                            workSheet.Cell(rowItemIndex, colItemIndex).SetValue(0.00);
                                        }
                                        else
                                        {
                                            workSheet.Cell(rowItemIndex, colItemIndex)
                                                .SetValue(menuOrderItems.TotalCount);
                                            colItemIndex++;
                                            workSheet.Cell(rowItemIndex, colItemIndex)
                                                .SetValue(menuOrderItems.TotalPrice);
                                        }
                                    }
                                    colItemIndex++;
                                });

                            });
                            workSheet.Cell(rowItemIndex, colItemIndex).SetValue(oi.MealList.Sum(d => d.TotalPrice));
                            rowItemIndex++;

                        });
                        var mealTypeRanges = workBook.NamedRanges.NamedRange("mealType");
                        if (mealTypeRanges != null)
                        {
                            mealTypeRanges.Ranges.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                            mealTypeRanges.Ranges.Style.Font.Bold = true;
                            mealTypeRanges.Ranges.Style.Font.FontSize = 14;
                        }
                        var menuTypeRanges = workBook.NamedRanges.NamedRange("menuType");
                        if (menuTypeRanges != null)
                        {
                            menuTypeRanges.Ranges.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                            menuTypeRanges.Ranges.Style.Font.Bold = true;
                            menuTypeRanges.Ranges.Style.Font.FontSize = 12;
                        }

                        var numberFormatRanges = workBook.NamedRanges.NamedRange("numberFormat");
                        if (numberFormatRanges != null)
                        {
                            numberFormatRanges.Ranges.Style.NumberFormat.Format = "#,##0";
                            numberFormatRanges.Ranges.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
                        }
                        var decimalFormatRanges = workBook.NamedRanges.NamedRange("decimalFormat");
                        if (decimalFormatRanges != null)
                        {
                            decimalFormatRanges.Ranges.Style.NumberFormat.Format = "#,##0.00";
                            decimalFormatRanges.Ranges.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
                        }
                        var tableContentRanges = workBook.NamedRanges.NamedRange("tableContent");
                        if (tableContentRanges != null)
                        {
                            tableContentRanges.Ranges.Style.Border.InsideBorderColor = XLColor.FromArgb(0, 0, 221);
                            tableContentRanges.Ranges.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
                            tableContentRanges.Ranges.Style.Border.OutsideBorderColor = XLColor.FromArgb(0, 0, 139);
                            tableContentRanges.Ranges.Style.Border.OutsideBorder = XLBorderStyleValues.Medium;
                        }
                        workSheet.Columns().AdjustToContents();
                        workBook.SaveAs(filePath);


                    }
                    response.FileName = filePath;
                });
        }

        public ReportResponse SchoolMenuExport(SchoolMenuExportRequest request)
        {
            return Execute<SchoolMenuExportRequest, ReportResponse>(
                request,
                response =>
                {
                    var filter = new MealMenuOrderFilterView
                    {
                        OrderDate = request.Filter.OrderDate,
                        RecordStatusId = (int)Config.RecordStatuses.Active,
                        SchoolId = request.Filter.SchoolId,
                        SchoolType = request.Filter.SchoolType,
                        MealTypeId = request.Filter.MealTypeId
                    };


                    var result = _mealMenuOrderService.GetSchoolOrder(new SchoolOrderGetRequest { Filter = filter });
                    if (result.Result == Result.Success)
                    {
                        var fileRepository = FileRepositoryPath;//_appSetting.GetSetting<string>("FileRepositoryPath");
                        var templateFile = Path.Combine(fileRepository, "Templates", "SchoolMenuExport.xlsx");
                        var directoryPath = Path.Combine(fileRepository, "SchoolMenuExport");
                        if (!Directory.Exists(directoryPath))
                            Directory.CreateDirectory(directoryPath);

                        var fileName = string.Format("{0}_{1}_{2}_{3}.xlsx",
                            request.Filter.SchoolId,
                            request.Filter.MealTypeId,
                            request.Filter.OrderDate.ToString("yyyyMMdd"),
                            Directory.GetFiles(directoryPath).Length);

                        var filePath = Path.Combine(directoryPath, fileName);
                        File.Copy(templateFile, filePath, true);
                        var spreadsheet = SpreadsheetDocument.Open(filePath, true);


                        var sheetData =
                            spreadsheet.WorkbookPart.GetPartsOfType<WorksheetPart>()
                                .First()
                                .Worksheet.GetFirstChild<SheetData>();

                        //TODO BEGIN TO WRITE EXCEL
                        var menuInfoCell = sheetData.Descendants<Cell>().First(c => c.CellReference == "B2");
                        menuInfoCell.DataType = CellValues.InlineString;
                        menuInfoCell.InlineString = new InlineString
                        {
                            Text =
                                new Text
                                {
                                    Text = Lookups.GetItem<MealTypes>(request.Filter.MealTypeId).Text + " Menu"
                                }
                        };

                        var cellMonth = sheetData.Descendants<Cell>().First(c => c.CellReference == "B4");
                        cellMonth.InlineString = new InlineString
                        {
                            Text =
                                new Text
                                {
                                    Text =
                                        String.Format("{0} {1}",
                                            CultureInfo.CurrentCulture.DateTimeFormat.GetMonthName(
                                                request.Filter.OrderDate.Month), request.Filter.OrderDate.Year)
                                }
                        };
                        cellMonth.DataType = CellValues.InlineString;

                        var dayCols = new List<string> { "B", "E", "H", "K", "N" };
                        var tempDate = new DateTime(request.Filter.OrderDate.Year, request.Filter.OrderDate.Month, 1);
                        var daysInMonth = DateTime.DaysInMonth(tempDate.Year, tempDate.Month);
                        while (tempDate.DayOfWeek == DayOfWeek.Saturday || tempDate.DayOfWeek == DayOfWeek.Sunday)
                            tempDate = tempDate.AddDays(1);

                        var startDay = tempDate.Day;
                        var firstDay = new DateTime(tempDate.Year, tempDate.Month, 1);
                        var diff = (firstDay.DayOfWeek == DayOfWeek.Saturday)
                            ? 1
                            : 0;
                        for (var i = startDay; i <= daysInMonth; i++)
                        {
                            tempDate = new DateTime(tempDate.Year, tempDate.Month, i);
                            if (tempDate.DayOfWeek == DayOfWeek.Saturday || tempDate.DayOfWeek == DayOfWeek.Sunday)
                                continue;

                            var columnNumber = (tempDate.GetWeekOfMonth() * 7) + 1; // - (diff*5);
                            columnNumber = columnNumber - (diff * 7);
                            var dataColumnNumber = (tempDate.GetWeekOfMonth() * 7) + 2;
                            dataColumnNumber = dataColumnNumber - (diff * 7);
                            var dayColsIndex = (int)tempDate.DayOfWeek - 1;
                            //if (firstDay.DayOfWeek == DayOfWeek.Saturday || firstDay.DayOfWeek == DayOfWeek.Sunday)
                            //    dayColsIndex = dayColsIndex - 1;

                            var columnIndex = string.Format("{0}{1}", dayCols[dayColsIndex], columnNumber);
                            var dataColumnIndex = string.Format("{0}{1}", dayCols[(int)tempDate.DayOfWeek - 1],
                                dataColumnNumber);

                            var cellMenu = sheetData.Descendants<Cell>().First(c => c.CellReference == columnIndex);
                            cellMenu.InlineString = new InlineString { Text = new Text { Text = i.ToString() } };
                            cellMenu.DataType = CellValues.InlineString;

                            //var orderItems = result.Order.OrderItems.Where(c => c.MealMenuValidDate.Date == tempDate);
                            var orderDate = result.Order.Days.FirstOrDefault(c => c.Date == tempDate);
                            if (orderDate != null)
                            {
                                var mealMenuOrderItemViews = orderDate.Items;

                                if (mealMenuOrderItemViews.Any())
                                {
                                    var sb = new StringBuilder();
                                    sb = mealMenuOrderItemViews
                                        .Where(item => item.Count != 0)
                                        .Aggregate(sb,
                                            (current, item) =>
                                                current.Append(string.Format("{0} ({1})\r\n", item.MenuName,
                                                    item.Count)));

                                    var cellMeal =
                                        sheetData.Descendants<Cell>().First(c => c.CellReference == dataColumnIndex);
                                    cellMeal.InlineString = new InlineString { Text = new Text { Text = sb.ToString() } };
                                    cellMeal.DataType = CellValues.InlineString;
                                }
                            }

                        }

                        spreadsheet.WorkbookPart.Workbook.Save();

                        spreadsheet.WorkbookPart.Workbook.CalculationProperties.ForceFullCalculation = true;
                        spreadsheet.WorkbookPart.Workbook.CalculationProperties.FullCalculationOnLoad = true;
                        spreadsheet.Close();

                        response.FileName = filePath;
                    }
                });
        }

        public ReportResponse MontlyMilkExport(MontlyMilkExportRequest request)
        {
            return Execute<MontlyMilkExportRequest, ReportResponse>(
                request,
                response =>
                {
                    int totalCount;
                    if (!request.Filter.OrderStartDate.HasValue)
                        throw new Exception("Order StartDate Is Null");
                    var orderStartDate = request.Filter.OrderStartDate.Value;
                    var eMealType = (MealTypes)request.Filter.MealTypeId;


                    var orders = _mealMenuOrderFacade.GetOrderReport(request.Filter, int.MaxValue, 1, "Route", true, out totalCount);

                    var fileRepository = FileRepositoryPath;
                    var templateFile = Path.Combine(fileRepository, "Templates", "MilkItem.xlsx");
                    var fileFolder = Path.Combine(fileRepository, "MonthlyOrder");
                    if (!Directory.Exists(fileFolder))
                        Directory.CreateDirectory(fileFolder);

                    var milkTotalCount = 0;
                    var milkMenus = _menuFacade.GetByFilter(new MenuFilterView { MenuTypeId = (int)MenuTypes.Milk, RecordStatusId = (int)Meal.Config.RecordStatuses.Active }, 0, 1, "Name", true, out milkTotalCount);

                    var filePath = Path.Combine(fileFolder, string.Format("{0}_{1}_Milk.xlsx", orderStartDate.ToString("yyyy-MMM"), eMealType.ToString("G")));
                    File.Copy(templateFile, filePath, true);
                    File.SetAttributes(filePath, FileAttributes.Normal | FileAttributes.Archive);
                    using (var workBook = new XLWorkbook(filePath, XLEventTracking.Disabled))
                    {
                        var wsMeal = workBook.Worksheets.First();

                        var lastDayOfMonth = DateTime.DaysInMonth(orderStartDate.Year, orderStartDate.Month);
                        if (request.Filter.OrderEndDate.HasValue)
                            lastDayOfMonth = request.Filter.OrderEndDate.Value.Day;

                        var headerTitle = string.Format("[{1} - {2}-{3}] {0} Order -  Milk Menu", eMealType.ToString("G"), orderStartDate.ToString("yyyy-MM-dd"), orderStartDate.ToString("yyyy-MM"), lastDayOfMonth);
                        var orderList =
                            orders.Where(o => o.MealTypeId == (int)eMealType /*&& o.Items.Any(i => i.Menus.Any(m => m.MenuTypeId == (int)MenuTypes.Milk))*/)
                                .OrderBy(k => k.SchoolRoute)
                                .ThenBy(k => k.SchoolName)
                                .ToList();

                        var rowStart = 6;
                        var columnStart = 4;
                        for (var day = orderStartDate.Day; day <= lastDayOfMonth; day++)
                        {
                            var currentDay = new DateTime(orderStartDate.Year, orderStartDate.Month, day);
                            if (currentDay.DayOfWeek == DayOfWeek.Saturday ||
                                currentDay.DayOfWeek == DayOfWeek.Sunday)
                                continue;
                            var weekDayRange = wsMeal.Range(rowStart - 1, columnStart, rowStart - 1, columnStart + milkMenus.Count).Merge();
                            weekDayRange.SetValue(currentDay.DayOfWeek.ToString().Substring(0, 2));

                            var monthDayRange = wsMeal.Range(rowStart, columnStart, rowStart, columnStart + milkMenus.Count).Merge();
                            monthDayRange.SetValue(day.ToString());

                            wsMeal.Cell(rowStart + 1, columnStart).SetValue("Total");
                            columnStart++;
                            for (var mm = 0; mm < milkMenus.Count; mm++)
                            {
                                wsMeal.Cell(rowStart + 1, columnStart + mm).SetValue(milkMenus[mm].Name);
                            }
                            columnStart += milkMenus.Count;
                        }



                        var tableHeaderRange = wsMeal.Range(rowStart - 1, 4, rowStart + 1, columnStart - 1).AddToNamed("tableHeader");
                        tableHeaderRange.Style.Fill.BackgroundColor = XLColor.FromArgb(218, 238, 243);
                        tableHeaderRange.Style.Border.InsideBorderColor = XLColor.FromArgb(0, 0, 221);
                        tableHeaderRange.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
                        tableHeaderRange.Style.Border.OutsideBorderColor = XLColor.FromArgb(0, 0, 139);
                        tableHeaderRange.Style.Border.OutsideBorder = XLBorderStyleValues.Medium;

                        var headerTitleRange =
                            wsMeal.Range(2, 6, 3, 20).Merge().AddToNamed("headerTitle");
                        headerTitleRange.SetValue(headerTitle);
                        headerTitleRange.Style.Fill.BackgroundColor = XLColor.FromArgb(67, 255, 152);
                        headerTitleRange.Style.Font.SetFontSize(16);
                        headerTitleRange.Style.Font.SetBold(true);
                        headerTitleRange.Style.Font.SetItalic(true);
                        headerTitleRange.Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);
                        headerTitleRange.Style.Alignment.SetVertical(XLAlignmentVerticalValues.Center);



                        var orderRowStart = 8;
                        var orderColumnStart = 1;
                        orderList.ForEach(o =>
                        {
                            var schoolRoute = o.SchoolRoute;
                            wsMeal.Cell(orderRowStart, orderColumnStart).SetValue(schoolRoute);
                            wsMeal.Cell(orderRowStart, orderColumnStart + 1).SetValue(o.SchoolType);
                            wsMeal.Cell(orderRowStart, orderColumnStart + 2).SetValue(o.SchoolName);
                            orderColumnStart += 3;
                            for (var day = orderStartDate.Day; day <= lastDayOfMonth; day++)
                            {
                                var currentDay = new DateTime(orderStartDate.Year, orderStartDate.Month, day);
                                if (currentDay.DayOfWeek == DayOfWeek.Saturday ||
                                    currentDay.DayOfWeek == DayOfWeek.Sunday)
                                    continue;

                                var dateMenus = o.Items.Where(m => m.Date == currentDay)
                                    .SelectMany(m => m.Menus)
                                    .Where(t => t.MenuTypeId == (int)MenuTypes.Milk).Select(m => m)
                                    .ToList();
                                if (dateMenus.Any())
                                    wsMeal.Cell(orderRowStart, orderColumnStart).SetValue(dateMenus.Sum(t => t.TotalCount));
                                orderColumnStart++;
                                for (var mm = 0; mm < milkMenus.Count; mm++)
                                {
                                    var milkMenu = dateMenus.FirstOrDefault(dm => dm.Name == milkMenus[mm].Name);
                                    if (milkMenu != null)
                                        wsMeal.Cell(orderRowStart, orderColumnStart + mm).SetValue(milkMenu.TotalCount);
                                }
                                orderColumnStart += milkMenus.Count;
                            }
                            orderColumnStart = 1;
                            orderRowStart++;

                        });



                        var tableContent =
                            wsMeal.Range(8, 1, orderRowStart - 1, columnStart - 1).AddToNamed("tableContent");
                        tableContent.Style.Border.InsideBorderColor = XLColor.FromArgb(0, 0, 221);
                        tableContent.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
                        tableContent.Style.Border.OutsideBorderColor = XLColor.FromArgb(0, 0, 139);
                        tableContent.Style.Border.OutsideBorder = XLBorderStyleValues.Medium;


                        wsMeal.Range(6, 1, 6, 3)
                            .Merge()
                            .SetValue(string.Format("Report Date : {0:G}", DateTime.Now));

                        rowStart = orderRowStart + 2;
                        columnStart = 4;
                        for (var day = orderStartDate.Day; day <= lastDayOfMonth; day++)
                        {
                            var currentDay = new DateTime(orderStartDate.Year, orderStartDate.Month, day);
                            if (currentDay.DayOfWeek == DayOfWeek.Saturday ||
                                currentDay.DayOfWeek == DayOfWeek.Sunday)
                                continue;
                            var weekDayRange = wsMeal.Range(rowStart + 1, columnStart, rowStart + 1, columnStart + milkMenus.Count).Merge();
                            weekDayRange.SetValue(currentDay.DayOfWeek.ToString().Substring(0, 2));

                            var monthDayRange = wsMeal.Range(rowStart, columnStart, rowStart, columnStart + milkMenus.Count).Merge();
                            monthDayRange.SetValue(day.ToString());

                            var dateMenus = orderList.SelectMany(k => k.Items.Where(m => m.Date == currentDay)
                                .SelectMany(m => m.Menus.Where(l => l.MenuTypeId == (int)MenuTypes.Milk)))
                                .Select(m => m)
                                .ToList();


                            wsMeal.Cell(rowStart - 1, columnStart).SetValue("Total");
                            wsMeal.Cell(rowStart - 2, columnStart).SetValue(dateMenus.Sum(k => k.TotalCount));

                            columnStart++;
                            for (var mm = 0; mm < milkMenus.Count; mm++)
                            {
                                wsMeal.Cell(rowStart - 1, columnStart + mm).SetValue(milkMenus[mm].Name);
                                wsMeal.Cell(rowStart - 2, columnStart + mm).SetValue(dateMenus.Where(k => k.Name == milkMenus[mm].Name).Sum(k => k.TotalCount));
                            }

                            columnStart += milkMenus.Count;
                        }
                        var footerRow = wsMeal.Range(orderRowStart, 4, rowStart + 1, columnStart - 1).AddToNamed("footerRow");
                        footerRow.Style.Font.SetBold(true);
                        footerRow.Style.Font.SetItalic(true);
                        footerRow.Style.Border.InsideBorderColor = XLColor.FromArgb(0, 0, 221);
                        footerRow.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
                        footerRow.Style.Border.OutsideBorderColor = XLColor.FromArgb(0, 0, 139);
                        footerRow.Style.Border.OutsideBorder = XLBorderStyleValues.Medium;


                        wsMeal.PageSetup.PageOrientation = XLPageOrientation.Landscape;
                        wsMeal.PageSetup.PagesWide = 1;
                        wsMeal.Columns().AdjustToContents();

                        workBook.Save();
                    }
                    response.FileName = filePath;
                });
        }
        public ReportResponse MonthlyPurchacseReport(OrderDayPropItemRequest request)
        {
            return Execute<OrderDayPropItemRequest, ReportResponse>(
                request,
                response =>
                {
                    var orderDayDate = Convert.ToDateTime(request.Filter.OrderDate);
                    var orderItems = _mealMenuOrderFacade.GetMonthlyPurchaseReport(request.Filter);
                    var path = AppDomain.CurrentDomain.BaseDirectory;
                    var filePath = Path.Combine(path, "Templates\\MASTER.xlsx");
                    var workBook = new XLWorkbook(filePath);
                    var sheet = workBook.Worksheet(1);

                    var rowIndex = 8;
                    var counter = 0;
                    var milkTotalCount = 0;

                    var schoolList = orderItems.GroupBy(d => new { d.SchoolName, d.TotalCount, d.MenuName, d.menuType }).Select(d => new
                    {
                        totalCount = d.Key.TotalCount,
                        SchoolName = d.Key.SchoolName,
                        menuName = d.Key.MenuName,
                        MenuType = d.Key.menuType
                    }).ToList();

                    var menuItem = orderItems.GroupBy(d => new { d.id, d.FruitCount, d.VegetableCount, d.SchoolName, d.TotalCount, d.MenuName, d.validate, d.menuType, d.Type, d.MealType, d.BreakFast }).Select(d => new
                    {
                        menuId = d.Key.id,
                        menuName = d.Key.MenuName,
                        totalCount = d.Key.TotalCount,
                        fruitCount = d.Key.FruitCount,
                        vegCount = d.Key.VegetableCount,
                        validdate = d.Key.validate,
                        menuTypeId = d.Key.menuType,
                        SchoolName = d.Key.SchoolName,
                        Type = d.Key.Type,
                        mealType = d.Key.MealType,
                        breakfastOVS = d.Key.BreakFast
                    }).ToList();
                    var oderMonth = orderDayDate.ToString("MMMM");
                    var orderYear = orderDayDate.ToString("dd.MM.yy");
                    var dateReportDate = oderMonth + " Number Report Date: " + orderYear;
                    sheet.Cell(3, 12).SetValue(dateReportDate);
                    var ColCountDay = 3;
                    List<int?> ManuLunchOption1HS = new List<int?>();
                    List<int?> ManuLunchOption1K8 = new List<int?>();
                    List<int?> ManuLunchOption2HS = new List<int?>();
                    List<int?> ManuLunchOption2K8 = new List<int?>();
                    List<int?> ManuLunchOption4 = new List<int?>();
                    List<int?> ManuLunchOptionHS5 = new List<int?>();
                    List<int?> ManuLunchOptionK85 = new List<int?>();
                    List<int?> MenuSackOptionK81 = new List<int?>();
                    List<int?> MenuSackOptionHS1 = new List<int?>();
                    List<int?> MenuSackOptionHS2 = new List<int?>();
                    List<int?> MenuSackOptionK82 = new List<int?>();
                    List<int?> MenuLunchVegHS = new List<int?>();
                    List<int?> MenuLunchVegK8 = new List<int?>();
                    List<int?> MenupickUpHS = new List<int?>();
                    List<int?> MenupickUpK8 = new List<int?>();
                    List<int?> MenuBBQHS = new List<int?>();
                    List<int?> MenuBBQK8 = new List<int?>();
                    List<int?> MenuSupperOption = new List<int?>();
                    List<int?> MenuSupperVeg = new List<int?>();
                    List<int?> MenuComptonLunchHS = new List<int?>();
                    List<int?> MenuComptonLunchk8 = new List<int?>();
                    List<int?> MenuBreakfastOVS1 = new List<int?>();
                    List<int?> MenuBreakfastOVS2 = new List<int?>();
                    List<int?> MenuBreakfastVegOVS1 = new List<int?>();
                    List<int?> MenuBreakfastVegOVS2 = new List<int?>();
                    List<int?> MenuBreakfastOp3 = new List<int?>();
                    List<int?> MenuBreakfastOp1 = new List<int?>();
                    List<int?> MenuBreakfastOp2 = new List<int?>();
                    List<int?> MenuBreakfastOption2 = new List<int?>();
                    List<int?> MenuBreakfastOp4OVS1 = new List<int?>();
                    List<int?> MenuBreakfastOp4OVS2 = new List<int?>();
                    List<int?> MenuComptonSupper = new List<int?>();
                    List<int?> MenuAnimoSchool1 = new List<int?>();
                    List<int?> MenuAnimoSchool2 = new List<int?>();
                    List<int?> MenuAnimoSchool3 = new List<int?>();
                    List<int?> MenuAnimoSchool4 = new List<int?>();
                    List<int?> MenuAnimoSchoolLunch1 = new List<int?>();
                    List<int?> MenuAnimoSchoolLunch2 = new List<int?>();
                    List<int?> MenuAnimoSchoolLunch3 = new List<int?>();
                    List<int?> MenuAnimoSchoolLunch4 = new List<int?>();
                    List<int?> MenuPizzaHS = new List<int?>();
                    List<int?> MenuPizzaK8 = new List<int?>();
                    List<int?> MenuSaladHS = new List<int?>();
                    List<int?> MenuSaladK8 = new List<int?>();
                    List<int?> MenuSupperAll = new List<int?>();
                    List<int?> MenuSupperSpecial = new List<int?>();
                    List<string> daysList = new List<string>();
                    List<int?> MenuAnimoBreakSchool1 = new List<int?>();
                    List<int?> MenuAnimoBreakSchool2 = new List<int?>();
                    List<int?> MenuAnimoBreakSchool3 = new List<int?>();
                    List<int?> MenuAnimoBreakSchool4 = new List<int?>();
                    List<int?> MenuAnimoBreakSchool5 = new List<int?>();
                    List<int?> MenuAnimoBreakSchool6 = new List<int?>();
                    List<int?> MenuSnackCount = new List<int?>();
                    List<int?> MenusnackCountSchool1 = new List<int?>();
                    List<int?> MenusnackCountSchool2 = new List<int?>();
                    List<int?> MenuOnlyMS1 = new List<int?>();
                    List<int?> MenuOnlyMS2 = new List<int?>();
                    List<int?> MenuNutSnack1 = new List<int?>();
                    List<int?> MenuNutSnack2 = new List<int?>();
                    List<int?> MenuNutSnack3 = new List<int?>();
                    List<int?> MenuAllLunch = new List<int?>();
                    List<int?> MenuAllVeg = new List<int?>();
                    List<int?> MenuAllPickup = new List<int?>();
                    List<int?> MenuAllPizza = new List<int?>();
                    List<int?> MenuAllBBQ = new List<int?>();
                    List<int?> MenuAllSackLunch = new List<int?>();
                    var mealType1 = new long[] { 2, 3 };
                    var SchoolName1 = new string[] { "LEAP ICEF Inglewood Hillcrest", "LEAP ICEF Inglewood Grevillea", "LEAP ICEF Innovation ", "LEAP ICEF View Park K8", "LEAP ICEF Vista Elem", "LEAP Magnolia 7" };
                    for (int day = 1; day <= orderDayDate.Day; day++)
                    {
                        var currentDay = new DateTime(orderDayDate.Year, orderDayDate.Month, day);
                        if (currentDay.DayOfWeek == DayOfWeek.Saturday ||
                            currentDay.DayOfWeek == DayOfWeek.Sunday)
                            continue;
                        var rowDays = currentDay.ToString("ddd");
                        var rowDate = currentDay.ToString("dd");
                        sheet.Cell(5, ColCountDay).SetValue(rowDays);
                        sheet.Cell(6, ColCountDay).SetValue(rowDate);
                        daysList.Add(rowDays);
                        var LunchOption1HS = menuItem.Where(m => m.validdate == currentDay && m.menuTypeId == 5 && m.Type == "Hs").Select(d => new { d.totalCount, d.SchoolName }).Distinct().ToList();
                        var LunchOption1HSdsfds = menuItem.Where(m => m.validdate == currentDay && m.menuTypeId == 5 && m.Type == "Hs").Select(d => new { d.totalCount, d.SchoolName }).Distinct().ToList();
                        var LunchOption1K8 = menuItem.Where(m => m.validdate == currentDay && m.menuTypeId == (int)MenuTypes.LunchOption1 && m.Type == "K8").Select(d => new { d.totalCount, d.SchoolName }).Distinct().ToList();
                        var LunchOption2HS = menuItem.Where(m => m.validdate == currentDay && m.menuTypeId == (int)MenuTypes.LunchOption2 && m.Type == "Hs").Select(d => new { d.totalCount, d.SchoolName }).Distinct().ToList();
                        var LunchOption2K8 = menuItem.Where(m => m.validdate == currentDay && m.menuTypeId == (int)MenuTypes.LunchOption2 && m.Type == "K8").Select(d => new { d.totalCount, d.SchoolName }).Distinct().ToList();
                        var LunchOption4 = menuItem.Where(m => m.validdate == currentDay && m.menuTypeId == (int)MenuTypes.LunchOption4).Select(d => new { d.totalCount, d.SchoolName }).Distinct().ToList();
                        var LunchOptionHS5 = menuItem.Where(m => m.validdate == currentDay && m.menuTypeId == (int)MenuTypes.LunchOption5 && m.Type == "Hs").Select(d => new { d.totalCount, d.SchoolName }).Distinct().ToList();
                        var LunchOptionK85 = menuItem.Where(m => m.validdate == currentDay && m.menuTypeId == (int)MenuTypes.LunchOption5 && m.Type == "K8").Select(d => new { d.totalCount, d.SchoolName }).Distinct().ToList();
                        var SackOptionHS1 = menuItem.Where(m => m.validdate == currentDay && m.menuTypeId == (int)MenuTypes.SackLunch1 && m.Type == "Hs").Select(d => new { d.totalCount, d.SchoolName }).Distinct().ToList();
                        var SackOptionK81 = menuItem.Where(m => m.validdate == currentDay && m.menuTypeId == (int)MenuTypes.SackLunch1 && m.Type == "K8").Select(d => new { d.totalCount, d.SchoolName }).Distinct().ToList();
                        var SackOptionHS2 = menuItem.Where(m => m.validdate == currentDay && m.menuTypeId == (int)MenuTypes.Vegetarian && m.mealType.Id == 4 && m.Type == "Hs").Select(d => new { d.totalCount, d.SchoolName }).Distinct().ToList();
                        var SackOptionK82 = menuItem.Where(m => m.validdate == currentDay && m.menuTypeId == (int)MenuTypes.Vegetarian && m.mealType.Id == 4 && m.Type == "K8").Select(d => new { d.totalCount, d.SchoolName }).Distinct().ToList();
                        var LunchVegHS = menuItem.Where(m => m.validdate == currentDay && m.menuTypeId == (int)MenuTypes.Vegetarian && m.mealType.Id == 3 && m.Type == "Hs").Select(d => new { d.totalCount, d.SchoolName }).Distinct().ToList();
                        var LunchVegK8 = menuItem.Where(m => m.validdate == currentDay && m.menuTypeId == (int)MenuTypes.Vegetarian && m.mealType.Id == 3 && m.Type == "K8").Select(d => new { d.totalCount, d.SchoolName }).Distinct().ToList();
                        var pickUpHS = menuItem.Where(m => m.validdate == currentDay && m.menuTypeId == (int)MenuTypes.PickupStix && m.Type == "Hs").Select(d => new { d.totalCount, d.SchoolName }).Distinct().ToList();
                        var pickUpK8 = menuItem.Where(m => m.validdate == currentDay && m.menuTypeId == (int)MenuTypes.PickupStix && m.Type == "K8").Select(d => new { d.totalCount, d.SchoolName }).Distinct().ToList();
                        var BBQHS = menuItem.Where(m => m.validdate == currentDay && m.menuTypeId == (int)MenuTypes.Bbq && m.Type == "Hs").Select(d => new { d.totalCount, d.SchoolName }).Distinct().ToList();
                        var BBQK8 = menuItem.Where(m => m.validdate == currentDay && m.menuTypeId == (int)MenuTypes.Bbq && m.Type == "K8").Select(d => new { d.totalCount, d.SchoolName }).Distinct().ToList();
                        var SupperOptions = menuItem.Where(m => m.validdate == currentDay && m.mealType.Id == 5 && m.menuTypeId == (int)MenuTypes.Supper1).Select(d => new { d.totalCount, d.SchoolName }).Distinct().ToList();
                        var SupperVeg = menuItem.Where(m => m.validdate == currentDay && m.menuTypeId == (int)MenuTypes.Vegetarian && m.mealType.Id == 5).Select(d => new { d.totalCount, d.SchoolName }).Distinct().ToList();
                        var ComptonLunchHS = menuItem.Where(m => m.validdate == currentDay && m.menuTypeId == (int)MenuTypes.ComptonLunch && m.Type == "Hs").Select(d => new { d.totalCount, d.SchoolName }).Distinct().ToList();
                        var ComptonLunchK8 = menuItem.Where(m => m.validdate == currentDay && m.menuTypeId == (int)MenuTypes.ComptonLunch && m.Type == "K8").Select(d => new { d.totalCount, d.SchoolName }).Distinct().ToList();
                        var BreakfastOVS1 = menuItem.Where(m => m.validdate == currentDay && m.menuTypeId == (int)MenuTypes.Breakfast1 && m.breakfastOVS == "OffervsServe").Select(d => new { d.totalCount, d.SchoolName , d.breakfastOVS}).Distinct().ToList();
                        var BreakfastOVS2 = menuItem.Where(m => m.validdate == currentDay && m.menuTypeId == (int)MenuTypes.Breakfast1 && m.breakfastOVS == "ServeOnly").Select(d => new { d.totalCount, d.SchoolName }).Distinct().ToList();
                        var BreakfastVegOVS1 = menuItem.Where(m => m.validdate == currentDay && m.mealType.Id==1 && m.breakfastOVS == "OffervsServe" && m.menuTypeId == (int)MenuTypes.Vegetarian).Select(d => new { d.totalCount, d.SchoolName }).Distinct().ToList();
                        var BreakfastVegOVS2 = menuItem.Where(m => m.validdate == currentDay && m.mealType.Id == 1 && m.breakfastOVS == "ServeOnly" && m.menuTypeId == (int)MenuTypes.Vegetarian).Select(d => new { d.totalCount, d.SchoolName }).Distinct().ToList();
                        var BreakfastOp3 = menuItem.Where(m => m.validdate == currentDay && m.menuTypeId == (int)MenuTypes.Breakfast3).Select(d => new { d.totalCount, d.SchoolName }).Distinct().ToList();
                        var BreakfastOp1OVS = menuItem.Where(m => m.validdate == currentDay && m.breakfastOVS == "OffervsServe" && m.menuTypeId == (int)MenuTypes.Breakfast2).Select(d => new { d.totalCount, d.SchoolName }).Distinct().ToList();
                        var BreakfastOp2OVS = menuItem.Where(m => m.validdate == currentDay && m.breakfastOVS == "ServeOnly" && m.menuTypeId == (int)MenuTypes.Breakfast2).Select(d => new { d.totalCount, d.SchoolName }).Distinct().ToList();
                        var BreakfastOption2 = menuItem.Where(m => m.validdate == currentDay && m.menuTypeId == (int)MenuTypes.Breakfast2).Select(d => new { d.totalCount, d.SchoolName }).Distinct().ToList();
                        var BreakfastOp4OVS1 = menuItem.Where(m => m.validdate == currentDay && m.menuTypeId == (int)MenuTypes.Breakfast4 && m.breakfastOVS == "OffervsServe").Select(d => new { d.totalCount, d.SchoolName }).Distinct().ToList();
                        var BreakfastOp4OVS2 = menuItem.Where(m => m.validdate == currentDay && m.menuTypeId == (int)MenuTypes.Breakfast4 && m.breakfastOVS == "ServeOnly").Select(d => new { d.totalCount, d.SchoolName }).Distinct().ToList();
                        var ComptonSupper = menuItem.Where(m => m.validdate == currentDay && m.menuTypeId == (int)MenuTypes.ComptonSupper).Select(d => new { d.totalCount, d.SchoolName }).Distinct().ToList();
                        var AnimoSchool1 = menuItem.Where(m => m.validdate == currentDay && m.menuTypeId == (int)MenuTypes.LunchOption1 && m.SchoolName == "Animo Ralph Bunche ").Select(d => new { d.totalCount, d.SchoolName }).Distinct().ToList();
                        var AnimoSchool2 = menuItem.Where(m => m.validdate == currentDay && m.menuTypeId == (int)MenuTypes.LunchOption1 && m.SchoolName == "Animo Jefferson Middle ").Select(d => new { d.totalCount, d.SchoolName }).Distinct().ToList();
                        var AnimoSchool3 = menuItem.Where(m => m.validdate == currentDay && m.menuTypeId == (int)MenuTypes.LunchOption1 && m.SchoolName == "Animo Pat Brown High School").Select(d => new { d.totalCount, d.SchoolName }).Distinct().ToList();
                        var AnimoSchoolLunch1 = menuItem.Where(m => m.validdate == currentDay && m.menuTypeId == (int)MenuTypes.LunchOption2 && m.SchoolName == "Animo Ralph Bunche ").Select(d => new { d.totalCount, d.SchoolName }).Distinct().ToList();
                        var AnimoSchoolLunch2 = menuItem.Where(m => m.validdate == currentDay && m.menuTypeId == (int)MenuTypes.LunchOption2 && m.SchoolName == "Animo Jefferson Middle ").Select(d => new { d.totalCount, d.SchoolName }).Distinct().ToList();
                        var AnimoSchoolLunch3 = menuItem.Where(m => m.validdate == currentDay && m.menuTypeId == (int)MenuTypes.LunchOption2 && m.SchoolName == "Animo Pat Brown High School").Select(d => new { d.totalCount, d.SchoolName }).Distinct().ToList();
                        var pizzaHS = menuItem.Where(m => m.validdate == currentDay && m.menuTypeId == 13 && m.Type == "Hs").Select(d => new { d.totalCount, d.SchoolName, d.menuName }).Distinct().ToList();
                        var pizzaK8 = menuItem.Where(m => m.validdate == currentDay && m.menuTypeId == 13 && m.Type == "K8").Select(d => new { d.totalCount, d.SchoolName, d.menuName }).Distinct().ToList();
                        var SaladHS = menuItem.Where(m => m.validdate == currentDay && m.menuName.Contains("Salad") && m.Type == "Hs").Select(d => new { d.totalCount, d.SchoolName, d.menuName }).Distinct().ToList();
                        var SaladK8 = menuItem.Where(m => m.validdate == currentDay && m.menuName.Contains("Salad") && m.Type == "K8").Select(d => new { d.totalCount, d.SchoolName, d.menuName }).Distinct().ToList();
                        var SupperAll = menuItem.Where(m => m.validdate == currentDay && m.mealType.Id == 5).Select(d => new { d.totalCount, d.SchoolName, d.menuName }).Distinct().ToList();
                        var SupperSpecial = menuItem.Where(m => m.validdate == currentDay && m.menuTypeId == (int)MenuTypes.Special && m.mealType.Id == 5).Select(d => new { d.totalCount, d.SchoolName, d.menuName }).Distinct().ToList();
                        var AnimoBreakSchool1 = menuItem.Where(m => m.validdate == currentDay && m.mealType.Id == 1 && m.SchoolName == "Animo Ralph Bunche ").Select(d => new { d.totalCount, d.SchoolName }).Distinct().ToList();
                        var AnimoBreakSchool2 = menuItem.Where(m => m.validdate == currentDay && m.mealType.Id == 1 && m.SchoolName == "Animo Jefferson Middle ").Select(d => new { d.totalCount, d.SchoolName }).Distinct().ToList();
                        var AnimoBreakSchool3 = menuItem.Where(m => m.validdate == currentDay && m.mealType.Id == 1 && m.SchoolName == "Animo Pat Brown High School").Select(d => new { d.totalCount, d.SchoolName }).Distinct().ToList();
                        var AnimoBreakSchool4 = menuItem.Where(m => m.validdate == currentDay && m.mealType.Id == 1 && m.SchoolName == "Palmdale Aerospace Academy HS").Select(d => new { d.totalCount, d.SchoolName }).Distinct().ToList();
                        var AnimoBreakSchool5 = menuItem.Where(m => m.validdate == currentDay && m.mealType.Id == 1 && m.SchoolName == "Palmdale Aerospace Middle").Select(d => new { d.totalCount, d.SchoolName }).Distinct().ToList();
                        var AnimoBreakSchool6 = menuItem.Where(m => m.validdate == currentDay && m.mealType.Id == 1 && m.SchoolName == "Palmdale Aerospace Staff Meals ONLY").Select(d => new { d.totalCount, d.SchoolName }).Distinct().ToList();
                        var snackCount = menuItem.Where(m => m.validdate == currentDay && m.mealType.Id == 2).Select(d => new { d.totalCount, d.SchoolName }).Distinct().ToList();
                        var snackCountSchool1 = menuItem.Where(m => m.validdate == currentDay && m.mealType.Id == 2 && m.SchoolName.Contains("Ideal")).Select(d => new { d.totalCount, d.SchoolName }).Distinct().ToList();
                        var snackCountSchool2 = menuItem.Where(m => m.validdate == currentDay && mealType1.Contains(m.mealType.Id) && SchoolName1.Contains(m.SchoolName)).Select(d => new { d.totalCount, d.SchoolName }).Distinct().ToList();
                        var OnlyMS1 = menuItem.Where(m => m.validdate == currentDay && m.menuTypeId == (int)MenuTypes.LunchOption5).Select(d => new { d.totalCount, d.SchoolName }).Distinct().ToList();
                        var OnlyMS2 = menuItem.Where(m => m.validdate == currentDay && m.menuTypeId == (int)MenuTypes.Vegetarian && m.mealType.Id == 3).Select(d => new { d.totalCount, d.SchoolName }).Distinct().ToList();
                        var NutSnack1 = menuItem.Where(m => m.validdate == currentDay && m.menuTypeId == (int)MenuTypes.Vegetarian && m.mealType.Id == 3 && m.SchoolName == "Ideal Program Services").Select(d => new { d.totalCount, d.SchoolName }).Distinct().ToList();
                        var NutSnack2 = menuItem.Where(m => m.validdate == currentDay && m.menuTypeId == (int)MenuTypes.Vegetarian && m.mealType.Id == 3 && m.SchoolName == "CEI Alliance CR 4").Select(d => new { d.totalCount, d.SchoolName }).Distinct().ToList();
                        var NutSnack3 = menuItem.Where(m => m.validdate == currentDay && m.menuTypeId == (int)MenuTypes.Vegetarian && m.mealType.Id == 3 && m.SchoolName == "James Jordan Middle School").Select(d => new { d.totalCount, d.SchoolName }).Distinct().ToList();
                        var AllLunch = menuItem.Where(m => m.validdate == currentDay && m.mealType.Id == 3 && m.SchoolName.Contains("%Brown%") && m.SchoolName.Contains("%James%")
                                       && m.SchoolName.Contains("%Lifesource Charter%") && m.SchoolName.Contains("%Lifesource Preschool%") && m.SchoolName.Contains("%Magnolia 7%") && m.SchoolName.Contains("%Multicultural%")).Select(d => new { d.totalCount, d.SchoolName }).Distinct().ToList();
                        var AllVeg = menuItem.Where(m => m.validdate == currentDay && m.menuTypeId == (int)MenuTypes.Vegetarian && m.SchoolName.Contains("%Brown%") && m.SchoolName.Contains("%James%")
                                       && m.SchoolName.Contains("%Lifesource Charter%") && m.SchoolName.Contains("%Lifesource Preschool%") && m.SchoolName.Contains("%Magnolia 7%") && m.SchoolName.Contains("%Multicultural%")).Select(d => new { d.totalCount, d.SchoolName }).Distinct().ToList();
                        var AllPickUpStix = menuItem.Where(m => m.validdate == currentDay && m.menuTypeId == (int)MenuTypes.PickupStix && m.SchoolName.Contains("%Brown%") && m.SchoolName.Contains("%James%")
                                       && m.SchoolName.Contains("%Lifesource Charter%") && m.SchoolName.Contains("%Lifesource Preschool%") && m.SchoolName.Contains("%Magnolia 7%") && m.SchoolName.Contains("%Multicultural%")).Select(d => new { d.totalCount, d.SchoolName }).Distinct().ToList();
                        var AllPizza = menuItem.Where(m => m.validdate == currentDay && m.mealType.Id == 3 && m.SchoolName.Contains("%Brown%") && m.SchoolName.Contains("%James%")
                                       && m.SchoolName.Contains("%Lifesource Charter%") && m.SchoolName.Contains("%Lifesource Preschool%") && m.SchoolName.Contains("%Magnolia 7%") && m.SchoolName.Contains("%Multicultural%")).Select(d => new { d.totalCount, d.SchoolName }).Distinct().ToList();
                        var AllBBq = menuItem.Where(m => m.validdate == currentDay && m.menuTypeId == (int)MenuTypes.Bbq && m.SchoolName.Contains("%Brown%") && m.SchoolName.Contains("%James%")
                                       && m.SchoolName.Contains("%Lifesource Charter%") && m.SchoolName.Contains("%Lifesource Preschool%") && m.SchoolName.Contains("%Magnolia 7%") && m.SchoolName.Contains("%Multicultural%")).Select(d => new { d.totalCount, d.SchoolName }).Distinct().ToList();
                        var AllSackLunch = menuItem.Where(m => m.validdate == currentDay && m.mealType.Id == 4 && m.SchoolName.Contains("%Brown%") && m.SchoolName.Contains("%James%")
                                       && m.SchoolName.Contains("%Lifesource Charter%") && m.SchoolName.Contains("%Lifesource Preschool%") && m.SchoolName.Contains("%Magnolia 7%") && m.SchoolName.Contains("%Multicultural%")).Select(d => new { d.totalCount, d.SchoolName }).Distinct().ToList();

                        var totalLunchOption1HS = LunchOption1HS.Sum(s => s.totalCount);
                        var totalLunchOption1K8 = LunchOption1K8.Sum(s => s.totalCount);
                        var totalLunchOption2HS = LunchOption2HS.Sum(s => s.totalCount);
                        var totalLunchOption2K8 = LunchOption2K8.Sum(s => s.totalCount);
                        var totalLunchOption4 = LunchOption4.Sum(s => s.totalCount);
                        var totalLunchOptionHS5 = LunchOptionHS5.Sum(s => s.totalCount);
                        var totalLunchOptionK85 = LunchOptionK85.Sum(s => s.totalCount);
                        var totalSackOptionHS1 = SackOptionHS1.Sum(s => s.totalCount);
                        var totalSackOptionK81 = SackOptionK81.Sum(s => s.totalCount);
                        var totalSackOptionHS2 = SackOptionHS2.Sum(s => s.totalCount);
                        var totalSackOptionK82 = SackOptionK82.Sum(s => s.totalCount);
                        var totalLunchVegHS = LunchVegHS.Sum(s => s.totalCount);
                        var totalLunchVegK8 = LunchVegK8.Sum(s => s.totalCount);
                        var totalpickUpHS = pickUpHS.Sum(s => s.totalCount);
                        var totalpickUpK8 = pickUpK8.Sum(s => s.totalCount);
                        var totalBBQHS = BBQHS.Sum(s => s.totalCount);
                        var totalBBQK8 = BBQK8.Sum(s => s.totalCount);
                        var totalsupperOption = SupperOptions.Sum(s => s.totalCount);
                        var totalsupperVeg = SupperVeg.Sum(s => s.totalCount);
                        var totalComptonLunchHS = ComptonLunchHS.Sum(s => s.totalCount);
                        var totalComptonLunchK8 = ComptonLunchK8.Sum(s => s.totalCount);
                        var totalBreakfastOVS1 = BreakfastOVS1.Sum(s => s.totalCount);
                        var totalBreakfastOVS2 = BreakfastOVS2.Sum(s => s.totalCount);
                        var totalBreakfastVegOVS1 = BreakfastVegOVS1.Sum(s => s.totalCount);
                        var totalBreakfastVegOVS2 = BreakfastVegOVS2.Sum(s => s.totalCount);
                        var TotalBreakfastOp3 = BreakfastOp3.Sum(s => s.totalCount);
                        var totalBreakfastOp1OVS = BreakfastOp1OVS.Sum(s => s.totalCount);
                        var totalBreakfastOp2OVS = BreakfastOp2OVS.Sum(s => s.totalCount);
                        var totalBreakfastOption2 = BreakfastOption2.Sum(s => s.totalCount);
                        var totalBreakfastOp4OVS1 = BreakfastOp4OVS1.Sum(s => s.totalCount);
                        var totalBreakfastOp4OVS2 = BreakfastOp4OVS2.Sum(s => s.totalCount);
                        var totalComptonSupper = ComptonSupper.Sum(s => s.totalCount);
                        var totalAnimoSchool1 = AnimoSchool1.Sum(s => s.totalCount);
                        var totalAnimoSchool2 = AnimoSchool2.Sum(s => s.totalCount);
                        var totalAnimoSchool3 = AnimoSchool3.Sum(s => s.totalCount);
                        var totalAnimoSchoolLunch1 = AnimoSchoolLunch1.Sum(s => s.totalCount);
                        var totalAnimoSchoolLunch2 = AnimoSchoolLunch2.Sum(s => s.totalCount);
                        var totalAnimoSchoolLunch3 = AnimoSchoolLunch3.Sum(s => s.totalCount);
                        var totalPizzaHS = pizzaHS.Sum(s => s.totalCount);
                        var totalPizzaK8 = pizzaK8.Sum(s => s.totalCount);
                        var totalSaladHS = SaladHS.Sum(s => s.totalCount);
                        var totalSaladK8 = SaladK8.Sum(s => s.totalCount);
                        var totalSupperAll = SupperAll.Sum(s => s.totalCount);
                        var totalSupperSpecial = SupperSpecial.Sum(s => s.totalCount);
                        var totalAnimoBreakSchool1 = AnimoBreakSchool1.Sum(s => s.totalCount);
                        var totalAnimoBreakSchool2 = AnimoBreakSchool2.Sum(s => s.totalCount);
                        var totalAnimoBreakSchool3 = AnimoBreakSchool3.Sum(s => s.totalCount);
                        var totalAnimoBreakSchool4 = AnimoBreakSchool4.Sum(s => s.totalCount);
                        var totalAnimoBreakSchool5 = AnimoBreakSchool5.Sum(s => s.totalCount);
                        var totalAnimoBreakSchool6 = AnimoBreakSchool6.Sum(s => s.totalCount);
                        var totalsnackCount = snackCount.Sum(s => s.totalCount);
                        var totalsnackCountSchool1 = snackCountSchool1.Sum(s => s.totalCount);
                        var totalsnackCountSchool2 = snackCountSchool2.Sum(s => s.totalCount);
                        var totalOnlyMS1 = OnlyMS1.Sum(s => s.totalCount);
                        var totalOnlyMS2 = OnlyMS2.Sum(s => s.totalCount);
                        var totalNutSnack1 = NutSnack1.Sum(s => s.totalCount);
                        var totalNutSnack2 = NutSnack2.Sum(s => s.totalCount);
                        var totalNutSnack3 = NutSnack3.Sum(s => s.totalCount);
                        var totalAllLunch = AllLunch.Sum(s => s.totalCount);
                        var totalAllVeg = AllVeg.Sum(s => s.totalCount);
                        var totalAllPickup = AllPickUpStix.Sum(s => s.totalCount);
                        var totalAllPizza = AllPizza.Sum(s => s.totalCount);
                        var totalAllBBQ = AllBBq.Sum(s => s.totalCount);
                        var totalAllSackLunch = AllSackLunch.Sum(s => s.totalCount);



                        ManuLunchOption1HS.Add(totalLunchOption1HS);
                        ManuLunchOption1K8.Add(totalLunchOption1K8);
                        ManuLunchOption2HS.Add(totalLunchOption2HS);
                        ManuLunchOption2K8.Add(totalLunchOption2K8);
                        ManuLunchOption4.Add(totalLunchOption4);
                        ManuLunchOptionHS5.Add(totalLunchOptionHS5);
                        ManuLunchOptionK85.Add(totalLunchOptionK85);
                        MenuSackOptionK81.Add(totalSackOptionK81);
                        MenuSackOptionHS1.Add(totalSackOptionHS1);
                        MenuSackOptionHS2.Add(totalSackOptionHS2);
                        MenuSackOptionK82.Add(totalSackOptionK82);
                        MenuLunchVegHS.Add(totalLunchVegHS);
                        MenuLunchVegK8.Add(totalLunchVegK8);
                        MenupickUpHS.Add(totalpickUpHS);
                        MenupickUpK8.Add(totalpickUpK8);
                        MenuBBQHS.Add(totalBBQHS);
                        MenuBBQK8.Add(totalBBQK8);
                        MenuSupperOption.Add(totalsupperOption);
                        MenuSupperVeg.Add(totalsupperVeg);
                        MenuComptonLunchHS.Add(totalComptonLunchHS);
                        MenuComptonLunchk8.Add(totalComptonLunchK8);
                        MenuBreakfastOVS1.Add(totalBreakfastOVS1);
                        MenuBreakfastOVS2.Add(totalBreakfastOVS2);
                        MenuBreakfastVegOVS1.Add(totalBreakfastVegOVS1);
                        MenuBreakfastVegOVS2.Add(totalBreakfastVegOVS2);
                        MenuBreakfastOp3.Add(TotalBreakfastOp3);
                        MenuBreakfastOp1.Add(totalBreakfastOp1OVS);
                        MenuBreakfastOp2.Add(totalBreakfastOp2OVS);
                        MenuBreakfastOption2.Add(totalBreakfastOption2);
                        MenuBreakfastOp4OVS1.Add(totalBreakfastOp4OVS1);
                        MenuBreakfastOp4OVS2.Add(totalBreakfastOp4OVS2);
                        MenuComptonSupper.Add(totalComptonSupper);
                        MenuAnimoSchool1.Add(totalAnimoSchool1);
                        MenuAnimoSchool2.Add(totalAnimoSchool2);
                        MenuAnimoSchool3.Add(totalAnimoSchool3);
                        MenuAnimoSchoolLunch1.Add(totalAnimoSchoolLunch1);
                        MenuAnimoSchoolLunch2.Add(totalAnimoSchoolLunch2);
                        MenuAnimoSchoolLunch3.Add(totalAnimoSchoolLunch3);
                        MenuPizzaHS.Add(totalPizzaHS);
                        MenuPizzaK8.Add(totalPizzaK8);
                        MenuSaladHS.Add(totalSaladHS);
                        MenuSaladK8.Add(totalSaladK8);
                        MenuSupperSpecial.Add(totalSupperSpecial);
                        MenuAnimoBreakSchool1.Add(totalAnimoBreakSchool1);
                        MenuAnimoBreakSchool2.Add(totalAnimoBreakSchool2);
                        MenuAnimoBreakSchool3.Add(totalAnimoBreakSchool3);
                        MenuAnimoBreakSchool4.Add(totalAnimoBreakSchool4);
                        MenuAnimoBreakSchool5.Add(totalAnimoBreakSchool5);
                        MenuAnimoBreakSchool6.Add(totalAnimoBreakSchool6);
                        MenusnackCountSchool1.Add(totalsnackCountSchool1);
                        MenusnackCountSchool2.Add(totalsnackCountSchool2);
                        MenuSnackCount.Add(totalsnackCount);
                        MenuOnlyMS1.Add(totalOnlyMS1);
                        MenuOnlyMS2.Add(totalOnlyMS2);
                        MenuNutSnack1.Add(totalNutSnack1);
                        MenuNutSnack2.Add(totalNutSnack2);
                        MenuNutSnack3.Add(totalNutSnack2);
                        MenuAllLunch.Add(totalAllLunch);
                        MenuAllVeg.Add(totalAllVeg);
                        MenuAllPickup.Add(totalAllPickup);
                        MenuAllPizza.Add(totalAllPizza);
                        MenuAllBBQ.Add(totalAllBBQ);
                        MenuAllSackLunch.Add(totalAllSackLunch);

                        ColCountDay++;
                    }

                    for (int i = 0; i <= MenuAnimoSchool1.Count - 1; i++)
                    {
                        int? total = MenuAnimoSchool1[i] + MenuAnimoSchool2[i] + MenuAnimoSchool3[i];
                        MenuAnimoSchool4.Add(total);
                    }

                    var totalAminoSchools1 = 3;
                    MenuAnimoSchool4.ForEach(s => {
                        sheet.Cell(12, totalAminoSchools1).SetValue(s);
                        totalAminoSchools1++;
                    });
                    for (int i = 0; i <= MenuAnimoSchoolLunch1.Count - 1; i++)
                    {
                        int? total = MenuAnimoSchoolLunch1[i] + MenuAnimoSchoolLunch2[i] + MenuAnimoSchoolLunch3[i];
                        MenuAnimoSchoolLunch4.Add(total);
                    }

                    var totalAminoSchoolsLunch2 = 3;
                    MenuAnimoSchoolLunch4.ForEach(s => {
                        sheet.Cell(19, totalAminoSchoolsLunch2).SetValue(s);
                        totalAminoSchoolsLunch2++;
                    });


                    var totalCountStart1 = 3;
                    ManuLunchOption1HS.ForEach(s => {
                        sheet.Cell(7, totalCountStart1).SetValue(s);
                        totalCountStart1++;
                    });
                    var totalCountStart2 = 3;
                    ManuLunchOption1K8.ForEach(s => {
                        sheet.Cell(8, totalCountStart2).SetValue(s);
                        totalCountStart2++;
                    });
                    var totalCountStart3 = 3;
                    ManuLunchOption2HS.ForEach(s => {
                        sheet.Cell(14, totalCountStart3).SetValue(s);
                        totalCountStart3++;
                    });
                    var totalCountStart4 = 3;
                    ManuLunchOption2K8.ForEach(s => {
                        sheet.Cell(15, totalCountStart4).SetValue(s);
                        totalCountStart4++;
                    });
                    var totalCountStart5 = 3;
                    ManuLunchOption4.ForEach(s => {
                        sheet.Cell(13, totalCountStart5).SetValue(s);
                        totalCountStart5++;
                    });
                    var totalCountStartHS5 = 3;
                    MenuOnlyMS1.ForEach(s => {
                        sheet.Cell(20, totalCountStartHS5).SetValue(s);
                        totalCountStartHS5++;
                    });
                    var totalCountStartK85 = 3;
                    MenuOnlyMS2.ForEach(s => {
                        sheet.Cell(21, totalCountStartK85).SetValue(s);
                        totalCountStartK85++;
                    });
                    var totalSackOptionh1 = 3;
                    MenuSackOptionHS1.ForEach(s => {
                        sheet.Cell(22, totalSackOptionh1).SetValue(s);
                        totalSackOptionh1++;
                    });
                    var totalSackOptionk1 = 3;
                    MenuSackOptionK81.ForEach(s => {
                        sheet.Cell(23, totalSackOptionk1).SetValue(s);
                        totalSackOptionk1++;
                    });
                    var totalSackOptionh2 = 3;
                    MenuSackOptionHS2.ForEach(s => {
                        sheet.Cell(25, totalSackOptionh2).SetValue(s);
                        totalSackOptionh2++;
                    });
                    var totalSackOptionk2 = 3;
                    MenuSackOptionK82.ForEach(s => {
                        sheet.Cell(26, totalSackOptionk2).SetValue(s);
                        totalSackOptionk2++;
                    });
                    var CountLunchVegHS = 3;
                    MenuLunchVegHS.ForEach(s => {
                        sheet.Cell(28, CountLunchVegHS).SetValue(s);
                        CountLunchVegHS++;
                    });
                    var CountLunchVegK8 = 3;
                    MenuLunchVegK8.ForEach(s => {
                        sheet.Cell(29, CountLunchVegK8).SetValue(s);
                        CountLunchVegK8++;
                    });
                    var CountpickUpHS = 3;
                    MenupickUpHS.ForEach(s => {
                        sheet.Cell(36, CountpickUpHS).SetValue(s);
                        CountpickUpHS++;
                    });
                    var CountpickUpK8 = 3;
                    MenupickUpK8.ForEach(s => {
                        sheet.Cell(37, CountpickUpK8).SetValue(s);
                        CountpickUpK8++;
                    });
                    var CountBBQHS = 3;
                    MenuBBQHS.ForEach(s => {
                        sheet.Cell(39, CountBBQHS).SetValue(s);
                        CountBBQHS++;
                    });
                    var CountBBQK8 = 3;
                    MenuBBQK8.ForEach(s => {
                        sheet.Cell(40, CountBBQK8).SetValue(s);
                        CountBBQK8++;
                    });
                    var CountSupperOption = 3;
                    MenuSupperOption.ForEach(s => {
                        sheet.Cell(45, CountSupperOption).SetValue(s);
                        CountSupperOption++;
                    });
                    var CountSupperSpecial = 3;
                    MenuSupperSpecial.ForEach(s => {
                        sheet.Cell(47, CountSupperSpecial).SetValue(s);
                        CountSupperSpecial++;
                    });
                    var CountSupperVeg = 3;
                    MenuSupperVeg.ForEach(s => {
                        sheet.Cell(48, CountSupperVeg).SetValue(s);
                        CountSupperVeg++;
                    });
                    var CountBreakfastOVS1 = 3;
                    MenuBreakfastOVS1.ForEach(s => {
                        sheet.Cell(62, CountBreakfastOVS1).SetValue(s);
                        CountBreakfastOVS1++;
                    });
                    var CountBreakfastOVS2 = 3;
                    MenuBreakfastOVS2.ForEach(s => {
                        sheet.Cell(63, CountBreakfastOVS2).SetValue(s);
                        CountBreakfastOVS2++;
                    });
                    var CountBreakfastVeg1 = 3;
                    MenuBreakfastVegOVS1.ForEach(s => {
                        sheet.Cell(67, CountBreakfastVeg1).SetValue(s);
                        CountBreakfastVeg1++;
                    });
                    var CountBreakfastVeg2 = 3;
                    MenuBreakfastVegOVS2.ForEach(s => {
                        sheet.Cell(68, CountBreakfastVeg2).SetValue(s);
                        CountBreakfastVeg2++;
                    });

                    var CountComptonLunchHS = 3;
                    MenuComptonLunchHS.ForEach(s => {
                        sheet.Cell(55, CountComptonLunchHS).SetValue(s);
                        CountComptonLunchHS++;
                    });
                    var CountComptonLunchK8 = 3;
                    MenuComptonLunchk8.ForEach(s => {
                        sheet.Cell(56, CountComptonLunchK8).SetValue(s);
                        CountComptonLunchK8++;
                    });
                    var CountBreakfastOp4 = 3;
                    MenuBreakfastOp4OVS1.ForEach(s => {
                        sheet.Cell(70, CountBreakfastOp4).SetValue(s);
                        CountBreakfastOp4++;
                    });
                    var totalBreakfastOp4 = 3;
                    MenuBreakfastOp4OVS2.ForEach(s => {
                        sheet.Cell(71, totalBreakfastOp4).SetValue(s);
                        totalBreakfastOp4++;
                    });
                    var CountBreakfastOp3 = 3;
                    MenuBreakfastOp3.ForEach(s => {
                        sheet.Cell(72, CountBreakfastOp3).SetValue(s);
                        CountBreakfastOp3++;
                    });
                    var CountBreakfastOp1 = 3;
                    MenuBreakfastOp1.ForEach(s => {
                        sheet.Cell(75, CountBreakfastOp1).SetValue(s);
                        CountBreakfastOp1++;
                    });
                    var CountBreakfastOp2 = 3;
                    MenuBreakfastOp2.ForEach(s => {
                        sheet.Cell(76, CountBreakfastOp2).SetValue(s);
                        CountBreakfastOp2++;
                    });
                    var CountBreakfastOption2 = 3;
                    MenuBreakfastOption2.ForEach(s => {
                        sheet.Cell(77, CountBreakfastOption2).SetValue(s);
                        CountBreakfastOption2++;
                    });
                    var CountComptonSupper = 3;
                    MenuComptonSupper.ForEach(s => {
                        sheet.Cell(58, CountComptonSupper).SetValue(s);
                        CountComptonSupper++;
                    });
                    var pizzaCountHS = 3;
                    MenuPizzaHS.ForEach(s => {
                        sheet.Cell(33, pizzaCountHS).SetValue(s);
                        pizzaCountHS++;
                    });
                    var pizzaCountK8 = 3;
                    MenuPizzaK8.ForEach(s => {
                        sheet.Cell(34, pizzaCountK8).SetValue(s);
                        pizzaCountK8++;
                    });
                    var saladCountHS = 3;
                    MenuSaladHS.ForEach(s => {
                        sheet.Cell(42, saladCountHS).SetValue(s);
                        saladCountHS++;
                    });
                    var saladCountK8 = 3;
                    MenuSaladK8.ForEach(s => {
                        sheet.Cell(43, saladCountK8).SetValue(s);
                        saladCountK8++;
                    });
                    var SupperAllCount = 3;
                    MenuSupperAll.ForEach(s => {
                        sheet.Cell(45, SupperAllCount).SetValue(s);
                        SupperAllCount++;
                    });
                    var SupperSpecialCount = 3;
                    MenuSupperSpecial.ForEach(s => {
                        sheet.Cell(47, SupperSpecialCount).SetValue(s);
                        SupperSpecialCount++;
                    });

                    List<int> nutSnackCount = new List<int>();
                    for (int i = 0; i <= MenuNutSnack1.Count - 1; i++)
                    {
                        int total = Convert.ToInt32(MenuNutSnack1[i] + MenuNutSnack2[i] + MenuNutSnack3[i]);
                        nutSnackCount.Add(total);
                    }
                    var nutSnackCount1 = 3;
                    nutSnackCount.ForEach(t => {
                        sheet.Cell(53, nutSnackCount1).SetValue(t);
                        nutSnackCount1++;
                    });

                    List<int> AnimoBreakSchoolCount = new List<int>();
                    for (int i = 0; i <= MenuAnimoBreakSchool1.Count - 1; i++)
                    {
                        int total = Convert.ToInt32(MenuAnimoBreakSchool1[i] + MenuAnimoBreakSchool2[i] + MenuAnimoBreakSchool3[i] + MenuAnimoBreakSchool4[i]
                                      + MenuAnimoBreakSchool5[i] + MenuAnimoBreakSchool6[i]);
                        AnimoBreakSchoolCount.Add(total);
                    }
                    var BreakSchoolCount = 3;
                    AnimoBreakSchoolCount.ForEach(t => {
                        sheet.Cell(74, BreakSchoolCount).SetValue(t);
                        BreakSchoolCount++;
                    });
                    var SnackCountTotal = 3;
                    MenuSnackCount.ForEach(t => {
                        sheet.Cell(50, SnackCountTotal).SetValue(t);
                        SnackCountTotal++;
                    });
                    List<int> snackCountSchools = new List<int>();
                    for (int i = 0; i <= MenusnackCountSchool1.Count - 1; i++)
                    {
                        int total = Convert.ToInt32(MenusnackCountSchool1[i] + MenusnackCountSchool2[i]);
                        snackCountSchools.Add(total);
                    }
                    var snackCountSchoolsTotal1 = 3;
                    snackCountSchools.ForEach(t => {
                        sheet.Cell(52, snackCountSchoolsTotal1).SetValue(t);
                        snackCountSchoolsTotal1++;
                    });

                    List<int> snackCountSchoolsTotal = new List<int>();
                    for (int i = 0; i <= MenuSnackCount.Count - 1; i++)
                    {
                        int total = Convert.ToInt32(snackCountSchools[i] + MenuSnackCount[i]);
                        snackCountSchoolsTotal.Add(total);
                    }

                    var SnackCountTotal1 = 3;
                    snackCountSchoolsTotal.ForEach(t => {
                        sheet.Cell(54, SnackCountTotal1).SetValue(t);
                        SnackCountTotal1++;
                    });

                    List<int> option1HSTotal = new List<int>();
                    var option1HSStart = 3;
                    for (int i = 1; i <= ManuLunchOption1HS.Count; i++)
                    {
                        int total = Convert.ToInt32(sheet.Cell(7, option1HSStart).Value);
                        option1HSTotal.Add(total);
                        option1HSStart++;
                    }
                    List<int> option1K8Total = new List<int>();
                    var option1K8Start = 3;
                    for (int i = 1; i <= ManuLunchOption2HS.Count; i++)
                    {
                        int total = Convert.ToInt32(sheet.Cell(8, option1K8Start).Value);
                        option1K8Total.Add(total);
                        option1K8Start++;
                    }
                    List<int> option1Count = new List<int>();
                    for (int i = 0; i <= ManuLunchOption1HS.Count - 1; i++)
                    {
                        int total = option1HSTotal[i] + option1K8Total[i];
                        option1Count.Add(total);
                    }
                    var calulateOption1 = 3;
                    option1Count.ForEach(t => {
                        sheet.Cell(9, calulateOption1).SetValue(t);
                        calulateOption1++;
                    });
                    var totalcalulateOption1 = 3;
                    option1Count.ForEach(t => {
                        sheet.Cell(10, totalcalulateOption1).SetValue(t);
                        totalcalulateOption1++;
                    });
                    List<int> option2HSTotal = new List<int>();
                    var option2HSStart = 3;
                    for (int i = 1; i <= ManuLunchOption1HS.Count; i++)
                    {
                        int total = Convert.ToInt32(sheet.Cell(14, option2HSStart).Value);
                        option2HSTotal.Add(total);
                        option2HSStart++;
                    }
                    List<int> option2K8Total = new List<int>();
                    var option2K8Start = 3;
                    for (int i = 1; i <= ManuLunchOption2HS.Count; i++)
                    {
                        int total = Convert.ToInt32(sheet.Cell(15, option2K8Start).Value);
                        option2K8Total.Add(total);
                        option2K8Start++;
                    }
                    List<int> option2Count = new List<int>();
                    for (int i = 0; i <= ManuLunchOption2HS.Count - 1; i++)
                    {
                        int total = option2HSTotal[i] + option2K8Total[i];
                        option2Count.Add(total);
                    }
                    var calulateOption2 = 3;
                    option2Count.ForEach(t => {
                        sheet.Cell(16, calulateOption2).SetValue(t);
                        calulateOption2++;
                    });
                    var totalcalulateOption2 = 3;
                    option2Count.ForEach(t => {
                        sheet.Cell(17, totalcalulateOption2).SetValue(t);
                        totalcalulateOption2++;
                    });


                    List<int> sackTotalHS = new List<int>();
                    var sackTotalHSStart = 3;
                    for (int i = 1; i <= MenuSackOptionHS1.Count; i++)
                    {
                        int total = Convert.ToInt32(sheet.Cell(22, sackTotalHSStart).Value);
                        sackTotalHS.Add(total);
                        sackTotalHSStart++;
                    }
                    List<int> sackTotalK8 = new List<int>();
                    var sackTotalK8Start = 3;
                    for (int i = 1; i <= MenuSackOptionHS2.Count; i++)
                    {
                        int total = Convert.ToInt32(sheet.Cell(23, sackTotalK8Start).Value);
                        sackTotalK8.Add(total);
                        sackTotalK8Start++;
                    }
                    List<int> sackCount = new List<int>();
                    for (int i = 0; i <= MenuSackOptionHS2.Count - 1; i++)
                    {
                        int total = sackTotalHS[i] + sackTotalK8[i];
                        sackCount.Add(total);
                    }
                    var calulateSack1 = 3;
                    sackCount.ForEach(t => {
                        sheet.Cell(24, calulateSack1).SetValue(t);
                        calulateSack1++;
                    });

                    List<int> LunchVegTotalHS = new List<int>();
                    var LunchVegTotalHSStart = 3;
                    for (int i = 1; i <= MenuLunchVegHS.Count; i++)
                    {
                        int total = Convert.ToInt32(sheet.Cell(28, LunchVegTotalHSStart).Value);
                        LunchVegTotalHS.Add(total);
                        LunchVegTotalHSStart++;
                    }
                    List<int> LunchVegTotalK8 = new List<int>();
                    var LunchVegTotalK8Start = 3;
                    for (int i = 1; i <= MenuLunchVegK8.Count; i++)
                    {
                        int total = Convert.ToInt32(sheet.Cell(29, LunchVegTotalK8Start).Value);
                        LunchVegTotalK8.Add(total);
                        LunchVegTotalK8Start++;
                    }
                    List<int> LunchVegCount = new List<int>();
                    for (int i = 0; i <= MenuSackOptionHS2.Count - 1; i++)
                    {
                        int total = LunchVegTotalHS[i] + LunchVegTotalK8[i];
                        LunchVegCount.Add(total);
                    }
                    var calulateLunchVeg1 = 3;
                    LunchVegCount.ForEach(t => {
                        sheet.Cell(30, calulateLunchVeg1).SetValue(t);
                        calulateLunchVeg1++;
                    });
                    var calulateLunchVeg2 = 3;
                    LunchVegCount.ForEach(t => {
                        sheet.Cell(31, calulateLunchVeg2).SetValue(t);
                        calulateLunchVeg2++;
                    });

                    List<int> sackVegTotalHS = new List<int>();
                    var sackVegTotalHSStart = 3;
                    for (int i = 1; i <= MenuSackOptionHS2.Count; i++)
                    {
                        int total = Convert.ToInt32(sheet.Cell(25, sackVegTotalHSStart).Value);
                        sackVegTotalHS.Add(total);
                        sackVegTotalHSStart++;
                    }
                    List<int> sackVegTotalK8 = new List<int>();
                    var sackVegTotalK8Start = 3;
                    for (int i = 1; i <= MenuLunchVegK8.Count; i++)
                    {
                        int total = Convert.ToInt32(sheet.Cell(26, sackVegTotalK8Start).Value);
                        sackVegTotalK8.Add(total);
                        sackVegTotalK8Start++;
                    }
                    List<int> sackVegCount = new List<int>();
                    for (int i = 0; i <= MenuSackOptionK82.Count - 1; i++)
                    {
                        int total = sackVegTotalHS[i] + sackVegTotalK8[i];
                        sackVegCount.Add(total);
                    }
                    var calulateSackVeg1 = 3;
                    sackVegCount.ForEach(t => {
                        sheet.Cell(27, calulateSackVeg1).SetValue(t);
                        calulateSackVeg1++;
                    });

                    List<int> pickupstixTotalHS = new List<int>();
                    var pickupstixTotalHSStart = 3;
                    for (int i = 1; i <= MenupickUpHS.Count; i++)
                    {
                        int total = Convert.ToInt32(sheet.Cell(36, pickupstixTotalHSStart).Value);
                        pickupstixTotalHS.Add(total);
                        pickupstixTotalHSStart++;
                    }
                    List<int> pickupstixTotalK8 = new List<int>();
                    var pickupstixTotalK8Start = 3;
                    for (int i = 1; i <= MenupickUpK8.Count; i++)
                    {
                        int total = Convert.ToInt32(sheet.Cell(37, pickupstixTotalK8Start).Value);
                        pickupstixTotalK8.Add(total);
                        pickupstixTotalK8Start++;
                    }
                    List<int> pickupStixCount = new List<int>();
                    for (int i = 0; i <= MenuSackOptionK82.Count - 1; i++)
                    {
                        int total = pickupstixTotalHS[i] + pickupstixTotalK8[i];
                        pickupStixCount.Add(total);
                    }
                    var calulatePickupStix1 = 3;
                    pickupStixCount.ForEach(t => {
                        sheet.Cell(38, calulatePickupStix1).SetValue(t);
                        calulatePickupStix1++;
                    });

                    List<int> BBQTotalHS = new List<int>();
                    var BBQTotalHSStart = 3;
                    for (int i = 1; i <= MenuBBQHS.Count; i++)
                    {
                        int total = Convert.ToInt32(sheet.Cell(39, BBQTotalHSStart).Value);
                        BBQTotalHS.Add(total);
                        BBQTotalHSStart++;
                    }
                    List<int> BBQTotalK8 = new List<int>();
                    var BBQTotalK8Start = 3;
                    for (int i = 1; i <= MenuBBQK8.Count; i++)
                    {
                        int total = Convert.ToInt32(sheet.Cell(40, BBQTotalK8Start).Value);
                        BBQTotalK8.Add(total);
                        BBQTotalK8Start++;
                    }
                    List<int> BBQCount = new List<int>();
                    for (int i = 0; i <= MenuBBQHS.Count - 1; i++)
                    {
                        int total = BBQTotalHS[i] + BBQTotalK8[i];
                        BBQCount.Add(total);
                    }
                    var calulateBBQ = 3;
                    BBQCount.ForEach(t => {
                        sheet.Cell(41, calulateBBQ).SetValue(t);
                        calulateBBQ++;
                    });

                    List<int> ComptonLunchTotalHS = new List<int>();
                    var ComptonLunchTotalHSStart = 3;
                    for (int i = 1; i <= MenuComptonLunchHS.Count; i++)
                    {
                        int total = Convert.ToInt32(sheet.Cell(55, ComptonLunchTotalHSStart).Value);
                        ComptonLunchTotalHS.Add(total);
                        ComptonLunchTotalHSStart++;
                    }
                    List<int> ComptonLunchTotalK8 = new List<int>();
                    var ComptonLunchTotalK8Start = 3;
                    for (int i = 1; i <= MenuComptonLunchk8.Count; i++)
                    {
                        int total = Convert.ToInt32(sheet.Cell(56, ComptonLunchTotalK8Start).Value);
                        ComptonLunchTotalK8.Add(total);
                        ComptonLunchTotalK8Start++;
                    }
                    List<int> ComLunchCount = new List<int>();
                    for (int i = 0; i <= MenuBBQHS.Count - 1; i++)
                    {
                        int total = ComptonLunchTotalHS[i] + ComptonLunchTotalK8[i];
                        ComLunchCount.Add(total);
                    }
                    var calulateCLunch = 3;
                    ComLunchCount.ForEach(t => {
                        sheet.Cell(57, calulateCLunch).SetValue(t);
                        calulateCLunch++;
                    });

                    List<int> AllItemLunchCount = new List<int>();
                    for (int i = 0; i <= MenuAllLunch.Count - 1; i++)
                    {
                        int total = Convert.ToInt32(MenuAllLunch[i] + MenuAllVeg[i] + MenuAllPickup[i] + MenuAllPizza[i] + MenuAllBBQ[i] + MenuAllSackLunch[i]);
                        AllItemLunchCount.Add(total);
                    }
                    var AllcalulateCLunch = 3;
                    AllItemLunchCount.ForEach(t => {
                        sheet.Cell(59, AllcalulateCLunch).SetValue(t);
                        AllcalulateCLunch++;
                    });


                    List<int> BreakfastAllTotalHS = new List<int>();
                    var BreakfastAllTotalHSStart = 3;
                    for (int i = 1; i <= MenuBreakfastOVS1.Count; i++)
                    {
                        int total = Convert.ToInt32(sheet.Cell(62, BreakfastAllTotalHSStart).Value);
                        BreakfastAllTotalHS.Add(total);
                        BreakfastAllTotalHSStart++;
                    }
                    List<int> BreakfastAllTotalK8 = new List<int>();
                    var BreakfastAllTotalK8Start = 3;
                    for (int i = 1; i <= MenuBreakfastOVS2.Count; i++)
                    {
                        int total = Convert.ToInt32(sheet.Cell(63, BreakfastAllTotalK8Start).Value);
                        BreakfastAllTotalK8.Add(total);
                        BreakfastAllTotalK8Start++;
                    }
                    List<int> BreakfastAllCount = new List<int>();
                    for (int i = 0; i <= MenuBreakfastOVS2.Count - 1; i++)
                    {
                        int total = BreakfastAllTotalHS[i] + BreakfastAllTotalK8[i];
                        BreakfastAllCount.Add(total);
                    }
                    var calulateBreakfastAll1 = 3;
                    BreakfastAllCount.ForEach(t => {
                        sheet.Cell(65, calulateBreakfastAll1).SetValue(t);
                        calulateBreakfastAll1++;
                    });
                    var calulateBreakfastAll2 = 3;
                    BreakfastAllCount.ForEach(t => {
                        sheet.Cell(66, calulateBreakfastAll2).SetValue(t);
                        calulateBreakfastAll2++;
                    });

                    List<int> BreakfastVegTotalHS = new List<int>();
                    var BreakfastVegTotalHSStart = 3;
                    for (int i = 1; i <= MenuBreakfastOVS1.Count; i++)
                    {
                        int total = Convert.ToInt32(sheet.Cell(67, BreakfastVegTotalHSStart).Value);
                        BreakfastVegTotalHS.Add(total);
                        BreakfastVegTotalHSStart++;
                    }
                    List<int> BreakfastVegTotalK8 = new List<int>();
                    var BreakfastVegTotalK8Start = 3;
                    for (int i = 1; i <= MenuBreakfastOVS2.Count; i++)
                    {
                        int total = Convert.ToInt32(sheet.Cell(68, BreakfastVegTotalK8Start).Value);
                        BreakfastVegTotalK8.Add(total);
                        BreakfastVegTotalK8Start++;
                    }
                    List<int> BreakfastVegCount = new List<int>();
                    for (int i = 0; i <= MenuBreakfastOVS2.Count - 1; i++)
                    {
                        int total = BreakfastVegTotalHS[i] + BreakfastVegTotalK8[i];
                        BreakfastVegCount.Add(total);
                    }
                    var calulateBreakfastVeg = 3;
                    BreakfastVegCount.ForEach(t => {
                        sheet.Cell(69, calulateBreakfastVeg).SetValue(t);
                        calulateBreakfastVeg++;
                    });

                    List<int> BreakfastOp2TotalHS = new List<int>();
                    var BreakfastOp2TotalHSStart = 3;
                    for (int i = 1; i <= MenuBreakfastOption2.Count; i++)
                    {
                        int total = Convert.ToInt32(sheet.Cell(75, BreakfastOp2TotalHSStart).Value);
                        BreakfastOp2TotalHS.Add(total);
                        BreakfastOp2TotalHSStart++;
                    }
                    List<int> BreakfastOp2TotalK8 = new List<int>();
                    var BreakfastOp2TotalK8Start = 3;
                    for (int i = 1; i <= MenuBreakfastOption2.Count; i++)
                    {
                        int total = Convert.ToInt32(sheet.Cell(76, BreakfastOp2TotalK8Start).Value);
                        BreakfastOp2TotalK8.Add(total);
                        BreakfastOp2TotalK8Start++;
                    }
                    List<int> BreakfastOp2Count = new List<int>();
                    for (int i = 0; i <= MenuBreakfastOption2.Count - 1; i++)
                    {
                        int total = BreakfastOp2TotalHS[i] + BreakfastOp2TotalK8[i];
                        BreakfastOp2Count.Add(total);
                    }
                    var calulateBreakfastOp2 = 3;
                    BreakfastOp2Count.ForEach(t => {
                        sheet.Cell(77, calulateBreakfastOp2).SetValue(t);
                        calulateBreakfastOp2++;
                    });

                    List<int> PizzaHSTotal = new List<int>();
                    var PizzaHSTotalStart = 3;
                    for (int i = 1; i <= MenuPizzaHS.Count; i++)
                    {
                        int total = Convert.ToInt32(sheet.Cell(33, PizzaHSTotalStart).Value);
                        PizzaHSTotal.Add(total);
                        PizzaHSTotalStart++;
                    }
                    List<int> PizzaK8Total = new List<int>();
                    var PizzaK8TotalStart = 3;
                    for (int i = 1; i <= MenuPizzaK8.Count; i++)
                    {
                        int total = Convert.ToInt32(sheet.Cell(34, PizzaK8TotalStart).Value);
                        PizzaK8Total.Add(total);
                        PizzaK8TotalStart++;
                    }
                    List<int> PizzaCount = new List<int>();
                    for (int i = 0; i <= MenuBreakfastOption2.Count - 1; i++)
                    {
                        int total = PizzaHSTotal[i] + PizzaK8Total[i];
                        PizzaCount.Add(total);
                    }
                    var calulatePizzaCount = 3;
                    PizzaCount.ForEach(t => {
                        sheet.Cell(35, calulatePizzaCount).SetValue(t);
                        calulatePizzaCount++;
                    });

                    List<int> SaladHSTotal = new List<int>();
                    var SaladHSTotalStart = 3;
                    for (int i = 1; i <= MenuSaladHS.Count; i++)
                    {
                        int total = Convert.ToInt32(sheet.Cell(42, SaladHSTotalStart).Value);
                        SaladHSTotal.Add(total);
                        SaladHSTotalStart++;
                    }
                    List<int> SaladK8Total = new List<int>();
                    var SaladK8TotalStart = 3;
                    for (int i = 1; i <= MenuPizzaK8.Count; i++)
                    {
                        int total = Convert.ToInt32(sheet.Cell(43, SaladK8TotalStart).Value);
                        SaladK8Total.Add(total);
                        SaladK8TotalStart++;
                    }
                    List<int> SaladCount = new List<int>();
                    for (int i = 0; i <= MenuSaladK8.Count - 1; i++)
                    {
                        int total = SaladHSTotal[i] + SaladK8Total[i];
                        SaladCount.Add(total);
                    }
                    var calulateSaladCount = 3;
                    SaladCount.ForEach(t => {
                        sheet.Cell(44, calulateSaladCount).SetValue(t);
                        calulateSaladCount++;
                    });
                    List<int> SupperCharTotal = new List<int>();
                    var SupperCharStart = 3;
                    for (int i = 1; i <= MenuSupperOption.Count; i++)
                    {
                        int total = Convert.ToInt32(sheet.Cell(45, SupperCharStart).Value);
                        SupperCharTotal.Add(total);
                        SupperCharStart++;
                    }
                    List<int> SupperVegTotal = new List<int>();
                    var SupperVegTotalK8Start = 3;
                    for (int i = 1; i <= MenuSupperVeg.Count; i++)
                    {
                        int total = Convert.ToInt32(sheet.Cell(48, SupperVegTotalK8Start).Value);
                        SupperVegTotal.Add(total);
                        SupperVegTotalK8Start++;
                    }
                    List<int> SupperSpecialTotalCount = new List<int>();
                    var SupperSpecialTotalStart = 3;
                    for (int i = 1; i <= MenuSupperSpecial.Count; i++)
                    {
                        int total = Convert.ToInt32(sheet.Cell(47, SupperSpecialTotalStart).Value);
                        SupperSpecialTotalCount.Add(total);
                        SupperSpecialTotalStart++;
                    }

                    List<int> SupperTotalCount = new List<int>();
                    for (int i = 0; i <= MenuSupperSpecial.Count - 1; i++)
                    {
                        int total = SupperCharTotal[i] + SupperVegTotal[i] + SupperSpecialTotalCount[i];
                        SupperTotalCount.Add(total);
                    }
                    var SupperVegTotalCount = 3;
                    SupperTotalCount.ForEach(t => {
                        sheet.Cell(49, SupperVegTotalCount).SetValue(t);
                        SupperVegTotalCount++;
                    });
                    List<int> MenuBreakfastOVSTotalCount = new List<int>();
                    for (int i = 0; i<= MenuBreakfastOVS1.Count - 1;i++)
                    {
                        int total = Convert.ToInt32(MenuBreakfastOVS1[i] + MenuBreakfastVegOVS1[i] + MenuBreakfastOp4OVS1[i] + MenuBreakfastOp1[i]);
                        MenuBreakfastOVSTotalCount.Add(total);
                    }
                    int daysCount = 3;
                    for (int i=0; i<= daysList.Count - 1;i++)
                    {
                        var days = daysList[i];
                        switch (days)
                        {
                            case "Mon":
                                sheet.Cell(78, daysCount).SetValue(Convert.ToInt32(MenuBreakfastOVSTotalCount[i] * (1.25)));
                                break;
                            case "Tue":
                                sheet.Cell(78, daysCount).SetValue(Convert.ToInt32(MenuBreakfastOVSTotalCount[i] * (0.25)));
                                break;
                            case "Wed":
                                sheet.Cell(78, daysCount).SetValue(Convert.ToInt32(MenuBreakfastOVSTotalCount[i] * (0.25)));
                                break;
                            case "Thu":
                                sheet.Cell(78, daysCount).SetValue(Convert.ToInt32(MenuBreakfastOVSTotalCount[i] * (1.25)));
                                break;
                            case "Fri":
                                sheet.Cell(78, daysCount).SetValue(Convert.ToInt32(MenuBreakfastOVSTotalCount[i] * (1.25)));
                                break;
                        }
                        daysCount++;
                    }

                    List<int> MenuBreakfastOSTotalCount = new List<int>();
                    for (int i = 0; i <= MenuBreakfastOVS2.Count - 1; i++)
                    {
                        int total = Convert.ToInt32(MenuBreakfastOVS2[i] + MenuBreakfastVegOVS2[i] + MenuBreakfastOp4OVS2[i] + MenuBreakfastOp2[i]);
                        MenuBreakfastOSTotalCount.Add(total);
                    }
                    int daysCount1 = 3;
                    for (int i = 0; i <= daysList.Count - 1; i++)
                    {
                        var days = daysList[i];
                        switch (days)
                        {
                            case "Mon":
                                sheet.Cell(79, daysCount1).SetValue(Convert.ToInt32(MenuBreakfastOSTotalCount[i] * (2)));
                                break;
                            case "Tue":
                                sheet.Cell(79, daysCount1).SetValue(Convert.ToInt32(MenuBreakfastOSTotalCount[i] * (1)));
                                break;
                            case "Wed":
                                sheet.Cell(79, daysCount1).SetValue(Convert.ToInt32(MenuBreakfastOSTotalCount[i] * (1)));
                                break;
                            case "Thu":
                                sheet.Cell(79, daysCount1).SetValue(Convert.ToInt32(MenuBreakfastOSTotalCount[i] * (2)));
                                break;
                            case "Fri":
                                sheet.Cell(79, daysCount1).SetValue(Convert.ToInt32(MenuBreakfastOSTotalCount[i] * (2)));
                                break;
                        }
                        daysCount1++;
                    }
                    int totalOVSOS1row = 3;
                    List<int> totalOVSOS1 = new List<int>();
                    MenuBreakfastOVSTotalCount.ForEach(s1 => {
                        int values = Convert.ToInt32(sheet.Cell(78, totalOVSOS1row).Value);
                        totalOVSOS1.Add(values);
                        totalOVSOS1row++;
                    });
                    int totalOVSOS2row = 3;
                    List<int> totalOVSOS2 = new List<int>();
                    MenuBreakfastOSTotalCount.ForEach(s1 => {
                        int values = Convert.ToInt32(sheet.Cell(79, totalOVSOS2row).Value);
                        totalOVSOS2.Add(values);
                        totalOVSOS2row++;
                    });
                    List<int> totalFruit = new List<int>();
                    for(int i=0;i<= MenuBreakfastOVSTotalCount.Count - 1; i++)
                    {
                        int count = totalOVSOS1[i] + totalOVSOS2[i];
                        totalFruit.Add(count);
                    }
                    int daysCount3 = 3;
                    totalFruit.ForEach(s =>
                    {
                        sheet.Cell(80, daysCount3).SetValue(s);
                        daysCount3++;
                    });

                    List<int> AllMenusHSItemes = new List<int>();
                    for(int i=0;i<= ManuLunchOption1HS.Count - 1; i++)
                    {
                        int count = Convert.ToInt32(ManuLunchOption1HS[i]+ ManuLunchOption2HS[i]+ MenuSackOptionHS1[i]+ MenuLunchVegHS[i]+ MenuPizzaHS[i]+ MenupickUpHS[i]+ MenuSackOptionHS2[i]+ MenuBBQHS[i]+ MenuSaladHS[i]);
                        AllMenusHSItemes.Add(count);
                    }
                    List<int> AllMenusK8Itemes = new List<int>();
                    for (int i = 0; i <= ManuLunchOption1K8.Count - 1; i++)
                    {
                        int count = Convert.ToInt32(ManuLunchOption1K8[i] + ManuLunchOption4[i] + ManuLunchOption2K8[i] + MenuSackOptionK81[i] + MenuSackOptionK82[i] + MenuLunchVegK8[i] + MenuPizzaK8[i] + MenupickUpK8[i] + MenuBBQK8[i] + MenuSaladK8[i] + MenuOnlyMS1[i]);
                        AllMenusK8Itemes.Add(count);
                    }
                    int daysCount2 = 3;
                    for (int i = 0; i <= daysList.Count - 1; i++)
                    {
                        var days = daysList[i];
                        switch (days)
                        {
                            case "Mon":
                                sheet.Cell(60, daysCount2).SetValue(Convert.ToInt32(AllMenusHSItemes[i] * (0.25)));
                                break;
                            case "Tue":
                                sheet.Cell(60, daysCount2).SetValue(Convert.ToInt32((AllMenusHSItemes[i] + AllMenusK8Itemes[i]) * (1.25)));
                                break;
                            case "Wed":
                                sheet.Cell(60, daysCount2).SetValue(Convert.ToInt32((AllMenusHSItemes[i] + AllMenusK8Itemes[i]) * (0.25)));
                                break;
                            case "Thu":
                                sheet.Cell(60, daysCount2).SetValue(Convert.ToInt32((AllMenusHSItemes[i] + AllMenusK8Itemes[i]) * (1.25)));
                                break;
                            case "Fri":
                                sheet.Cell(60, daysCount2).SetValue(Convert.ToInt32(AllMenusHSItemes[i] * (0.25)));
                                break;
                        }
                        daysCount2++;
                    }
                    
                    List<int> AllMenusItemes2 = new List<int>();
                    for (int i = 0; i <= ManuLunchOption1HS.Count - 1; i++)
                    {
                        int count = Convert.ToInt32(option1Count[i] + ManuLunchOption4[i] + option2Count[i] + sackCount[i] + sackVegCount[i] + LunchVegCount[i] + PizzaCount[i] + pickupStixCount[i] + BBQCount[i] + SaladCount[i] + MenuOnlyMS1[i]);
                        AllMenusItemes2.Add(count);
                    }
                    int daysCount4 = 3;
                    for (int i = 0; i <= daysList.Count - 1; i++)
                    {
                        var days = daysList[i];
                        switch (days)
                        {
                            case "Mon":
                                sheet.Cell(61, daysCount4).SetValue(Convert.ToInt32(AllMenusItemes2[i]));
                                break;
                            case "Tue":
                                sheet.Cell(61, daysCount4).SetValue("No Juice");
                                break;
                            case "Wed":
                                sheet.Cell(61, daysCount4).SetValue(Convert.ToInt32(AllMenusHSItemes[i]));
                                break;
                            case "Thu":
                                sheet.Cell(61, daysCount4).SetValue("No Juice");
                                break;
                            case "Fri":
                                sheet.Cell(61, daysCount4).SetValue(Convert.ToInt32(AllMenusItemes2[i]));
                                break;
                        }
                        daysCount4++;
                    }


                    var directory = String.Format(path + "Documents\\MASTER " + orderYear + " .xlsx");
                    if (!Directory.Exists(directory))
                        workBook.SaveAs(directory);
                    var filePath1 = Path.Combine(path, "Documents\\MASTER " + orderYear + " .xlsx");
                    var workBook1 = new XLWorkbook(filePath1);



                    workBook.SaveAs(filePath1);
                    response.FileName = filePath1;
                });
        }
    }
}