using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Win32;
using SchoolScheduler.Common.Models;

namespace SchoolScheduler.GUI.Views
{
    public partial class ConfigurationWindow : Window
    {
        public ScheduleConfiguration Configuration { get; private set; }

        public ConfigurationWindow()
        {
            InitializeComponent();
            Configuration = new ScheduleConfiguration();
            InitializeControls();
            LoadConfigurationToUI();
        }

        public ConfigurationWindow(ScheduleConfiguration config) : this()
        {
            Configuration = config ?? new ScheduleConfiguration();
            LoadConfigurationToUI();
        }

        private void InitializeControls()
        {
            // Popola ore di inizio
            for (int i = 7; i <= 9; i++)
            {
                cmbStartHour.Items.Add(i.ToString("00"));
            }
            cmbStartHour.SelectedIndex = 1; // 08
        }

        private void LoadConfigurationToUI()
        {
            // Giorni di scuola
            if (Configuration.SchoolDays == 5)
            {
                rbFiveDays.IsChecked = true;
                pnlExcludedDay.IsEnabled = true;
            }
            else
            {
                rbSixDays.IsChecked = true;
                pnlExcludedDay.IsEnabled = false;
            }

            // Giorno escluso
            if (Configuration.ExcludedDay.HasValue)
            {
                foreach (var item in cmbExcludedDay.Items)
                {
                    if (item is System.Windows.Controls.ComboBoxItem cbi &&
                        cbi.Tag?.ToString() == Configuration.ExcludedDay.Value.ToString())
                    {
                        cmbExcludedDay.SelectedItem = item;
                        break;
                    }
                }
            }

            // Orario
            cmbStartHour.Text = Configuration.DefaultStartTime.Hours.ToString("00");
            cmbStartMinute.Text = Configuration.DefaultStartTime.Minutes.ToString("00");

            // Durata lezione
            foreach (var item in cmbLessonDuration.Items)
            {
                if (item is System.Windows.Controls.ComboBoxItem cbi &&
                    cbi.Tag?.ToString() == Configuration.LessonDurationMinutes.ToString())
                {
                    cmbLessonDuration.SelectedItem = item;
                    break;
                }
            }

            // Ore giornaliere
            cmbMaxDailyHours.Text = Configuration.MaxDailyHours.ToString();
            cmbMinDailyHours.Text = Configuration.MinDailyHours.ToString();
        }

        private void SaveConfigurationFromUI()
        {
            // Giorni di scuola
            Configuration.SchoolDays = rbFiveDays.IsChecked == true ? 5 : 6;

            // Giorno escluso
            if (Configuration.SchoolDays == 5 && cmbExcludedDay.SelectedItem is System.Windows.Controls.ComboBoxItem cbi)
            {
                if (Enum.TryParse<DayOfWeek>(cbi.Tag?.ToString(), out var day))
                {
                    Configuration.ExcludedDay = day;
                }
            }
            else
            {
                Configuration.ExcludedDay = null;
            }

            // Orario
            int.TryParse(cmbStartHour.Text, out int hour);
            int.TryParse(cmbStartMinute.Text, out int minute);
            Configuration.DefaultStartTime = new TimeSpan(hour, minute, 0);

            // Durata lezione
            if (cmbLessonDuration.SelectedItem is System.Windows.Controls.ComboBoxItem durationItem)
            {
                int.TryParse(durationItem.Tag?.ToString(), out int duration);
                Configuration.LessonDurationMinutes = duration;
            }

            // Ore giornaliere
            int.TryParse(cmbMaxDailyHours.Text, out int maxHours);
            int.TryParse(cmbMinDailyHours.Text, out int minHours);
            Configuration.MaxDailyHours = maxHours;
            Configuration.MinDailyHours = minHours;
        }

        private void FiveDays_Checked(object sender, RoutedEventArgs e)
        {
            if (pnlExcludedDay != null)
                pnlExcludedDay.IsEnabled = true;
        }

        private void SixDays_Checked(object sender, RoutedEventArgs e)
        {
            if (pnlExcludedDay != null)
                pnlExcludedDay.IsEnabled = false;
        }

        private void SaveConfig_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new SaveFileDialog
            {
                Filter = "JSON Files|*.json|All Files|*.*",
                DefaultExt = "json",
                FileName = "schedule_config.json"
            };

            if (dialog.ShowDialog() == true)
            {
                SaveConfigurationFromUI();
                Configuration.SaveToFile(dialog.FileName);
                txtConfigStatus.Text = "Configurazione salvata con successo!";
            }
        }

        private void LoadConfig_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Filter = "JSON Files|*.json|All Files|*.*",
                DefaultExt = "json"
            };

            if (dialog.ShowDialog() == true)
            {
                Configuration = ScheduleConfiguration.LoadFromFile(dialog.FileName);
                LoadConfigurationToUI();
                txtConfigStatus.Text = "Configurazione caricata con successo!";
            }
        }

        private void DefaultConfig_Click(object sender, RoutedEventArgs e)
        {
            Configuration = new ScheduleConfiguration();
            LoadConfigurationToUI();
            txtConfigStatus.Text = "Configurazione ripristinata ai valori di default";
        }

        private void OK_Click(object sender, RoutedEventArgs e)
        {
            SaveConfigurationFromUI();
            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
