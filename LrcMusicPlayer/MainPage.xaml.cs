using LrcMusicPlayer.Common;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Windows.Input;
using Windows.Media;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.Storage.Streams;
using Windows.Storage.AccessCache;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.UI.ViewManagement;
using Windows.UI.Xaml;
using Windows.UI.Core;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;
using Windows.UI.Xaml.Media.Imaging;
using flaclib;
using FLACSource

// The Split Page item template is documented at http://go.microsoft.com/fwlink/?LinkId=234234

namespace LrcMusicPlayer
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
    
    /// <summary>
    /// A page that displays a group title, a list of items within the group, and details for
    /// the currently selected item.
    /// </summary>
    public sealed partial class MainPage : Page
    {
        #region Declar
        public static PlayList Playlist = null;
        private Dictionary<TimeSpan, string> lrcData;
        private PlayListItem currentItem;
        private ImageSource none;
        private int lastLrc = -1;
        private bool lyricFingerDown = false, itemListViewIsTapped = false, sliderPressed = false;
        private PlayStyle playMode = PlayStyle.RepeatOnce;
        public static MediaElement MyMediaElement = null;
        
        private NavigationHelper navigationHelper;
        private DispatcherTimer lrcTimer = new DispatcherTimer();
        private DispatcherTimer _timer;
        public static MainPage Current;
        private bool isInitialized = false;

        /// <summary>
        /// Indicates whether this scenario page is still active. Changes value during navigation 
        /// to or away from the page.
        /// </summary>
        private bool isThisPageActive = false;

        // same type as returned from Windows.Storage.Pickers.FileOpenPicker.PickMultipleFilesAsync()
        //private IReadOnlyList<StorageFile> playFileList = null;

        /// <summary>
        /// index to current media item in playFileList
        /// </summary>
        private int currentItemIndex = -1;

        private SystemMediaTransportControls systemMediaControls = null;

        /// <summary>
        /// NavigationHelper is used on each page to aid in navigation and 
        /// process lifetime management
        /// </summary>
        public NavigationHelper NavigationHelper {
            get { return this.navigationHelper; }
        }
        #endregion
        
        #region Loading
        public MainPage() {
            this.InitializeComponent();

            // Setup the navigation helper
            this.navigationHelper = new NavigationHelper(this);
            this.navigationHelper.LoadState += navigationHelper_LoadState;
            this.navigationHelper.SaveState += navigationHelper_SaveState;

            // Setup the logical page navigation components that allow
            // the page to only show one pane at a time.
            this.navigationHelper.GoBackCommand = new LrcMusicPlayer.Common.RelayCommand(() => this.GoBack(), () => this.CanGoBack());

            // Start listening for Window size changes 
            // to change from showing two panes to showing a single pane
            Current = this;
            Window.Current.SizeChanged += Window_SizeChanged;
            this.InvalidateVisualState();
            this.none = mainCoverImage.Source;
        }

        private async void pageRoot_Loaded(object sender, RoutedEventArgs e) {
            lrcTimer.Interval = new TimeSpan(0, 0, 0, 0, 10);
            lrcTimer.Tick += lrcTimer_Tick;
            uiNextButton.Visibility = uiPrevButton.Visibility = LeftPanel.Visibility = Visibility.Collapsed;
            MuteButton.AddHandler(TappedEvent, new TappedEventHandler(MuteButton_Tapped), false);
            timelineSlider.AddHandler(PointerPressedEvent, new PointerEventHandler(timelineSlider_PointerEntered), true);
            timelineSlider.AddHandler(PointerCaptureLostEvent, new PointerEventHandler(timelineSlider_PointerCaptureLost), true);

            if (MyMediaElement == null || MyMediaElement.CurrentState == MediaElementState.Stopped) timelineSlider.IsEnabled = false;
            if (MyMediaElement == null) {
                DependencyObject rootGrid = VisualTreeHelper.GetChild(Window.Current.Content, 0);
                MyMediaElement = (MediaElement)VisualTreeHelper.GetChild(rootGrid, 0);
                MyMediaElement.CurrentStateChanged += MyMediaElement_CurrentStateChanged;
                MyMediaElement.MediaOpened += MyMediaElement_MediaOpened;
                MyMediaElement.MediaEnded += MyMediaElement_MediaEnded;
                MyMediaElement.MediaFailed += MyMediaElement_MediaFailed;
                VolumeSlider.Value = MyMediaElement.Volume * 100;
            }
            if (Playlist == null) {
                Playlist = await PlayList.LoadFromFile();
                if (Playlist.Items.Count == 0) {
                    stateTextBlock.Text = "Swipe from the buttom to add files.";
                } else {
                    stateTextBlock.Text = "Stopped";
                }
            } else {
                if (currentItem == null) stateTextBlock.Text = "Stopped";
            }
            itemListView.ItemsSource = Playlist.Items;
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
            // TODO: Assign a bindable group to Me.DefaultViewModel("Group")
            // TODO: Assign a collection of bindable items to Me.DefaultViewModel("Items")

            if (e.PageState == null) {
                // When this is a new page, select the first item automatically unless logical page
                // navigation is being used (see the logical page navigation #region below.)
                //if (!this.UsingLogicalPageNavigation() && this.itemsViewSource.View != null) {
                //    this.itemsViewSource.View.MoveCurrentToFirst();
                //}
            } else {
                // Restore the previously saved state associated with this page
                //if (e.PageState.ContainsKey("SelectedItem") && this.itemsViewSource.View != null) {
                //    // TODO: Invoke Me.itemsViewSource.View.MoveCurrentTo() with the selected
                //    //       item as specified by the value of pageState("SelectedItem")

                //}
            }
        }

        /// <summary>
        /// Preserves state associated with this page in case the application is suspended or the
        /// page is discarded from the navigation cache.  Values must conform to the serialization
        /// requirements of <see cref="SuspensionManager.SessionState"/>.
        /// </summary>
        /// <param name="sender">The source of the event; typically <see cref="NavigationHelper"/></param>
        /// <param name="e">Event data that provides an empty dictionary to be populated with
        /// serializable state.</param>
        private void navigationHelper_SaveState(object sender, SaveStateEventArgs e) {
            //if (this.itemsViewSource.View != null) {
            //    // TODO: Derive a serializable navigation parameter and assign it to
            //    //       pageState("SelectedItem")

            //}
        }
        #endregion

        #region Logical page navigation
        // The split page isdesigned so that when the Window does have enough space to show
        // both the list and the dteails, only one pane will be shown at at time.
        //
        // This is all implemented with a single physical page that can represent two logical
        // pages.  The code below achieves this goal without making the user aware of the
        // distinction.

        private const int MinimumWidthForSupportingTwoPanes = 300;

        /// <summary>
        /// Invoked to determine whether the page should act as one logical page or two.
        /// </summary>
        /// <returns>True if the window should show act as one logical page, false
        /// otherwise.</returns>
        private bool UsingLogicalPageNavigation() {
            return Window.Current.Bounds.Width < MinimumWidthForSupportingTwoPanes;
        }

        /// <summary>
        /// Invoked with the Window changes size
        /// </summary>
        /// <param name="sender">The current Window</param>
        /// <param name="e">Event data that describes the new size of the Window</param>
        private void Window_SizeChanged(object sender, Windows.UI.Core.WindowSizeChangedEventArgs e) {
            this.InvalidateVisualState();
        }        

        private bool CanGoBack() {
            if (this.UsingLogicalPageNavigation() && this.itemListView.SelectedItem != null) {
                return true;
            } else {
                return this.navigationHelper.CanGoBack();
            }
        }
        private void GoBack() {
            if (this.UsingLogicalPageNavigation() && this.itemListView.SelectedItem != null) {
                // When logical page navigation is in effect and there's a selected item that
                // item's details are currently displayed.  Clearing the selection will return to
                // the item list.  From the user's point of view this is a logical backward
                // navigation.
                this.itemListView.SelectedItem = null;
            } else {
                this.navigationHelper.GoBack();
            }
        }

        private void InvalidateVisualState() {
            var visualState = DetermineVisualState();
            VisualStateManager.GoToState(this, visualState, false);
            this.navigationHelper.GoBackCommand.RaiseCanExecuteChanged();
        }

        /// <summary>
        /// Invoked to determine the name of the visual state that corresponds to an application
        /// view state.
        /// </summary>
        /// <returns>The name of the desired visual state.  This is the same as the name of the
        /// view state except when there is a selected item in portrait and snapped views where
        /// this additional logical page is represented by adding a suffix of _Detail.</returns>
        private string DetermineVisualState() {
            if (!UsingLogicalPageNavigation())
                return "PrimaryView";

            // Update the back button's enabled state when the view state changes
            var logicalPageBack = this.UsingLogicalPageNavigation() && this.itemListView.SelectedItem != null;

            return logicalPageBack ? "SinglePane_Detail" : "SinglePane";
        }

        protected override void OnNavigatedTo(NavigationEventArgs e) {
            // A pointer back to the main page.  This is needed if you want to call methods in MainPage such
            // as NotifyUser()
            isThisPageActive = true;

            // retrieve the SystemMediaTransportControls object, register for its events, and configure it 
            // to a disabled state consistent with how this scenario starts off (ie. no media loaded)
            SetupSystemMediaTransportControls();

            if (!isInitialized) {
                isInitialized = true;
            }
        }

        /// <summary>
        /// Invoked when we are about to leave this page.
        /// </summary>
        /// <param name="e">Event data that describes how this page was reached.  The Parameter
        /// property is typically used to configure the page.</param>
        protected override void OnNavigatedFrom(NavigationEventArgs e) {
            isThisPageActive = false;

            // Because the system media transport control object is associated with the current app view
            // (ie. window), all scenario pages in this sample will be using the same instance. Therefore 
            // we need to remove the ButtonPressed event handler specific to this scenario page before 
            // user navigates away to another scenario in the sample.
            systemMediaControls.ButtonPressed -= systemMediaControls_ButtonPressed;

            // Perform other cleanup for this scenario page.
            //MyMediaElement.Source = null;
            currentItemIndex = -1;
        }
        #endregion

        #region AppBar.LeftPenal.Event
        private async void FavoriteButton_Click(object sender, RoutedEventArgs e) {
            if (LoadFavoriteButton.Text != "\uE0A5  Favorite") {
                FavoriteButton.IsChecked = true;
                return;
            }
            foreach (var item in itemListView.SelectedItems.Select(c => c as PlayListItem)) {
                item.IsFavorite = FavoriteButton.IsChecked.Value;
            }
            await Playlist.SaveFavorite();
        }

        private void SelectAllButton_Click(object sender, RoutedEventArgs e) {
            if (itemListView.SelectedItems.Count < itemListView.Items.Count) {
                itemListView.SelectAll();
            } else {
                itemListView.SelectedIndex = -1;
            }
        }

        private void FavoriteButtonBoarder_Tapped(object sender, TappedRoutedEventArgs e) {
            FavoriteButtonBoarder.Visibility = Visibility.Collapsed;
            FavoriteButton.IsEnabled = true;
            FavoriteButton_Click(this, null);
        }

        private async void DeleteFilesButton_Click(object sender, RoutedEventArgs e) {
            if (itemListView.SelectedItems.Count == 0) return;
            bool currentDeleted = false;
            var items = itemListView.SelectedItems.Select(c => c as PlayListItem).ToArray();

            for (int i = 0; i < items.Count(); i++) {
                if (items[i] == currentItem) {
                    currentDeleted = true;
                    continue;
                }
                Playlist.Items.Remove(items[i]);
            }
            currentItemIndex = Playlist.Items.IndexOf(currentItem);
            if (currentDeleted) Playlist.Items.Remove(currentItem);

            await Playlist.Save();
        }

        private void PageViewButton_Click(object sender, RoutedEventArgs e) {
            Current.Frame.Navigate(typeof(ItemListPageView), Current);
        }

        private void MyAppBar_Closed(object sender, object e) {
            if (LeftPanel.Visibility == Visibility.Visible) {
                LeftPanel.Visibility = Visibility.Collapsed;
                itemListView.SelectionMode = ListViewSelectionMode.Single;
                itemListView.SelectedIndex = currentItemIndex;
                itemListView.AllowDrop = itemListView.IsSwipeEnabled = itemListView.CanReorderItems = false;
                currentItemIndex = Playlist.Items.IndexOf(currentItem);
            }
        }

        private void ShowMyAppBar() {
            if (MyAppBar.IsOpen) return;
            itemListView.SelectionMode = ListViewSelectionMode.Multiple;
            MyAppBar.IsOpen = MyAppBar.IsSticky = true;
            itemListView.AllowDrop = itemListView.IsSwipeEnabled = itemListView.CanReorderItems = true;
            LeftPanel.Visibility = Visibility.Visible;
        }
        #endregion

        #region AppBar.RightPenal.Event
        // For supported audio and video formats for Windows Store apps, see:
        //     http://msdn.microsoft.com/en-us/library/windows/apps/hh986969.aspx
        private static string[] supportedAudioFormats = new string[]
        {
            ".3g2", ".3gp2", ".3gp", ".3gpp", ".m4a", ".mp4", ".asf", ".wma", ".aac", ".adt", ".adts", ".mp3", ".wav", ".ac3", ".ec3", ".flac", ".lrc"
        };

        /// <summary>
        /// Invoked when user invokes the "Select Files" XAML button in the app.
        /// Launches the Windows File Picker to let user select a list of audio or video files 
        //  to play in the app.
        /// </summary>
        private async void AddFilesButton_Click(object sender, RoutedEventArgs ev) {
            FileOpenPicker filePicker = new FileOpenPicker();
            filePicker.ViewMode = PickerViewMode.List;
            filePicker.SuggestedStartLocation = PickerLocationId.MusicLibrary;
            filePicker.CommitButtonText = "Add";
            foreach (string fileExtension in supportedAudioFormats) {
                filePicker.FileTypeFilter.Add(fileExtension);
            }

            IReadOnlyList<StorageFile> selectedFiles = await filePicker.PickMultipleFilesAsync();
            LoadSelectedFiles(selectedFiles);
        }

        private async void AddFolderButton_Click(object sender, RoutedEventArgs ev) {
            FolderPicker folderPicker = new FolderPicker();
            folderPicker.ViewMode = PickerViewMode.List;
            folderPicker.SuggestedStartLocation = PickerLocationId.MusicLibrary;
            folderPicker.CommitButtonText = "Add";
            foreach (string fileExtension in supportedAudioFormats) {
                folderPicker.FileTypeFilter.Add(fileExtension);
            }

            var selectedFolder = await folderPicker.PickSingleFolderAsync();
            if (selectedFolder == null) return;
            IReadOnlyList<StorageFile> selectedFiles = await selectedFolder.GetFilesAsync();
            LoadSelectedFiles(selectedFiles.Where(c => supportedAudioFormats.Contains(c.FileType)));
        }

        private async void LoadSelectedFiles(IEnumerable<StorageFile> selectedFiles) {
            if (selectedFiles.Count() <= 0) return;
            stateTextBlock.Text = "Loading Files . . .";
            await PlayList.LoadStorageFiles(selectedFiles, Playlist);
            stateTextBlock.Text = "Loading Complete";
            systemMediaControls.IsEnabled = true;
            if (currentItemIndex < 0) await SetNewMediaItem(0);
        }

        private async void LoadFavoriteButton_Click(object sender, RoutedEventArgs e) {
            MyAppBar.IsSticky = MyAppBar.IsOpen = false;
            stateTextBlock.Text = "Loading Playlist . . .";
            try {
                StorageFolder folder = ApplicationData.Current.RoamingFolder;
                StorageFile file;
                string lableText;
                if (LoadFavoriteButton.Text == "\uE0A5  Favorite") {
                    file = await folder.GetFileAsync("favorite.lmp");
                    lableText = "\uE10E  Go Back";
                } else {
                    file = await folder.GetFileAsync("playlist.lmp");
                    lableText = "\uE0A5  Favorite";
                }
                Playlist = await PlayList.LoadFromFile(file);
                itemListView.ItemsSource = Playlist.Items;
                LoadFavoriteButton.Text = lableText;
                await SetNewMediaItem(0);
                stateTextBlock.Text = "Loading Complete";
            } catch (Exception) {
                stateTextBlock.Text = "No File In Playlist";
            }
        }

        private async void LoadFromFileButton_Click(object sender, RoutedEventArgs e) {
            MyAppBar.IsSticky = MyAppBar.IsOpen = false;
            FileOpenPicker filePicker = new FileOpenPicker();
            filePicker.ViewMode = PickerViewMode.List;
            filePicker.SuggestedStartLocation = PickerLocationId.MusicLibrary;
            filePicker.CommitButtonText = "Play";
            filePicker.FileTypeFilter.Add(".lmp");
            var selectedFile = await filePicker.PickSingleFileAsync();
            if (selectedFile == null) return;
            stateTextBlock.Text = "Loading Playlist . . .";
            if (LoadFavoriteButton.Text != "\uE0A5  Favorite") LoadFavoriteButton.Text = "\uE0A5  Favorite";
            Playlist = await PlayList.LoadFromFile(selectedFile);
            itemListView.ItemsSource = Playlist.Items;
            await SetNewMediaItem(0);
        }

        private async void SavePlaylistButton_Click(object sender, RoutedEventArgs e) {
            FileSavePicker filePicker = new FileSavePicker();
            filePicker.SuggestedStartLocation = PickerLocationId.MusicLibrary;
            filePicker.DefaultFileExtension = ".lmp";
            filePicker.FileTypeChoices.Add("LM Player Playlist(*.lmp)", new string[] { ".lmp" });

            var selectedFile = await filePicker.PickSaveFileAsync();
            if (selectedFile == null) return;
            await Playlist.Save(selectedFile);
            stateTextBlock.Text = "Playlist Saved";
        }

        private void PlayModeButton_Click(object sender, RoutedEventArgs e) {
            MenuFlyoutItem _sender = sender as MenuFlyoutItem;
            var data = _sender.Text.Split(new string[] { "  " }, StringSplitOptions.None);
            FontIcon icon = playModeButton.Icon as FontIcon;
            icon.Glyph = data[0];
            playModeButton.Label = data[1];
            switch (data[1]) {
                case "Repeat Once":
                    playMode = PlayStyle.RepeatOnce;
                    break;
                case "Repeat All":
                    playMode = PlayStyle.RepeatAll;
                    break;
                case "Shuffle":
                    playMode = PlayStyle.Shuffle;
                    break;
                case "Repeat Song":
                    playMode = PlayStyle.RepeatSong;
                    break;
                case "Single Song":
                    playMode = PlayStyle.SingleSong;
                    break;
            }
        }
        #endregion

        #region MyMediaElement.Event
        private async void PlayMediaElement() {
            if (MyMediaElement.CurrentState == MediaElementState.Stopped) {
                systemMediaControls.IsEnabled = true;
                int selectedIndex = itemListView.SelectedIndex;
                await SetNewMediaItem(selectedIndex == -1 ? 0 : selectedIndex);
            } else {
                MyMediaElement.Play();
            }
        }

        private async void NextMediaElement() {

        }

        private async void PrevMediaElement() {

        }

        private void SetupTimer() {
            _timer = new DispatcherTimer();
            _timer.Interval = TimeSpan.FromSeconds(timelineSlider.StepFrequency);
            StartTimer();
        }

        private void _timer_Tick(object sender, object e) {
            if (!sliderPressed) {
                var time = MyMediaElement.Position;
                timelineSlider.Value = time.TotalSeconds;
                MediaTimeNowText.Text = time.ToString("mm\\:ss");
            }
        }

        private void StartTimer() {
            _timer.Tick += _timer_Tick;
            _timer.Start();
        }

        private void StopTimer() {
            _timer.Stop();
            _timer.Tick -= _timer_Tick;
        }

        private async void MyMediaElement_MediaOpened(object sender, RoutedEventArgs e) {
            if (currentItem == null) return;
            var totalTime = MyMediaElement.NaturalDuration.TimeSpan;
            MediaTimeTotalText.Text = totalTime.ToString("mm\\:ss");
            timelineSlider.Maximum = totalTime.TotalSeconds;
            timelineSlider.StepFrequency = 0.01;

            SetupTimer();
            if (currentItem.LrcToken!="") {
                lrcData = await PlayListItem.GetLyrics(currentItem.LrcToken);

                var data = lrcData.Values.ToList();
                data.Insert(0, "\n\n\n\n");
                data.Add("\n\n\n\n");
                foreach (var item in data) {
                    TextBlock lyric = new TextBlock();
                    lyric.Style = this.Resources["LyricTextStyle"] as Style;
                    lyric.Text = item;
                    lyric.LineHeight = 60;

                    lyricsPanel.Children.Add(lyric);
                }
                lrcTimer.Start();
            } else {
                lrcData = null;
            }
            MyMediaElement.Play();
        }

        private async void MyMediaElement_MediaEnded(object sender, RoutedEventArgs e) {
            if (currentItem == null) return;
            switch (playMode) {
                case PlayStyle.RepeatOnce:
                    if (currentItemIndex < Playlist.Items.Count - 1) {
                        await SetNewMediaItem(currentItemIndex + 1);
                    } else {
                        MyMediaElement.Stop();
                    }
                    break;
                case PlayStyle.RepeatAll: if (currentItemIndex < Playlist.Items.Count - 1) {
                        await SetNewMediaItem(currentItemIndex + 1);
                    } else {
                        await SetNewMediaItem(0);
                    }
                    break;
                case PlayStyle.Shuffle:
                    Random r = new Random();
                    int next = r.Next(Playlist.Items.Count);
                    while (next == currentItemIndex) next = r.Next(Playlist.Items.Count);
                    await SetNewMediaItem(next);
                    break;
                case PlayStyle.RepeatSong:
                    MyMediaElement.Play();
                    break;
                case PlayStyle.SingleSong:
                    MyMediaElement.Stop();
                    break;
                default:
                    break;
            }
            
        }

        private void MyMediaElement_MediaFailed(object sender, ExceptionRoutedEventArgs e) {
            if (isThisPageActive) {
                string errorMessage = String.Format(@"Cannot play {0} [""{1}""]." +
                    "\nPress Next or Previous to continue, or select new files to play.",
                    Playlist.Items[currentItemIndex].FileToken,
                    e.ErrorMessage.Trim());
                //rootPage.NotifyUser(errorMessage, NotifyType.ErrorMessage);
            }
        }

        private void MyMediaElement_CurrentStateChanged(object sender, RoutedEventArgs e) {
            if (currentItem == null) return;
            if (!isThisPageActive) {
                return;
            }
            switch (MyMediaElement.CurrentState) {
                case MediaElementState.Paused:
                    lrcTimer.Stop();
                    MyMediaElement.AutoPlay = false;
                    uiPlayPauseButton.Content = "\uE102";
                    break;
                case MediaElementState.Playing:
                    if (lrcData != null) lrcTimer.Start();
                    MyMediaElement.AutoPlay = true;
                    uiPlayPauseButton.Content = "\uE103";
                    break;
                case MediaElementState.Stopped:
                    lrcTimer.Stop();
                    MyMediaElement.AutoPlay = false;
                    uiPlayPauseButton.Content = "\uE102";
                    systemMediaControls.IsEnabled = false;
                    mainTitleText.Text = "Lrc Music Player";
                    mainArtistText.Text = "Madoka Magica";
                    mainAlbumText.Text = "Few Moe Project";
                    lyricsPanel.Children.Clear();
                    mainCoverImage.Source = none;
                    timelineSlider.IsEnabled = false;
                    break;
            }
            stateTextBlock.Text = MyMediaElement.CurrentState.ToString();
            SyncPlaybackStatusToMediaElementState();
        }
        #endregion

        #region SystemMediaTransportControls.Event
        /// <summary>
        /// Invoked from this scenario page's OnNavigatedTo event handler.  Retrieve and initialize the
        /// SystemMediaTransportControls object.
        /// </summary>
        private void SetupSystemMediaTransportControls() {
            // Retrieve the SystemMediaTransportControls object associated with the current app view
            // (ie. window).  There is exactly one instance of the object per view, instantiated by
            // the system the first time GetForCurrentView() is called for the view.  All subsequent 
            // calls to GetForCurrentView() from the same view (eg. from different scenario pages in 
            // this sample) will return the same instance of the object.
            systemMediaControls = SystemMediaTransportControls.GetForCurrentView();

            // This scenario will always start off with no media loaded, so we will start off disabling the 
            // system media transport controls.  Doing so will hide the system UI for media transport controls
            // from being displayed, and will prevent the app from receiving any events such as ButtonPressed 
            // from it, regardless of the current state of event registrations and button enable/disable states.
            // This makes IsEnabled a handy way to turn system media transport controls off and back on, as you 
            // may want to do when the user navigates to and away from certain parts of your app.
            systemMediaControls.IsEnabled = false;

            // To receive notifications for the user pressing media keys (eg. "Stop") on the keyboard, or 
            // clicking/tapping on the equivalent software buttons in the system media transport controls UI, 
            // all of the following needs to be true:
            //     1. Register for ButtonPressed event on the SystemMediaTransportControls object.
            //     2. IsEnabled property must be true to enable SystemMediaTransportControls itself.
            //        [Note: IsEnabled is initialized to true when the system instantiates the
            //         SystemMediaTransportControls object for the current app view.]
            //     3. For each button you want notifications from, set the corresponding property to true to
            //        enable the button.  For example, set IsPlayEnabled to true to enable the "Play" button 
            //        and media key.
            //        [Note: the individual button-enabled properties are initialized to false when the
            //         system instantiates the SystemMediaTransportControls object for the current app view.]
            //
            // Here we'll perform 1, and 3 for the buttons that will always be enabled for this scenario (Play,
            // Pause, Stop).  For 2, we purposely set IsEnabled to false to be consistent with the scenario's 
            // initial state of no media loaded.  Later in the code where we handle the loading of media
            // selected by the user, we will enable SystemMediaTransportControls.
            systemMediaControls.ButtonPressed += systemMediaControls_ButtonPressed;

            // Note: one of the prerequisites for an app to be allowed to play audio while in background, 
            // is to enable handling Play and Pause ButtonPressed events from SystemMediaTransportControls.
            systemMediaControls.IsPlayEnabled = true;
            systemMediaControls.IsPauseEnabled = true;
            systemMediaControls.IsStopEnabled = true;
            systemMediaControls.PlaybackStatus = MediaPlaybackStatus.Closed;
        }

        /// <summary>
        /// Updates the SystemMediaTransportControls' PlaybackStatus property based on the CurrentState property of the
        /// MediaElement control.
        /// </summary>
        /// <remarks>Invoked mainly from the MediaElement control's CurrentStateChanged event handler.</remarks>
        private void SyncPlaybackStatusToMediaElementState() {
            // Updating PlaybackStatus with accurate information is important; for example, it determines whether the system media
            // transport controls UI will show a play vs pause software button, and whether hitting the play/pause toggle key on 
            // the keyboard will translate to a Play vs a Pause ButtonPressed event.
            //
            // Even if your app uses its own custom transport controls in place of the built-in ones from XAML, it is still a good
            // idea to update PlaybackStatus in response to the MediaElement's CurrentStateChanged event.  Windows supports scenarios 
            // such as streaming media from a MediaElement to a networked device (eg. TV) selected by the user from Devices charm 
            // (ie. "Play To"), in which case the user may pause and resume media streaming using a TV remote or similar means.  
            // The CurrentStateChanged event may be the only way to get notified of playback status changes in those cases.
            switch (MyMediaElement.CurrentState) {
                case MediaElementState.Closed:
                    systemMediaControls.PlaybackStatus = MediaPlaybackStatus.Closed;
                    break;

                case MediaElementState.Opening:
                    // This state is when new media is being loaded to the XAML MediaElement control [ie.
                    // SetSource()].  For this sample the design is to maintain the previous playing/pause 
                    // state before the new media is being loaded.  So we'll leave the PlaybackStatus alone
                    // during loading.  This keeps the system UI from flickering between displaying a "Play" 
                    // vs "Pause" software button during the transition to a new media item.
                    break;

                case MediaElementState.Buffering:
                    // No updates in MediaPlaybackStatus necessary--buffering is just
                    // a transitional state where the system is still working to get
                    // media to start or to continue playing.
                    break;

                case MediaElementState.Paused:
                    systemMediaControls.PlaybackStatus = MediaPlaybackStatus.Paused;
                    break;

                case MediaElementState.Playing:
                    systemMediaControls.PlaybackStatus = MediaPlaybackStatus.Playing;
                    break;

                case MediaElementState.Stopped:
                    systemMediaControls.PlaybackStatus = MediaPlaybackStatus.Stopped;
                    break;
            }
        }

        private async void systemMediaControls_ButtonPressed(SystemMediaTransportControls sender, SystemMediaTransportControlsButtonPressedEventArgs args) {
            // The system media transport control's ButtonPressed event may not fire on the app's UI thread.  XAML controls 
            // (including the MediaElement control in our page as well as the scenario page itself) typically can only be 
            // safely accessed and manipulated on the UI thread, so here for simplicity, we dispatch our entire event handling 
            // code to execute on the UI thread, as our code here primarily deals with updating the UI and the MediaElement.
            // 
            // Depending on how exactly you are handling the different button presses (which for your app may include buttons 
            // not used in this sample scenario), you may instead choose to only dispatch certain parts of your app's 
            // event handling code (such as those that interact with XAML) to run on UI thread.
            await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, async () =>
            {
                // Because the handling code is dispatched asynchronously, it is possible the user may have
                // navigated away from this scenario page to another scenario page by the time we are executing here.
                // Check to ensure the page is still active before proceeding.
                if (isThisPageActive) {
                    switch (args.Button) {
                        case SystemMediaTransportControlsButton.Play:
                            //rootPage.NotifyUser("Play pressed", NotifyType.StatusMessage);
                            PlayMediaElement();
                            break;

                        case SystemMediaTransportControlsButton.Pause:
                            //rootPage.NotifyUser("Pause pressed", NotifyType.StatusMessage);
                            MyMediaElement.Pause();
                            break;

                        case SystemMediaTransportControlsButton.Stop:
                            //rootPage.NotifyUser("Stop pressed", NotifyType.StatusMessage);
                            MyMediaElement.Stop();
                            break;

                        case SystemMediaTransportControlsButton.Next:
                            //rootPage.NotifyUser("Next pressed", NotifyType.StatusMessage);
                            // range-checking will be performed in SetNewMediaItem()
                            await SetNewMediaItem(currentItemIndex + 1);
                            break;

                        case SystemMediaTransportControlsButton.Previous:
                            //rootPage.NotifyUser("Previous pressed", NotifyType.StatusMessage);
                            // range-checking will be performed in SetNewMediaItem()
                            await SetNewMediaItem(currentItemIndex - 1);
                            break;

                        // Insert additional case statements for other buttons you want to handle in your app.
                        // Remember that you also need to first enable those buttons via the corresponding
                        // Is****Enabled property on the SystemMediaTransportControls object.
                    }
                }
            });
        }

        /// <summary>
        /// Updates the system UI for media transport controls to display media metadata from the given StorageFile.
        /// </summary>
        /// <param name="mediaFile">
        /// The media file being loaded.  This method will try to extract media metadata from the file for use in
        /// the system UI for media transport controls.
        /// </param>
        private async void UpdateSystemMediaControlsDisplay(int itemIndex) {
            PlayListItem item = Playlist.Items[itemIndex];
            MediaPlaybackType mediaType = MediaPlaybackType.Music;
            var updater = systemMediaControls.DisplayUpdater;

            updater.ClearAll();
            updater.Type = mediaType;
            updater.MusicProperties.Title = mainTitleText.Text = item.Title;
            updater.MusicProperties.AlbumArtist = mainArtistText.Text = item.Artist;
            mainAlbumText.Text = item.Album;

            if (item.ThumbnailStream != null) {
                updater.Thumbnail = RandomAccessStreamReference.CreateFromStream(item.ThumbnailStream);
            }

            if (!isThisPageActive) {
                // User may have navigated away from this scenario page to another scenario page 
                // before the async operation completed.
                return;
            }

            // Finally update the system UI display for media transport controls with the new values currently
            // set in the DisplayUpdater, be it via CopyFrmoFileAsync(), ClearAll(), etc.
            var image = await item.GetOriginalPicture();
            mainCoverImage.Source = image == null ? none : image;
            systemMediaControls.DisplayUpdater.Update();
        }
        #endregion

        #region ItemListView.Event
        private void pageRoot_Tapped(object sender, TappedRoutedEventArgs e) {
            if (!itemListViewIsTapped) {
                if (MyAppBar.IsOpen) MyAppBar.IsSticky = MyAppBar.IsOpen = false;
                if (currentItem != null) itemListView.SelectedItem = currentItem;
            }
            itemListViewIsTapped = false;
        }

        private void itemListView_Tapped(object sender, TappedRoutedEventArgs e) {
            itemListViewIsTapped = true;
        }

        private void itemListView_SelectionChanged(object sender, SelectionChangedEventArgs e) {
            //if (this.UsingLogicalPageNavigation()) this.InvalidateVisualState();
           if (itemListView.SelectedItems.Count == itemListView.Items.Count) {
                SelectAllButton.IsChecked = true;
                SelectAllButton.Label = "Unselect All";
            } else {
                SelectAllButton.IsChecked = false;
                SelectAllButton.Label = "Select All";
            }

            if (itemListView.SelectedItems.Count > 0) {
                var items = itemListView.SelectedItems.Select(c => c as PlayListItem);
                if (items.All(c => c.IsFavorite)) {
                    if (!FavoriteButton.IsEnabled) {
                        FavoriteButtonBoarder.Visibility = Visibility.Collapsed;
                        FavoriteButton.IsEnabled = true;
                    }
                    FavoriteButton.IsChecked = true;
                } else if (items.Any(c => c.IsFavorite)) {
                    FavoriteButton.IsChecked = true;
                    FavoriteButton.IsEnabled = false;
                    FavoriteButtonBoarder.Visibility = Visibility.Visible;
                } else {
                    if (!FavoriteButton.IsEnabled) {
                        FavoriteButtonBoarder.Visibility = Visibility.Collapsed;
                        FavoriteButton.IsEnabled = true;
                    }
                    FavoriteButton.IsChecked = false;
                }
            } else {
                FavoriteButton.IsChecked = false;
                FavoriteButton.IsEnabled = false;
            }
        }

        private async void itemListView_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e) {
            if (e.OriginalSource.GetType().Name == "Grid" ||
                itemListView.SelectedIndex == currentItemIndex ||
                MyAppBar.IsOpen) return;
            await SetNewMediaItem(itemListView.SelectedIndex);
        }

        private void itemListView_Holding(object sender, HoldingRoutedEventArgs e) {
            itemListView.SelectedIndex = -1;
            ShowMyAppBar();

        }

        private void itemListView_RightTapped(object sender, RightTappedRoutedEventArgs e) {
            ShowMyAppBar();
        }
        #endregion

        #region lyricsScrollView.Event
        private void lyricsScrollView_ViewChanging(object sender, ScrollViewerViewChangingEventArgs e) {
            if (!e.IsInertial) {
                lyricFingerDown = true;
                lyricsScrollViewHelper.Visibility = Visibility.Visible;
                lrcTimer.Stop();
            }
        }

        private void lyricsScrollView_ViewChanged(object sender, ScrollViewerViewChangedEventArgs e) {
            if (e.IsIntermediate || !lyricFingerDown) return;
            lyricFingerDown = false;
            lyricsScrollViewHelper.Visibility = Visibility.Collapsed;
            double current = lyricsScrollView.VerticalOffset + 270;
            var height = lyricsPanel.Children
                .Select(c => (TextBlock)c)
                .ToList();

            double sum = 0;
            for (int i = 0; i < height.Count; i++) {
                sum += height[i].ActualHeight;
                if (sum > current) {
                    MyMediaElement.Position = lrcData.ElementAt(i).Key;
                    break;
                }
            }
            lrcTimer.Start();
        }
        #endregion

        #region Media Player
        /// <summary>
        /// Performs all necessary actions (including SystemMediaTransportControls related) of loading a new media item 
        /// in the app for playback.
        /// </summary>
        /// <param name="newItemIndex">index in playFileList of new item to load for playback, can be out of range.</param>
        /// <remarks>
        /// If the newItemIndex argument is out of range, it will be adjusted accordingly to stay in range.
        /// </remarks>
        private async Task SetNewMediaItem(int newItemIndex) {
            if (!systemMediaControls.IsEnabled) systemMediaControls.IsEnabled = true;
            if (!MyMediaElement.AutoPlay) MyMediaElement.AutoPlay = true;
            if (!timelineSlider.IsEnabled) timelineSlider.IsEnabled = true;
            // enable Next button unless we're on last item of the playFileList
            if (newItemIndex >= Playlist.Items.Count - 1) {
                systemMediaControls.IsNextEnabled = false;
                uiNextButton.Visibility = Visibility.Collapsed;
                newItemIndex = Playlist.Items.Count - 1;
            } else {
                systemMediaControls.IsNextEnabled = true;
                uiNextButton.Visibility = Visibility.Visible;
            }

            // enable Previous button unless we're on first item of the playFileList
            if (newItemIndex <= 0) {
                systemMediaControls.IsPreviousEnabled = false;
                uiPrevButton.Visibility = Visibility.Collapsed;
                newItemIndex = 0;
            } else {
                systemMediaControls.IsPreviousEnabled = true;
                uiPrevButton.Visibility = Visibility.Visible;
            }

            // note that the Play, Pause and Stop buttons were already enabled via SetupSystemMediaTransportControls() 
            // invoked during this scenario page's OnNavigateToHandler()

            currentItemIndex = itemListView.SelectedIndex = newItemIndex;
            currentItem = Playlist.Items[newItemIndex];
            itemListView.ScrollIntoView(currentItem, ScrollIntoViewAlignment.Leading);
            StorageFile mediaFile = await currentItem.GetFile();


            lrcTimer.Stop();
            MyMediaElement.Tag = mediaFile.DisplayName;

            IRandomAccessStream stream = null;
            try {
                stream = await mediaFile.OpenAsync(FileAccessMode.Read);
                if (mediaFile.FileType.ToLower() == ".flac") {
                    MemoryStream audioStream = new MemoryStream();
                    using (MemoryStream output = new MemoryStream())
                    using (WavWriter wav = new WavWriter(output))
                        if (IntPtr.Size == 8) {
                            using (FlacReader_x64 flac = new FlacReader_x64(stream.AsStreamForRead(), wav)) {
                                var s = new MemoryStream();
                                flac.Process();
                                output.Seek(0, SeekOrigin.Begin);
                                output.CopyTo(audioStream);
                                audioStream.Seek(0, SeekOrigin.Begin);
                            }
                        } else {
                            using (FlacReader_x86 flac = new FlacReader_x86(stream.AsStreamForRead(), wav)) {
                                var s = new MemoryStream();
                                flac.Process();
                                output.Seek(0, SeekOrigin.Begin);
                                output.CopyTo(audioStream);
                                audioStream.Seek(0, SeekOrigin.Begin);
                            }
                        }
                    stream = audioStream.AsRandomAccessStream();
                }
            } catch (Exception e) {
                // User may have navigated away from this scenario page to another scenario page
                // before the async operation completed.
                if (isThisPageActive) {
                    // If the file can't be opened, for this sample we will behave similar to the case of
                    // setting a corrupted/invalid media file stream on the MediaElement (which triggers a
                    // MediaFailed event). We abort any ongoing playback by nulling the MediaElement's
                    // source. The user must press Next or Previous to move to a different media item,
                    // or use the file picker to load a new set of files to play.
                    MyMediaElement.Source = null;
                }
            }
            // User may have navigated away from this scenario page to another scenario page
            // before the async operation completed. Check to make sure page is still active.
            if (!isThisPageActive) {
                return;
            }
            if (stream != null) {
                // We're about to change the MediaElement's source media, so put ourselves into a
                // "changing media" state. We stay in that state until the new media is playing,
                // loaded (if user has currently paused or stopped playback), or failed to load.
                // At those points we will call OnChangingMediaEnded().
                string MIMEtype = mediaFile.ContentType;
                if (MIMEtype == "audio/flac") MIMEtype = "audio/wav";
                MyMediaElement.SetSource(stream, MIMEtype);
            } else {
                stateTextBlock.Text = "Can not open media";
            }
            UpdateSystemMediaControlsDisplay(newItemIndex);
            lyricsPanel.Children.Clear();
        }

        void lrcTimer_Tick(object sender, object e) {
            if (lrcData == null) return;
            var position = MyMediaElement.Position;
            var up = lrcData.TakeWhile(c => c.Key <= position);
            int count = up.Count();
            if (count == lastLrc || count <= 0) return;
            if (lastLrc < 0 || lastLrc >= lrcData.Count) lastLrc = 0;
            double height = lyricsPanel.Children
                .Take(count - 1)
                .Select(c => (TextBlock)c)
                .Sum(c => c.ActualHeight);

            if (lyricsPanel.Children.Count == 0) return;
            TextBlock last = lyricsPanel.Children.ElementAt(lastLrc) as TextBlock;
            last.Style = this.Resources["LyricTextStyle"] as Style;
            TextBlock current = lyricsPanel.Children.ElementAt(count) as TextBlock;
            current.Style = this.Resources["LyricActivateTextStyle"] as Style;
            lastLrc = count;

            lyricsScrollView.ChangeView(0, height - 240, lyricsScrollView.ZoomFactor);
        }
        #endregion

        #region MediaTransport
        private async void uiButton_Click(object sender, RoutedEventArgs e) {
            if (currentItem == null) {
                if (itemListView.SelectedItem != null) {
                    currentItemIndex = itemListView.SelectedIndex;
                    await SetNewMediaItem(currentItemIndex);
                }
                return;
            }
            var _sender = sender as Button;
            switch (_sender.Name) {
                case "uiPlayPauseButton":
                    if ((string)uiPlayPauseButton.Content == "\uE102") PlayMediaElement();
                    else MyMediaElement.Pause();
                    break;
                case "uiStopButton":
                    MyMediaElement.Stop();
                    break;
                case "uiNextButton":
                    await SetNewMediaItem(currentItemIndex + 1);
                    break;
                case "uiPrevButton":
                    await SetNewMediaItem(currentItemIndex - 1);
                    break;
            }
        }

        private void VolumeSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e) {
            if (MyMediaElement.IsMuted) {
                if (VolumeSlider.Value == 0) return;
                else {
                    MyMediaElement.IsMuted = false;
                    MuteButton.Text = "\uE15D";
                }
            }
            MyMediaElement.Volume = VolumeSlider.Value / 100;
        }

        private void MuteButton_Tapped(object sender, TappedRoutedEventArgs e) {
            if (MyMediaElement.IsMuted) {
                MuteButton.Text = "\uE15D";
                MyMediaElement.IsMuted = false;
                VolumeSlider.Value = MyMediaElement.Volume * 100;
            } else {
                MuteButton.Text = "\uE198";
                MyMediaElement.IsMuted = true;
                VolumeSlider.Value = 0;
            }
            e.Handled = true;
        }

        private void timelineSlider_PointerCaptureLost(object sender, PointerRoutedEventArgs e) {
            MyMediaElement.Position = TimeSpan.FromSeconds(timelineSlider.Value);
            sliderPressed = false;
        }

        private void timelineSlider_PointerEntered(object sender, PointerRoutedEventArgs e) {
            sliderPressed = true;
        }

        private void timelineSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e) {
            if (sliderPressed) {
                var newTime = TimeSpan.FromSeconds(e.NewValue);
                MyMediaElement.Position = newTime;
                MediaTimeNowText.Text = newTime.ToString("mm\\:ss");
            }
        }
        #endregion

        private void pageRoot_SizeChanged(object sender, SizeChangedEventArgs e) {
            itemDetailGrid.Width = itemDetail.ActualWidth;
        }
    }
}
