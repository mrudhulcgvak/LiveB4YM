using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using Tar.ViewModel;

namespace Better4You.Meal.ViewModel
{
    [DataContract]
    public class MealMenuOrderView : IView
    {
        [DataMember]
        public long Id { get; set; }
        
        [DataMember]
        public GeneralItemView OrderStatus { get; set; }

        [DataMember]
        public GeneralItemView RecordStatus { get; set; }
        
        [DataMember]
        public double? TotalAmount { get; set; }        

        [DataMember]
        public DateTime OrderDate { get; set; }

        [DataMember]
        public long SchoolId { get; set; }

        [DataMember]
        public List<MealMenuOrderItemView> OrderItems { get; set; }
        
        [DataMember]
        public GeneralItemView MealType { get; set; }

        [DataMember]
        public double? DebitAmount { get; set; }
        
        [DataMember]
        public string Note { get; set; }

        public MealMenuOrderView()
        {
            OrderItems = new List<MealMenuOrderItemView>();
        }
    }
}
