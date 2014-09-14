using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using LrcMusicPlayer.Common;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;

// The Item Detail Page item template is documented at http://go.microsoft.com/fwlink/?LinkId=234232

namespace LrcMusicPlayer
{
    /// <summary>
    /// A page that displays details for a single item within a group while allowing gestures to
    /// flip through other items belonging to the same group.
    /// </summary>
    public sealed partial class ItemListPageView : Page
    {
        private MainPage rootPage = MainPage.Current;
        private NavigationHelper navigationHelper;

        /// <summary>
        /// NavigationHelper is used on each page to aid in navigation and 
        /// process lifetime management
        /// </summary>
        public NavigationHelper NavigationHelper {
            get { return this.navigationHelper; }
        }

        public ItemListPageView() {
            this.InitializeComponent();
            this.navigationHelper = new NavigationHelper(this);
            this.navigationHelper.LoadState += navigationHelper_LoadState;
            var appBar = rootPage.BottomAppBar.Content as Grid;
            var leftPanel = appBar.Children[0] as StackPanel;
            RightPanel = appBar.Children[1] as StackPanel;
            FavoriteButton = leftPanel.Children[0] as AppBarToggleButton;
            this.itemGridView.ItemsSource = MainPage.Playlist.Items;
        }

        /// <summary>
        /// Populates the page with content passed during navigation.  Any saved state is also
        /// provided when recreating a page from a prior session.
        /// </summary>
        /// <param name="sender">
        /// The source of the event; typically <see cref="NavigationHelper"/>
        /// </param>
        /// <param name="e">Event data that provides both the navigation parameter passed to
        /// <see cref="Frame.Navigate(Type, Object)"/> when this page was initially requested and
        /// a dictionary of state preserved by this page during an earlier
        /// session.  The state will be null the first time a page is visited.</param>
        private void navigationHelper_LoadState(object sender, LoadStateEventArgs e) {
            object navigationParameter;
            if (e.PageState != null && e.PageState.ContainsKey("SelectedItem")) {
                navigationParameter = e.PageState["SelectedItem"];
            }

            // TODO: Assign a bindable group to this.DefaultViewModel["Group"]
            // TODO: Assign a collection of bindable items to this.DefaultViewModel["Items"]
            // TODO: Assign the selected item to this.flipView.SelectedItem
        }

        /// <summary>
        /// This is the click handler for the 'Back' button.  When clicked we want to go back to the main sample page
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void Back_Click(object sender, RoutedEventArgs e) {
            MainPage.Current.Frame.GoBack();
        }

        private async void DeleteFilesButton_Click(object sender, RoutedEventArgs e) {
            var selectedItems = itemGridView.SelectedItems
                .Select(c => itemGridView.Items.IndexOf(c))
                .OrderBy(c => c).ToArray();
            for (int i = 0; i < selectedItems.Count(); i++) {
                var item = MainPage.Playlist.Items[selectedItems[i] - i];
                MainPage.Playlist.Items.Remove(item);
            }
            await MainPage.Playlist.Save();
        }

        private void SelectAllButton_Click(object sender, RoutedEventArgs e) {
            if (SelectAllButton.IsChecked.Value) {
                itemGridView.SelectAll();
                if (itemGridView.Items.Count == 0) SelectAllButton.IsChecked = false;
            } else {
                itemGridView.SelectedIndex = -1;
            }
        }

        #region NavigationHelper registration

        /// The methods provided in this section are simply used to allow
        /// NavigationHelper to respond to the page's navigation methods.
        /// 
        /// Page specific logic should be placed in event handlers for the  
        /// <see cref="GridCS.Common.NavigationHelper.LoadState"/>
        /// and <see cref="GridCS.Common.NavigationHelper.SaveState"/>.
        /// The navigation parameter is available in the LoadState method 
        /// in addition to page state preserved during an earlier session.

        protected override void OnNavigatedTo(NavigationEventArgs e) {
            navigationHelper.OnNavigatedTo(e);
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e) {
            navigationHelper.OnNavigatedFrom(e);
        }

        #endregion
    }
}
