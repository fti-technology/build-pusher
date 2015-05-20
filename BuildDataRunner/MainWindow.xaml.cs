using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
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
            });
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
            iBuildDetailViewSource.Source = BuildDetails;
        }



        //public static readonly DependencyProperty BuildDetailsProperty = DependencyProperty.Register("BuildDetails", typeof(ObservableCollection<IBuildDetail>
        
    }
}
