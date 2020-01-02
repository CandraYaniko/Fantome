﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Input;
using Fantome.ModManagement;
using Fantome.ModManagement.IO;
using Fantome.MVVM.Commands;
using Fantome.MVVM.ViewModels;
using Fantome.UserControls.Dialogs;
using Fantome.Utilities;
using LoLCustomSharp;
using MaterialDesignThemes.Wpf;
using Microsoft.Win32;
using Microsoft.WindowsAPICodePack.Dialogs;
using Serilog;
using Application = System.Windows.Application;
using MessageBox = System.Windows.MessageBox;

namespace Fantome
{
    public partial class MainWindow : Window
    {
        public const string LOGS_FOLDER = "Logs";

        public ModListViewModel ModList { get => this.ModsListBox.DataContext as ModListViewModel; }

        private ModManager _modManager;
        private OverlayPatcher _patcher;
        private NotifyIcon _notifyIcon;

        public ICommand RunSettingsDialogCommand => new RelayCommand(RunSettingsDialog);
        public ICommand RunCreateModDialogCommand => new RelayCommand(RunCreateModDialog);

        public MainWindow()
        {
            CheckWindowsVersion();
            CheckForExistingProcess();

            Config.Load();
            InitializeLogger();
            CreateWorkFolders();
            StartPatcher();
            InitializeComponent();
            InitializeModManager();
            BindMVVM();
            InitializeTrayIcon();
        }

        private void InitializeModManager()
        {
            Log.Information("Initializing Mod Manager");
            this._modManager = new ModManager();
        }
        private void StartPatcher()
        {
            Log.Information("Starting League Patcher");
            string overlayDirectory = (Directory.GetCurrentDirectory() + @"\" + ModManager.OVERLAY_FOLDER + @"\").Replace('\\', '/');
            this._patcher = new OverlayPatcher();
            this._patcher.Start(overlayDirectory);
        }
        private void CreateWorkFolders()
        {
            Log.Information("Creating Work folders");
            Directory.CreateDirectory(ModManager.MOD_FOLDER);
            Directory.CreateDirectory(ModManager.OVERLAY_FOLDER);
            Directory.CreateDirectory(LOGS_FOLDER);
        }
        private void BindMVVM()
        {
            Log.Information("Binding View Models");
            this.ModsListBox.DataContext = new ModListViewModel(this._modManager);
            this.ButtonCreateMod.DataContext = this;
            this.SettingsButton.DataContext = this;
        }
        private void InitializeTrayIcon()
        {
            Log.Information("Initializing Tray Icon");

            Stream iconStream = Application.GetResourceStream(new Uri("pack://application:,,,/Fantome;component/Resources/fantome.ico")).Stream;
            this._notifyIcon = new NotifyIcon()
            {
                Visible = false,
                Icon = new System.Drawing.Icon(iconStream)
            };

            this._notifyIcon.DoubleClick += delegate (object sender, EventArgs args)
            {
                Show();
                this.WindowState = WindowState.Normal;
            };
        }
        private void CheckWindowsVersion()
        {
            OperatingSystem operatingSystem = Environment.OSVersion;
            if (operatingSystem.Version.Major != 10)
            {
                if (MessageBox.Show("You need to be running Windows 10 in order to use Fantome", "", MessageBoxButton.OK, MessageBoxImage.Error) == MessageBoxResult.OK)
                {
                    Application.Current.Shutdown();
                }
            }
        }
        private void RemoveExecutableAdminPrivilages()
        {
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows NT\CurrentVersion\AppCompatFlags\Layers\", true))
            {
                if (key != null)
                {
                    List<string> toRemove = key.GetValueNames().Where(x => x.StartsWith(this._modManager.LeagueFolder)).ToList();
                    foreach (string value in toRemove)
                    {
                        Log.Information("Removing Admin privialige from: " + value);
                        key.DeleteValue(value);
                    }
                }
            }

            using (RegistryKey key = Registry.LocalMachine.OpenSubKey(@"Software\Microsoft\WindowsNT\CurrentVersion\AppCompatFlags\Layers\", true))
            {
                if (key != null)
                {
                    List<string> toRemove = key.GetValueNames().Where(x => x.StartsWith(this._modManager.LeagueFolder)).ToList();
                    foreach (string value in toRemove)
                    {
                        Log.Information("Removing Admin privialige from: " + value);
                        key.DeleteValue(value);
                    }
                }
            }
        }
        private void InitializeLogger()
        {
            string logPath = string.Format(@"{0}\FantomeLog - {1}.txt", LOGS_FOLDER, DateTime.Now.ToString("dd.MM.yyyy - HH-mm-ss"));
            string loggingPattern = Config.Get<string>("LoggingPattern");

            Log.Logger = new LoggerConfiguration()
                .WriteTo.File(logPath, outputTemplate: loggingPattern)
                .CreateLogger();
        }
        private void CheckForExistingProcess()
        {
            foreach (Process process in Process.GetProcessesByName("Fantome").Where(x => x.Id != Process.GetCurrentProcess().Id))
            {
                if (process.MainModule.ModuleName == "Fantome.exe")
                {
                    if (MessageBox.Show("There is alrady a running instance of Fantome.\nPlease check your tray.", "", MessageBoxButton.OK, MessageBoxImage.Error) == MessageBoxResult.OK)
                    {
                        Application.Current.Shutdown();
                    }
                }
            }
        }

        private void AddMod(object sender, RoutedEventArgs e)
        {
            CommonOpenFileDialog dialog = new CommonOpenFileDialog("Choose the ZIP file of your mod")
            {
                Multiselect = false
            };

            dialog.Filters.Add(new CommonFileDialogFilter("ZIP Files", ".zip"));

            if (dialog.ShowDialog() == CommonFileDialogResult.Ok)
            {
                string modName = Path.GetFileName(dialog.FileName);
                string modPath = string.Format(@"{0}\{1}", ModManager.MOD_FOLDER, modName);

                if (!File.Exists(modPath))
                {
                    Log.Information("Copying Mod: {0} to {1}", dialog.FileName, modPath);
                    File.Copy(dialog.FileName, modPath, true);
                }

                Log.Information("Loading Mod: {0}", modPath);
                ModFile mod = new ModFile(modPath);
                this.ModList.AddMod(mod, true);
            }
        }

        private async void RunSettingsDialog(object o)
        {
            SettingsDialog dialog = new SettingsDialog
            {
                DataContext = new SettingsViewModel()
            };


            object result = await DialogHost.Show(dialog, "RootDialog", (dialog.DataContext as SettingsViewModel).ClosingEventHandler);
        }
        private async void RunCreateModDialog(object o)
        {
            CreateModDialog dialog = new CreateModDialog
            {
                DataContext = new CreateModDialogViewModel(this.ModList)
            };


            object result = await DialogHost.Show(dialog, "RootDialog", (dialog.DataContext as CreateModDialogViewModel).ClosingEventHandler);
        }
        private async void DialogHost_Loaded(object sender, EventArgs e)
        {
            DialogHelper.RootDialog = this.RootDialog;

            string leagueLocation = Config.Get<string>("LeagueLocation");
            if (string.IsNullOrEmpty(leagueLocation))
            {
                await GetLeagueLocation();
                Config.Set("LeagueLocation", leagueLocation);
            }

            this._modManager.AssignLeague(leagueLocation);
            this.ModList.Sync();

            RemoveExecutableAdminPrivilages();

            async Task GetLeagueLocation()
            {
                LeagueLocationDialog dialog = new LeagueLocationDialog()
                {
                    DataContext = new LeagueLocationDialogViewModel()
                };

                object result = await DialogHost.Show(dialog, "RootDialog");

                if ((bool)result == true)
                {
                    leagueLocation = dialog.ViewModel.LeagueLocation;
                }
            }
        }

        protected override void OnStateChanged(EventArgs e)
        {
            if (this.WindowState == WindowState.Minimized)
            {
                this._notifyIcon.Visible = true;
                Hide();
            }
            else if (this.WindowState == WindowState.Normal)
            {
                this._notifyIcon.Visible = false;
            }

            base.OnStateChanged(e);
        }

        private void Window_Closing(object sender, CancelEventArgs e)
        {
            this._notifyIcon.Dispose();
        }
    }
}
