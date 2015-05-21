using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
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
        private ServiceOptionsRoot _readJsonConfigOptions;
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
        private Uri _tfsPath = null;
        // ServiceOptionsRoot serviceOptions
        public MainWindow()
        {

            InitializeComponent();
            BuildDetails = new ObservableCollection<IBuildDetail>();
            BindingOperations.EnableCollectionSynchronization(BuildDetails, _itemsLock);

            _dataBaseLogDirPath = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonDocuments), _databaseSubDir);
            _readJsonConfigOptions = ServiceOptions.ReadJsonConfigOptions(Logger);
            if (_readJsonConfigOptions == null)
                return;
            _tfsPath = new Uri(_readJsonConfigOptions.BuildServer);
            //_tfsOps = new TfsOps(_dataBaseLogDirPath, _readJsonConfigOptions.BuildServer);
            GetTFSBranches();

            
        }


        public ObservableCollection<IBuildDetail> BuildDetails { get; set; }


        public void UpdateBuildDetails(string TFSProjectPath, Uri tfsUri)
        {
            //TfsOps.GetLastBuildDetails(TFSProjectPath, _tfsPath).ForEach(x => BuildDetails.Add(x));
            var lastBuildDetails = TfsOps.GetLastBuildDetails(TFSProjectPath, _tfsPath);
            foreach (var lastBuildDetail in lastBuildDetails)
            {
                BuildDetails.Add(lastBuildDetail);
            }
        }

        public void GetBuildDetails(string TFSProjectPath)
        {
            BuildDetails.Clear();

            Task.Factory.StartNew(() =>
            {
                UpdateBuildDetails(TFSProjectPath, _tfsPath);
            }).Wait();  // hangs up the UI a bit, but prevents selection in tree
            DownLoadBtn.IsEnabled = true;
        }
       
        private void GetTFSBranches()
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
            
          //  EnableTreeNode(TreeViewBranches);
        }

      
        
        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            System.Windows.Data.CollectionViewSource iBuildDetailViewSource = ((System.Windows.Data.CollectionViewSource)(this.FindResource("iBuildDetailViewSource")));
            // Load data by setting the CollectionViewSource.Source property:
            iBuildDetailViewSource.Source = BuildDetails;
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
    }
}
