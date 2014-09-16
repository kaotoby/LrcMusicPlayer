using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Input;

namespace LrcMusicPlayer.Common
{
    public class MyListView : ListView
    {
        public event RightTappedEventHandler ItemRightTapped;

        protected override DependencyObject GetContainerForItemOverride() {
            var item = new MyListViewItem();
            item.RightTapped += OnItemRightTapped;
            return item;
        }

        protected virtual void OnItemRightTapped(object sender, RightTappedRoutedEventArgs args) {
            if (ItemRightTapped != null)
                ItemRightTapped(sender, args);
            args.Handled = true;
        }
    }

    public class MyListViewItem : ListViewItem
    {
        protected override void OnRightTapped(RightTappedRoutedEventArgs e) {
            base.OnRightTapped(e);

            // Stop 'swallowing' the event
            e.Handled = false;
        }
    }

    public class MySliderValue : IValueConverter
    {

        public object Convert(object value, Type targetType, object parameter, string language) {
            var newTime = TimeSpan.FromSeconds((double)value);
            return string.Format("{0} / {1}", newTime.ToString("mm\\:ss"),
                MainPage.MyMediaElement.NaturalDuration.TimeSpan.ToString("mm\\:ss"));
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language) {
            throw new NotImplementedException();
        }
    }
}
