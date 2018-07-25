using System.Runtime.Serialization;
using Better4You.Meal.ViewModel;
using Tar.Service.Messages;

namespace Better4You.Meal.Service.Messages
{
    [DataContract]
    public class MealMenuOrderSubmitOrderRequest:Request
    {
        [DataMember]
        public MealMenuOrderFilterView Filter { get; set; }

        [DataMember]
        public long UserId { get; set; }

    }
}