using Microsoft.Win32;
using RecentFiles.Commands;
using RecentFiles.Model;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Input;

namespace RecentFiles.ViewModel
{
	public class MainViewModel : BaseModel
	{
		private Thread _scanThread = null;
		private bool _scanThreadAbort = false;
		public string RootFolder { get; set; } = @"C:\Users";
		public string Filter { get; set; } = "*.*";
		public bool SkipWindowsFolder { get; set; } = true;
		public DateTime? DateFrom { get; set; } = DateTime.Now.AddDays(-7);
		public DateTime? DateTo { get; set; }
		public ObservableCollection<FileInfo> Files { get; private set; } = new ObservableCollection<FileInfo>();
		public ICommand ScanCommand { get; set; }
		public bool ScanIsEnabled { get; set; } = true;
		public bool StopIsEnabled { get; set; } = true;
		public ICommand StopCommand { get; set; }
		public ICommand ExportCommand { get; set; }

		public MainViewModel()
		{
			ScanCommand = new RelayCommand(StartScan);
			StopCommand = new RelayCommand(Stop);
			ExportCommand = new RelayCommand(Export);
		}

		private void StartScan(object o)
		{
			if (_scanThread?.IsAlive == true)
				return; // Already scanning

			_scanThreadAbort = false;
			ScanIsEnabled = false;
			StopIsEnabled = true;
			try
			{
				Files.Clear();
				_scanThread = new Thread(ScanThreadMethod);
				_scanThread.Start();
			}
			catch (Exception ex)
			{
				MessageBox.Show(ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
			}
		}

		private void ScanThreadMethod()
		{
			try
			{
				var filesFound = new List<FileInfo>();
				var windowsFolder = Path.Combine(RootFolder, "Windows");
				var updateStopwatch = new Stopwatch();
				updateStopwatch.Start();

				foreach (var file in GetFiles(RootFolder, Filter))
				{
					if (_scanThreadAbort)
						return;

					// Skip Windows folder contents
					if (SkipWindowsFolder && file.StartsWith(windowsFolder, StringComparison.OrdinalIgnoreCase))
						continue;

					// Filter by File Modified Date
					var lastWriteTime = File.GetLastWriteTime(file);
					if ((DateFrom.HasValue && lastWriteTime < DateFrom.Value) || (DateTo.HasValue && lastWriteTime > DateTo.Value))
						continue;

					// File matched conditions
					filesFound.Add(new FileInfo(file));

					// Update UI every second with a batch of files
					if (updateStopwatch.Elapsed.TotalSeconds > 1)
					{
						Application.Current.Dispatcher.Invoke(() =>
						{
							// Execute this code on the (main) UI thread
							foreach (var f in filesFound)
								Files.Add(f);
						});
						filesFound.Clear();
						updateStopwatch.Restart();
					}
				}
				// Add remaining files to the UI
				Application.Current.Dispatcher.Invoke(() =>
				{
					foreach (var f in filesFound)
						Files.Add(f);
				});
			}
			catch (Exception ex)
			{
				MessageBox.Show(ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
			}
			finally
			{
				ScanIsEnabled = true;
				StopIsEnabled = false;
			}
		}

		/// <summary>
		/// Safely lists all files recursively, so continues scanning on AccessDenied exception.
		/// </summary>
		/// <param name="root"></param>
		/// <param name="searchPattern"></param>
		/// <returns></returns>
		/// <exception cref="Exception"></exception>
		private IEnumerable<string> GetFiles(string root, string searchPattern)
		{
			Stack<string> pending = new Stack<string>();
			pending.Push(root);
			while (pending.Count != 0)
			{
				if (_scanThreadAbort)
					throw new Exception("Aborted");

				var path = pending.Pop();
				string[] next = null;
				try
				{
					next = Directory.GetFiles(path, searchPattern);
				}
				catch { } // Access Denied

				if (next != null && next.Length != 0)
				{
					foreach (var file in next)
					{
						if (_scanThreadAbort)
							throw new Exception("Aborted");
						yield return file;
					}
				}
				try
				{
					next = Directory.GetDirectories(path);
					foreach (var subdir in next)
					{
						if (_scanThreadAbort)
							throw new Exception("Aborted");
						pending.Push(subdir);
					}
				}
				catch { } // Access Denied
			}
		}

		private void Stop(object o)
		{
			if (_scanThread is null)
				return;
			_scanThreadAbort = true;
			StopIsEnabled = false;
			_scanThread.Join();
		}

		private void Export(object o)
		{
			var saveFileDialog = new SaveFileDialog();
			saveFileDialog.Filter = "Text files (*.txt)|*.txt";

			if (saveFileDialog.ShowDialog() == true)
			{
				File.WriteAllLines(saveFileDialog.FileName, Files.Select(x => String.Join("\t", x.FullName, x.LastWriteTime, x.Length)));
			}
		}
	}
}
