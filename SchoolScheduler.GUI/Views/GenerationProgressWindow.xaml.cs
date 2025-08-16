using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Threading;
using System.Windows;
using SchoolScheduler.GUI.Services;

namespace SchoolScheduler.GUI.Views
{
    public partial class GenerationProgressWindow : Window
    {
        private CancellationTokenSource? _cancellationTokenSource;

        public GenerationProgressWindow()
        {
            InitializeComponent();
        }

        public void SetCancellationTokenSource(CancellationTokenSource cts)
        {
            _cancellationTokenSource = cts;
        }

        public void UpdateProgress(ScheduleGenerator.GenerationProgress progress)
        {
            Dispatcher.Invoke(() =>
            {
                progressBar.Value = progress.Percentage;
                txtPercentage.Text = $"{progress.Percentage}%";
                txtStatus.Text = progress.Message;

                // Aggiungi al log
                txtLog.AppendText($"[{DateTime.Now:HH:mm:ss}] {progress.Message}\n");
                txtLog.ScrollToEnd();

                if (progress.IsCompleted)
                {
                    btnCancel.IsEnabled = false;
                    btnOK.IsEnabled = true;

                    if (progress.HasErrors)
                    {
                        txtStatus.Foreground = System.Windows.Media.Brushes.Red;
                    }
                    else
                    {
                        txtStatus.Foreground = System.Windows.Media.Brushes.Green;
                    }
                }
            });
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            _cancellationTokenSource?.Cancel();
            DialogResult = false;
            Close();
        }

        private void OK_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }
    }
}
