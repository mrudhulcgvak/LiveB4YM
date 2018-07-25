using Better4You.Meal.Config;
using System.Collections.Generic;
using System.Linq;
using Tar.Core.Extensions;
using Tar.ViewModel;
using System;
using Better4You.Core;


namespace Better4You.Meal.ViewModel
{
    public class FoodPercentageView : IView
    {
        public long Id { get; set; }

        public long SchoolId { get; set; }

        public Tar.ViewModel.GeneralItemView MealType { get; set; }
        
        public int Fruit { get; set; }
        
        public int Vegetable { get; set; }
        public List<Tar.ViewModel.GeneralItemView> MealTypes
        {
            get
            {
                return Lookups.GetItems<MealTypes>().Where(x => x.Id != Config.MealTypes.None.ToInt64()).ToList();
            }
        }

    }
}