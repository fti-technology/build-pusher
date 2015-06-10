using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using BuildDataDriver.Interfaces;
using BuildDataDriver.tools;
using FTIPusher.Util;
using Microsoft.TeamFoundation.Build.Client;
using NLog;
using System.Windows.Forms;
using System.Windows.Threading;
using Parago.Windows;


namespace BuildDataRunner
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private static object _itemsLock = new object();
        private readonly TfsOps _tfsOps;
        private const string _databaseSubDir = "FTIDeployer";
        private readonly string _dataBaseLogDirPath;
        private readonly ServiceOptionsRoot _readJsonConfigOptions;
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
        private Uri _tfsPath = null;
        public int LastSelectDropIndex { get; set; }
        public static string UpdateMessage = "Updating path details....";
        // ServiceOptionsRoot serviceOptions
        public MainWindow()
        {
            LastSelectDropIndex = -1;
            PresentationTraceSources.Refresh();
            PresentationTraceSources.DataBindingSource.Listeners.Add(new ConsoleTraceListener());
            PresentationTraceSources.DataBindingSource.Listeners.Add(new DebugTraceListener());
            PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Warning | SourceLevels.Error;

            InitializeComponent();

            _dataBaseLogDirPath = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonDocuments), _databaseSubDir);
            _readJsonConfigOptions = ServiceOptions.ReadJsonConfigOptions(Logger);
            if (_readJsonConfigOptions == null)
                return;
            _tfsPath = new Uri(_readJsonConfigOptions.BuildServer);

            GetTfsBranches();
            
        }
        
        public IEnumerable<string> DropPaths { get; set; }

        public ObservableCollection<IBuildDetail> UpdateBuildDetails(string TFSProjectPath, Uri tfsUri)
        {
            ObservableCollection<IBuildDetail> buildDetails = new ObservableCollection<IBuildDetail>();
            var lastBuildDetails = TfsOps.GetLastBuildDetails(TFSProjectPath, _tfsPath);
            foreach (var lastBuildDetail in lastBuildDetails)
            {
                buildDetails.Add(lastBuildDetail);
            }

            return buildDetails;
        }

        public ObservableCollection<IBuildDetail> MyBuildDetails
        {
            get { return (ObservableCollection<IBuildDetail>)GetValue(MyBuildDetailsProperty); }
            set { SetValue(MyBuildDetailsProperty, value); }
        }

        // Using a DependencyProperty as the backing store for MyBuildDetails.  This enables animation, styling, binding, etc...
        public static readonly DependencyProperty MyBuildDetailsProperty =
            DependencyProperty.Register("MyBuildDetails", typeof (ObservableCollection<IBuildDetail>),
                typeof (MainWindow),
                new UIPropertyMetadata(new ObservableCollection<IBuildDetail>(), PropertyChangedCallback));

        private static void PropertyChangedCallback(DependencyObject dependencyObject,
            DependencyPropertyChangedEventArgs dependencyPropertyChangedEventArgs)
        {
            var mainWindow = dependencyObject as MainWindow;
            if (mainWindow != null)
            {
                var old = dependencyPropertyChangedEventArgs.OldValue as ObservableCollection<IBuildDetail>;
                if (old != null)
                    old.CollectionChanged -= mainWindow.NOnCollectionChanged;


                var n = dependencyPropertyChangedEventArgs.NewValue as ObservableCollection<IBuildDetail>;
                if (n != null)
                {
                    n.CollectionChanged += mainWindow.NOnCollectionChanged;




                    mainWindow.NOnCollectionChanged((object)mainWindow, null);
                }
            }
        }

        private void NOnCollectionChanged(object sender, NotifyCollectionChangedEventArgs notifyCollectionChangedEventArgs)
        {
            UpdateDownloadDetails();
        }
        
        public IEnumerable<string> MyDropPaths
        {
            get { return (IEnumerable<string>)GetValue(MyDropPathsProperty); }
            set { SetValue(MyDropPathsProperty, value); }
        }

        // Using a DependencyProperty as the backing store for MyDropPaths.  This enables animation, styling, binding, etc...
        public static readonly DependencyProperty MyDropPathsProperty =
            DependencyProperty.Register("MyDropPaths", typeof(IEnumerable<string>), typeof(MainWindow), new PropertyMetadata(null));

        private async Task<ObservableCollection<IBuildDetail>> GetBuildDetailsAsync(string TFSProjectPath)
        {
            var res = await Task.Run(() => UpdateBuildDetails(TFSProjectPath, _tfsPath));
            return res;
        }

        public void GetBuildDetails(string TFSProjectPath)
        {

            var sc = SynchronizationContext.Current;

            // Using FromCurrentSynchronizationContext is a shorthand pattern for running a post on SynchronizationContext 
            GetBuildDetailsAsync(TFSProjectPath)
                .ContinueWith(r =>
                {
                    MyBuildDetails = r.Result;
                    BuildDataGrid.DataContext = r.Result;

                },TaskScheduler.FromCurrentSynchronizationContext());

            DownLoadBtn.IsEnabled = true;
            
        }

        private void UpdateDownloadDetails()//ObservableCollection<IBuildDetail> listOfBuildDetails)
        {
            DropLocations.IsEnabled = false;
            var treeItem = TreeViewBranches.SelectedItem as TreeViewItem;
            if (treeItem != null)
            {
                var treeValue = treeItem.Header.ToString();

                if (!string.IsNullOrEmpty(treeValue))
                {
                    var subBranch = treeValue.Substring(treeValue.LastIndexOf('/')).Replace("/", "");
                    List<string> update = new List<string>(){UpdateMessage};
                    StepsListView.DataContext = null;                    

                    StepsListView.DataContext = update;

                    TaskScheduler uiTaskScheduler = TaskScheduler.FromCurrentSynchronizationContext();

                    TfsOps.DownLoadPathsFromDropPathAsync(MyBuildDetails, subBranch).ContinueWith(t =>
                        {
                            StepsListView.DataContext = t.Result;
                            DropLocations.IsEnabled = true;
                        }, uiTaskScheduler);
                }
                else
                {
                    StepsListView.DataContext = null;
                }
            }
        }

        private void GetTfsBranches()
        {
            TreeViewItem treeViewItem = new TreeViewItem {Header = "Branches"};
            var branchList = TfsOps.GetBranchList(_readJsonConfigOptions.BuildsToWatch.ToList<IBuildsToWatch>(), _tfsPath);
            foreach (var branch in branchList)
            {
                treeViewItem.Items.Add(new TreeViewItem() {Header = branch});
            }
            TreeViewBranches.Items.Add(treeViewItem);
        }

        private void treeViewItem_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            DownLoadBtn.IsEnabled = false;
            var item = e.OriginalSource as TextBlock;
            if (item == null)
            { 
                return; 
            }
            else
            {
                var s = item.Text;
                GetBuildDetails(s);
            }
          
            e.Handled = true;
        }

      
        
        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            System.Windows.Data.CollectionViewSource iBuildDetailViewSource = ((System.Windows.Data.CollectionViewSource)(this.FindResource("iBuildDetailViewSource")));
            // Load data by setting the CollectionViewSource.Source property:
            iBuildDetailViewSource.Source = MyBuildDetails;
        }

        private void DownLoadBtn_Click(object sender, RoutedEventArgs e)
        {
            var treeItem = TreeViewBranches.SelectedItem as TreeViewItem;
            if (treeItem != null)
            {
                var treeValue = treeItem.Header.ToString();

                if (!string.IsNullOrEmpty(treeValue))
                {
                    var dialog = new System.Windows.Forms.FolderBrowserDialog();
                    dialog.Description = "Select a folder to save the drop components into";
                    System.Windows.Forms.DialogResult result = dialog.ShowDialog();
                    if (result == System.Windows.Forms.DialogResult.OK)
                    {
                        //dialog.SelectedPath

                        var subBranch = treeValue.Substring(treeValue.LastIndexOf('/')).Replace("/", "");
                        var dropsPaths = TfsOps.DownLoadPathsFromDropPath(BuildDataGrid.Items.Cast<IBuildDetail>(), subBranch);
                        long count = 1;
                        Object thisLock = new Object();
                        ProgressDialogResult resultProgress = ProgressDialog.Execute(this, "Downloading", () =>
                        {
                             long dropPathsCount  = dropsPaths.Count();

                            Task.Factory.StartNew(() =>
                            {
                                Parallel.ForEach(dropsPaths, new ParallelOptions {MaxDegreeOfParallelism = 4},
                                    item =>
                                    {
                                        ProgressDialog.Current.ReportWithCancellationCheck(
                                            "Starting copy.... \n Copying {0}", item);
                                        FileUtils.RoboCopyFile(item, dialog.SelectedPath);

                                        lock (thisLock)
                                        {
                                            ProgressDialog.Current.Report("Completed step {0}/{1}... \nCopying {2} ",
                                                Interlocked.Increment(ref count), dropPathsCount, item);
                                        }
                                    });
                            }).Wait();
                        }, ProgressDialogSettings.WithSubLabelAndCancel);


                        if (resultProgress.Cancelled)
                            System.Windows.Forms.MessageBox.Show("Copy cancelled.");
                        else if (resultProgress.OperationFailed)
                            System.Windows.Forms.MessageBox.Show("Copy failed.");
                        else
                            System.Windows.Forms.MessageBox.Show("Copy successfully executed.");
                    }

                }
            }
        }

        private void DropLocations_Loaded(object sender, RoutedEventArgs e)
        {
            var comboBox = sender as System.Windows.Controls.ComboBox;
            if (comboBox == null)
                return;
            
            List<ComboBoxItem> comboBoxItems = new List<ComboBoxItem>();
          
            ComboBoxItem item = new ComboBoxItem();
            item.Content = "TFS Drop";
            item.IsEnabled = true;
            item.IsSelected = true;
            comboBoxItems.Add(item);

            //dropsList.Add("TFS Drop");  // Default

            ////var selectedItem = TreeViewBranches.SelectedItem;

            if (_readJsonConfigOptions.FTPLocations.Any())
            {
                foreach (var ftpLocation in _readJsonConfigOptions.FTPLocations)
                {
                    if (ftpLocation.InternalSharePath != null && !string.IsNullOrEmpty(ftpLocation.InternalSharePath))
                    {
                        comboBoxItems.Add(new ComboBoxItem()
                        {
                            Content = ftpLocation.InternalSharePath,
                            IsEnabled = false,
                            IsSelected = false
                        });
                    }
                }

            }

            comboBox.ItemsSource = comboBoxItems;
            comboBox.SelectedIndex = 0;
            LastSelectDropIndex = comboBox.SelectedIndex;
            
        }

        private void DropLocations_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var comboBox = sender as System.Windows.Controls.ComboBox;
            if (comboBox == null)
                return;

            if(comboBox.SelectedIndex == 0)
                


            LastSelectDropIndex = comboBox.SelectedIndex;
            //TreeViewBranches.Items.Clear();
            ////var branchList = TfsOps.GetBranchList(_readJsonConfigOptions.BuildsToWatch.ToList<IBuildsToWatch>(), _tfsPath);
            //foreach (var branch in branchList)
            //{
            //    treeViewItem.Items.Add(new TreeViewItem() { Header = branch });
            //}
            //TreeViewBranches.Items.Add(treeViewItem);
        }

        private void StepsListView_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {

            var x = 1;
            if (e.NewValue == null)
            {


            }
            else
            {
                var strval = e.NewValue as List<string>;
                if (strval == null ) 
                    return;

                if (strval.Any())
                {
                    if (String.CompareOrdinal(strval[0], UpdateMessage) == 0)
                    {
                        return;
                    }

                    // Go to tree, get branch
                    // Get locations from options drop down
                    // Build paths
                    // search
                    // enable found items
                }

            }
        }

    }

    public class DebugTraceListener : TraceListener
    {
        public override void Write(string message)
        {
        }

        public override void WriteLine(string message)
        {
         //   Debugger.Break();
        }
    }
}
