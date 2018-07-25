using System;
using System.Runtime.Serialization;
using Tar.ViewModel;

namespace Better4You.Meal.ViewModel
{
    [DataContract]
    public class DailyItemsReportView : IView
    {
        [DataMember]
        public string SchoolCode { get; set; }

        [DataMember]
        public string SchoolName { get; set; }

        [DataMember]
        public string Route { get; set; }

        [DataMember]
        public string MenuName { get; set; }

        [DataMember]
        public string Type { get; set; }

        [DataMember]
        public string ServiceType { get; set; }

        [DataMember]

        public GeneralItemView MealType { get; set; }

        [DataMember]
        public int? FruitCount { get; set; }

        [DataMember]
        public int? VegetableCount { get; set; }

        [DataMember]
        public DateTime OrderDay { get; set; }
        [DataMember]
        public int? TotalCount { get; set; }

        [DataMember]
        public string LunchOVS { get; set; }

        [DataMember]
        public string BreakFast { get; set; }

        [DataMember]
        public long id { get; set; }

        [DataMember]
        public DateTime validate { get; set; }

        [DataMember]
        public long menuType { get; set; }


    }
}