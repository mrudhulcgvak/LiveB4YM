using System.ServiceModel;
using Better4You.Meal.Service.Messages;

namespace Better4You.Meal.Service
{
    [ServiceContract]
    public interface IReportService
    {
        [OperationContract]
        ReportResponse MonthlyExport(MonthlyExportRequest request);

        // [OperationContract]
        // ReportResponse DaylunchExport(MontlyMilkExportRequest request);
        [OperationContract]
        ReportResponse DateRangeOrderItemExport(DateRangeOrderItemRequest request);
        [OperationContract]
        ReportResponse SchoolMenuExport(SchoolMenuExportRequest request);
        [OperationContract]
        ReportResponse MontlyMilkExport(MontlyMilkExportRequest request);
        [OperationContract]
        ReportResponse OrderDayPropItemExport(OrderDayPropItemRequest request);
        [OperationContract]
        ReportResponse DailyLunchReport(OrderDayPropItemRequest request);

        [OperationContract]
        ReportResponse DailyLunchRouteReport(OrderDayPropItemRequest request);
        [OperationContract]
        ReportResponse DailyBreakfastRouteReport(OrderDayPropItemRequest request); 

        [OperationContract]
        ReportResponse DailySupperReport(OrderDayPropItemRequest request);

        [OperationContract]
        ReportResponse DailyBreakfastReport(OrderDayPropItemRequest request);

        [OperationContract]
        ReportResponse MonthlyPurchacseReport(OrderDayPropItemRequest request);


    }
}
